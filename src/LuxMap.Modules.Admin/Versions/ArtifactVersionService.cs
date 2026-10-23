using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using LuxMap.Persistence;
using LuxMap.Persistence.Versions;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LuxMap.Modules.Admin.Versions;

/// <summary>
/// The version registry for the system admin (BE-34): list it, register a firmware ahead, and answer "which results did this version
/// produce". Rows are immutable — a result cites them — so there is no update and no delete.
/// </summary>
public sealed partial class ArtifactVersionService(
    LuxMapDbContext db, VersionUsage usage, ICurrentActorAccessor actor, ILogger<ArtifactVersionService> logger)
{
    public const int MetadataMaxBytes = 4096;

    public async Task<PagedResult<ArtifactVersionItem>> ListAsync(string? component, PageRequest page, CancellationToken ct)
    {
        if (component is not null && !ArtifactComponents.All.Contains(component))
            throw Invalid("component", $"component is one of {string.Join(", ", ArtifactComponents.All)}.");
        var query = db.Set<ArtifactVersion>().AsNoTracking().Where(v => component == null || v.Component == component);
        var total = await query.CountAsync(ct);
        // Identity order is insertion order: newest first.
        var rows = await query.OrderByDescending(v => v.VersionId).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return PagedResult<ArtifactVersionItem>.From(page, total, rows.Select(Item).ToList());
    }

    public async Task<ArtifactVersionItem> RegisterAsync(RegisterArtifactVersionRequest request, CancellationToken ct)
    {
        var component = request.Component is { } c && ArtifactComponents.Firmware.Contains(c) ? c
            : throw Invalid("component", $"Only a firmware can be registered: {string.Join(", ", ArtifactComponents.Firmware)}.");
        var version = ArtifactRegistry.CleanVersion(request.Version)
            ?? throw Invalid("version", $"version is required: 1–{ArtifactRegistry.VersionMaxLength} characters, no control characters.");
        var hash = request.ArtifactHash is null ? null
            : Sha256().IsMatch(request.ArtifactHash) ? request.ArtifactHash
            : throw Invalid("artifact_hash", "artifact_hash is a SHA-256 in lowercase hex (64 characters).");
        var metadata = request.Metadata is not { } m ? "{}"
            : JsonSerializer.Serialize(m) is { Length: <= MetadataMaxBytes } raw ? raw
            : throw Invalid("metadata", $"metadata is a JSON object of at most {MetadataMaxBytes} bytes.");
        var userId = actor.UserId ?? throw new InvalidOperationException("An authorized request carries a user id.");

        var row = new ArtifactVersion
        {
            Component = component, Version = version, ArtifactHash = hash, Metadata = metadata, CreatedBy = userId,
            CreatedAt = UtcMicrosecondClock.UtcNow(),
        };
        db.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A device may have reported it first: that row has no hash and cannot gain one (rows are immutable).
            throw new LuxMapException(ErrorCodes.ArtifactVersionExists, HttpStatusCode.Conflict,
                "That component already has this version — registered, or reported by a device first.");
        }

        logger.LogInformation("Artifact version {VersionId} ({Component} {Version}) registered by {AdminId}.", row.VersionId, component, version, userId);
        return Item(row);
    }

    public async Task<ArtifactVersionUsage> UsageAsync(long versionId, CancellationToken ct)
    {
        var row = await db.Set<ArtifactVersion>().AsNoTracking().SingleOrDefaultAsync(v => v.VersionId == versionId, ct)
            ?? throw new LuxMapException(ErrorCodes.ArtifactVersionNotFound, HttpStatusCode.NotFound, "No artifact version has that id.");
        return new(Item(row), await usage.CountAsync(row.VersionId, row.Version, ct));
    }

    private static ArtifactVersionItem Item(ArtifactVersion v)
    {
        using var metadata = JsonDocument.Parse(v.Metadata);
        return new(v.VersionId, v.Component, v.Version, v.ArtifactHash, metadata.RootElement.Clone(), v.CreatedAt, v.CreatedBy);
    }

    private static LuxMapException Invalid(string field, string message)
        => new(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, message, new Dictionary<string, object?> { ["field"] = field });

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256();
}
