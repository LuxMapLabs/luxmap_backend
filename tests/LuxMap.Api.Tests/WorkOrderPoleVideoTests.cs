using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Persistence.Audit;
using LuxMap.Persistence.Versions;
using LuxMap.Shared.Storage;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

/// <summary>POLE-VIDEO PV-c1: the clip of a pole photo through the API with ranges (D-9), and the record that a Manager opened it (D-13).</summary>
public partial class WorkOrderTests
{
    private const int ClipLength = 1000;

    /// <summary>A pole photo whose row already carries a clip (PV-a's columns), with the clip's bytes in the in-memory store.</summary>
    private async Task<(string Order, string Photo, byte[] Clip, string Key)> NewPoleVideo()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole);
        var clip = RandomNumberGenerator.GetBytes(ClipLength);
        var sha = Convert.ToHexStringLower(SHA256.HashData(clip));
        var key = $"pole-videos/{id}/{Guid.NewGuid():N}/{sha}.mp4";
        await Db(async db =>
        {
            using var fixture = db.EnterUnscopedSystemWriteBackdoor();
            var extractor = new ArtifactVersion { Component = ArtifactComponents.FrameExtractor, Version = "pole-video-test-" + Guid.NewGuid().ToString("N"),
                ArtifactHash = new string('c', 64) };
            db.Add(extractor); await db.SaveChangesAsync();
            var row = await db.Set<WorkOrderPolePhoto>().IgnoreQueryFilters().SingleAsync(p => p.PhotoId == photo);
            (row.VideoObjectKey, row.VideoSha256, row.VideoBytes, row.VideoDurationMs) = (key, sha, ClipLength, 5000);
            (row.VideoSampledFrames, row.VideoOnFrames, row.VideoOffFrames, row.VideoDaylightFrames) = (5, 4, 0, 0);
            (row.VideoConfidenceMin, row.VideoConfidenceMedian, row.VideoConfidenceMax) = (.6, .8, .9);
            (row.VideoMedianLuma, row.VideoHdr, row.VideoExtractorVersionId) = (.3, false, extractor.VersionId);
            await db.SaveChangesAsync(); return 0;
        });
        poleStore.Objects[(StorageBucket.Video, key)] = clip;
        return (id, photo, clip, key);
    }

    private string VideoPath(string order, string photo) => PhotosPath(order) + $"/{photo}/video";

    private async Task<HttpResponseMessage> GetVideo(string order, string photo, string who = "manager", string? range = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, VideoPath(order, photo));
        if (range is not null) request.Headers.TryAddWithoutValidation("Range", range);
        // The body is read before the message is disposed; the headers stay on the returned response.
        return await (await PoleClient(who)).SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    private Task<AuditEvent[]> ViewedEvents(string order)
        => Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().Where(e => e.EntityId == order && e.Action == AuditAction.Viewed).ToArrayAsync());

    [Theory]
    [InlineData("manager", 200)] [InlineData("superior", 200)] [InlineData("admin", 200)] [InlineData("a", 200)] [InlineData("anonymous", 401)]
    public async Task The_pole_video_can_be_read_by_every_role_that_reads_work_orders(string who, int expected)
    {
        var (order, photo, clip, _) = await NewPoleVideo();
        using var response = await GetVideo(order, photo, who);
        Assert.Equal(expected, (int)response.StatusCode);
        if (expected == 200) Assert.Equal(clip, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task A_photo_without_a_clip_or_an_unknown_photo_is_a_404()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var plain = await NewPolePhoto(id, pole);
        using var noClip = await GetVideo(id, plain);
        Assert.Equal(HttpStatusCode.NotFound, noClip.StatusCode);
        Assert.Equal("POLE_VIDEO_NOT_FOUND", JsonDocument.Parse(await noClip.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetProperty("code").GetString());
        using var unknown = await GetVideo(id, "PPH-999999");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var (otherOrder, photo, _, _) = await NewPoleVideo();
        using var wrongOrder = await GetVideo(id, photo);     // a real clip, but not under this work order
        Assert.Equal(HttpStatusCode.NotFound, wrongOrder.StatusCode);
        Assert.NotEqual(id, otherOrder);
    }

    [Fact]
    public async Task Without_a_range_the_whole_clip_is_sent_as_a_200_that_says_ranges_are_accepted_and_is_never_cached()
    {
        var (order, photo, clip, _) = await NewPoleVideo();
        using var response = await GetVideo(order, photo);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ClipLength, response.Content.Headers.ContentLength);
        Assert.Contains("bytes", response.Headers.AcceptRanges);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Null(response.Content.Headers.ContentRange);
        Assert.Equal(clip, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(0, poleStore.RangeReads);
    }

    [Theory]
    [InlineData("bytes=0-99", 0, 99)]
    [InlineData("bytes=250-499", 250, 499)]
    [InlineData("bytes=900-", 900, 999)]
    [InlineData("bytes=-100", 900, 999)]
    [InlineData("bytes=-5000", 0, 999)]
    [InlineData("bytes=990-5000", 990, 999)]
    [InlineData("bytes=999-999", 999, 999)]
    public async Task A_satisfiable_range_is_a_206_with_exactly_those_bytes_and_asks_the_store_for_only_those(string range, int from, int to)
    {
        var (order, photo, clip, _) = await NewPoleVideo();
        using var response = await GetVideo(order, photo, range: range);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(from, response.Content.Headers.ContentRange?.From);
        Assert.Equal(to, response.Content.Headers.ContentRange?.To);
        Assert.Equal(ClipLength, response.Content.Headers.ContentRange?.Length);
        Assert.Equal(to - from + 1, response.Content.Headers.ContentLength);
        Assert.Equal(clip[from..(to + 1)], await response.Content.ReadAsByteArrayAsync());
        Assert.Equal((from, to), poleStore.LastRange);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("bytes=1000-")] [InlineData("bytes=5000-6000")] [InlineData("bytes=-0")]
    public async Task A_range_the_clip_cannot_satisfy_is_a_416_with_the_size_and_the_error_shape(string range)
    {
        var (order, photo, _, _) = await NewPoleVideo();
        using var response = await GetVideo(order, photo, range: range);
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, response.StatusCode);
        Assert.Equal("bytes */1000", response.Content.Headers.ContentRange?.ToString() ?? string.Join(",", response.Headers.GetValues("Content-Range")));
        var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error");
        Assert.Equal("RANGE_NOT_SATISFIABLE", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(error.GetProperty("details").GetProperty("correlation_id").GetString()));
        Assert.Equal(ClipLength, error.GetProperty("details").GetProperty("size").GetInt32());
    }

    [Theory]
    [InlineData("bytes=5-3")] [InlineData("bytes=0-1,5-6")] [InlineData("items=0-5")] [InlineData("garbage")] [InlineData("bytes=")]
    public async Task A_range_header_that_is_malformed_names_another_unit_or_holds_several_ranges_is_ignored_and_the_whole_clip_is_sent(string range)
    {
        var (order, photo, clip, _) = await NewPoleVideo();
        using var response = await GetVideo(order, photo, range: range);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(clip, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task A_Manager_opening_an_undecided_clip_is_recorded_once_with_one_audit_event_however_many_ranges_follow()
    {
        var (order, photo, _, _) = await NewPoleVideo();
        using (await GetVideo(order, photo, range: "bytes=0-1")) { }          // a player's first request is usually a range
        using (await GetVideo(order, photo, range: "bytes=500-599")) { }
        using (var whole = await GetVideo(order, photo)) await whole.Content.ReadAsByteArrayAsync();
        var row = await Db(db => db.Set<WorkOrderPolePhoto>().IgnoreQueryFilters().SingleAsync(p => p.PhotoId == photo));
        Assert.Equal(users["manager"].UserId, row.VideoViewedBy);
        Assert.NotNull(row.VideoViewedAt);
        var viewed = Assert.Single(await ViewedEvents(order));
        Assert.Equal(users["manager"].UserId, viewed.ActorUserId);
    }

    [Fact]
    public async Task Parallel_requests_of_one_player_record_the_view_once()
    {
        var (order, photo, _, _) = await NewPoleVideo();
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(n => GetVideo(order, photo, range: $"bytes={n * 100}-{n * 100 + 99}")));
        try { Assert.All(responses, r => Assert.Equal(HttpStatusCode.PartialContent, r.StatusCode)); }
        finally { foreach (var r in responses) r.Dispose(); }
        Assert.Single(await ViewedEvents(order));
    }

    [Theory]
    [InlineData("a")] [InlineData("superior")] [InlineData("admin")]
    public async Task Only_a_user_who_may_decide_is_recorded_as_having_viewed(string who)
    {
        var (order, photo, _, _) = await NewPoleVideo();
        using (await GetVideo(order, photo, who)) { }
        var row = await Db(db => db.Set<WorkOrderPolePhoto>().IgnoreQueryFilters().SingleAsync(p => p.PhotoId == photo));
        Assert.Null(row.VideoViewedBy); Assert.Null(row.VideoViewedAt);
        Assert.Empty(await ViewedEvents(order));
    }

    [Fact]
    public async Task A_clip_that_is_already_decided_is_not_recorded_again()
    {
        var (order, photo, _, _) = await NewPoleVideo();
        await PoleDecision(order, photo, "reject");
        using (await GetVideo(order, photo)) { }
        var row = await Db(db => db.Set<WorkOrderPolePhoto>().IgnoreQueryFilters().SingleAsync(p => p.PhotoId == photo));
        Assert.Null(row.VideoViewedBy);
        Assert.Empty(await ViewedEvents(order));
    }

    [Fact]
    public async Task A_missing_clip_object_is_not_recorded_as_viewed()
    {
        var (order, photo, _, key) = await NewPoleVideo();
        poleStore.Objects.TryRemove((StorageBucket.Video, key), out _);
        using var response = await GetVideo(order, photo);
        Assert.False(response.IsSuccessStatusCode);
        var row = await Db(db => db.Set<WorkOrderPolePhoto>().IgnoreQueryFilters().SingleAsync(p => p.PhotoId == photo));
        Assert.Null(row.VideoViewedBy);
    }
}

/// <summary>The range rules on their own (RFC 9110 §14), without a host.</summary>
public sealed class PoleVideoRangeTests
{
    private static LuxMap.Modules.WorkOrders.PoleVideoRange Resolve(string? header, long total)
        => LuxMap.Modules.WorkOrders.PoleVideoRange.Resolve(
            header is null ? null : Microsoft.Net.Http.Headers.RangeHeaderValue.TryParse(header, out var parsed) ? parsed : null, total);

    [Theory]
    [InlineData(null, false, false, 0, 999)]
    [InlineData("bytes=0-0", true, false, 0, 0)]
    [InlineData("bytes=0-", true, false, 0, 999)]
    [InlineData("bytes=-1", true, false, 999, 999)]
    [InlineData("bytes=-1000", true, false, 0, 999)]
    [InlineData("bytes=-2000", true, false, 0, 999)]
    [InlineData("bytes=999-2000", true, false, 999, 999)]
    [InlineData("BYTES=10-20", true, false, 10, 20)]
    [InlineData("bytes=1000-", true, true, 0, 0)]
    [InlineData("bytes=-0", true, true, 0, 0)]
    [InlineData("bytes=0-1,4-5", false, false, 0, 999)]
    [InlineData("bytes=9-3", false, false, 0, 999)]
    [InlineData("lines=0-5", false, false, 0, 999)]
    public void Resolve_follows_the_standard_and_falls_back_to_the_whole_clip(string? header, bool partial, bool unsatisfiable, long from, long to)
        => Assert.Equal((partial, unsatisfiable, from, to), Resolve(header, 1000) is var r ? (r.Partial, r.Unsatisfiable, r.From, r.To) : default);

    [Fact]
    public void A_one_byte_clip_has_one_satisfiable_range()
    {
        Assert.Equal((true, false, 0L, 0L), Resolve("bytes=0-", 1) is var r ? (r.Partial, r.Unsatisfiable, r.From, r.To) : default);
        Assert.True(Resolve("bytes=1-", 1).Unsatisfiable);
    }
}
