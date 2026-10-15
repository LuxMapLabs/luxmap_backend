using System.Net;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.WorkOrders;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LuxMap.Modules.Survey.Review;

/// <summary>Manager-confirmed ON/OFF photos publish through the same writer as accepted sweeps (PP-9).</summary>
public sealed class ReviewedPolePhotoPublisher(LuxMapDbContext db, IAuditTrail audit, ICurrentActorAccessor actor,
    IOptions<SurveyReviewOptions> options) : IPolePhotoPublication
{
    public async Task<PolePhotoPublicationResult> PublishAsync(PolePhotoPublication input, CancellationToken ct)
    {
        var pole = await db.Set<Pole>().AsNoTracking().SingleOrDefaultAsync(p => p.PoleId == input.PoleId, ct);
        if (pole is null || pole.CommuneId != input.CommuneId || pole.DataSource != input.DataSource)
            throw new LuxMapException("POLE_PHOTO_TARGET_CHANGED", HttpStatusCode.Conflict, "Photo target changed.");
        var current = await db.Set<PoleCurrentStatus>().SingleOrDefaultAsync(p => p.PoleId == input.PoleId, ct);
        if (!SurveyPublicationRules.IsNewer(input.CapturedAt, current?.LastEvaluatedAt ?? current?.LastSeenAt))
            return new(false, "not_newer");
        // An ON reading cannot rebut dim. Preserve the classification and its confidence, but record
        // the newer observation clock so an older OFF photo cannot subsequently overwrite it.
        var preserve = input.CvState == "on" && current?.FixtureStatus is FixtureStatus.Normal or FixtureStatus.Dim;
        var status = preserve ? current!.FixtureStatus : input.CvState == "off" ? FixtureStatus.Out : FixtureStatus.Normal;
        var confidence = preserve ? current!.StatusConfidence : input.Confidence;
        await PoleStatusPublication.StageAsync(db, pole.PoleId, pole.CommuneId, status, confidence,
            input.CapturedAt, input.Now, null, null, ct);
        audit.Record(new(input.Now, AuditActorKind.User, actor.UserId, actor.Role, pole.CommuneId,
            AuditEntityType.WorkOrder, input.WorkOrderId, AuditAction.Applied, null,
            new { input.PhotoId, input.PoleId, fixture_status = status, input.CapturedAt, input.DataSource, input.ArtifactVersion }));
        await db.SaveChangesAsync(ct);

        var open = FaultStatusSets.Open.ToArray();
        if (input.CvState == "off" && !await db.Set<Fault>().AnyAsync(f => f.PoleId == pole.PoleId
            && open.Contains(f.FaultStatus) && (f.OverrideFaultType == FaultType.LampOut
                || f.OverrideFaultType == null && f.FaultType == FaultType.LampOut), ct))
        {
            // Trusted registry SQL only; never constructed from request input.
            var nextIdSql = $"SELECT {PrefixedIds.Fault.DefaultValueSql} AS \"Value\"";
            var id = await db.Database.SqlQueryRaw<string>(nextIdSql).SingleAsync(ct);
            var fault = new Fault
            {
                FaultId = id, PoleId = pole.PoleId, CommuneId = pole.CommuneId, SegmentId = pole.SegmentId,
                FaultType = FaultType.LampOut, FaultStatus = FaultStatus.Confirmed,
                SourceChannel = SourceChannel.Cv, DataSource = pole.DataSource, OriginPolePhotoId = input.PhotoId,
                DetectionModelVersion = input.ArtifactVersion,
                Severity = SurveyPublicationRules.SeverityFor(FaultType.LampOut, pole.NearSensitivePoi, options.Value),
                StatusConfidence = input.Confidence, DetectedAt = input.CapturedAt, CreatedAt = input.Now, UpdatedAt = input.Now,
                ConfirmedBy = actor.UserId, ConfirmedAt = input.Now,
            };
            db.Add(fault);
            audit.Record(new(input.Now, AuditActorKind.User, actor.UserId, actor.Role, pole.CommuneId,
                AuditEntityType.Fault, id, AuditAction.Created, null,
                new { fault.FaultId, fault.PoleId, fault.FaultType, fault.FaultStatus, fault.DataSource,
                    fault.OriginPolePhotoId, fault.DetectionModelVersion, input.ModelVersion }));
            await db.SaveChangesAsync(ct);
        }
        return new(true, preserve ? "classification_preserved" : "applied");
    }
}
