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
    IOptions<SurveyReviewOptions> options, StationaryLuxEvaluator stationary) : IPolePhotoPublication
{
    public async Task<PolePhotoPublicationResult> PublishAsync(PolePhotoPublication input, CancellationToken ct)
    {
        var human = input.LabelSource == PolePhotoLabelSource.Engineer;
        if (input.LabelSource is not (PolePhotoLabelSource.Ai or PolePhotoLabelSource.Engineer)
            || input.EffectiveCvState is not ("on" or "off")
            || human && (input.EffectiveCvState == input.AiCvState || string.IsNullOrWhiteSpace(input.ReviewedBy)
                || input.ReviewedAt is null || string.IsNullOrWhiteSpace(input.ReviewNote) || input.ReviewNote.Length > 2000)
            || !human && (input.EffectiveCvState != input.AiCvState || input.AiConfidence is null
                || !double.IsFinite(input.AiConfidence.Value) || input.AiConfidence is < 0 or > 1
                || string.IsNullOrWhiteSpace(input.AiModelVersion) || string.IsNullOrWhiteSpace(input.AiArtifactVersion)))
            throw new InvalidOperationException("Invalid pole photo publication provenance.");
        var labelConfidence = human ? null : input.AiConfidence;
        var pole = await db.Set<Pole>().AsNoTracking().SingleOrDefaultAsync(p => p.PoleId == input.PoleId, ct);
        if (pole is null || pole.CommuneId != input.CommuneId || pole.DataSource != input.DataSource)
            throw new LuxMapException("POLE_PHOTO_TARGET_CHANGED", HttpStatusCode.Conflict, "Photo target changed.");
        var current = await db.Set<PoleCurrentStatus>().SingleOrDefaultAsync(p => p.PoleId == input.PoleId, ct);

        // LUX-BASELINE D-8: the protocol reading taken with this photo is judged first, and saved with its own audit, whether or not
        // the photo turns out newer than the current status — a late decision is still a valid reading of its night (D-9).
        var lux = await stationary.EvaluateAsync(input, pole, actor.UserId!, ct);
        if (lux is not null)
        {
            audit.Record(new(input.Now, AuditActorKind.User, actor.UserId, actor.Role, pole.CommuneId,
                AuditEntityType.WorkOrder, input.WorkOrderId, AuditAction.Analyzed, null,
                new { input.PhotoId, lux.LuxId, lux.PoleId, lux.CvState, classified_as = lux.ClassifiedAs, lux.LuxValue, lux.BaselineValue,
                    lux.BaselineRatio, lux.DimThresholdRatio, lux.DimEvaluationEligible, lux.BaselineEligible, lux.ExpectedRatio,
                    reason_codes = lux.ReasonCodes, lux.AlgorithmVersionId, input.DataSource }));
            await db.SaveChangesAsync(ct);
        }

        if (!SurveyPublicationRules.IsNewer(input.CapturedAt, current?.LastEvaluatedAt ?? current?.LastSeenAt))
            return new(false, "not_newer");
        // A bare ON reading cannot rebut dim. A protocol reading judged against a stable baseline can (normal / dim); a filter test
        // never moves the status (D-12). Otherwise preserve the classification and its confidence, but record the newer
        // observation clock so an older OFF photo cannot subsequently overwrite it.
        var luxDecides = lux is { CvState: "on", DimEvaluationEligible: true, ExpectedRatio: null };
        var before = current is null ? null : new { current.FixtureStatus, current.StatusConfidence, current.LastEvaluatedAt, current.LastSeenAt, current.LastSweepId, current.LastRunId };
        var status = luxDecides ? lux!.ClassifiedAs : SurveyPublicationRules.PhotoStatus(input.EffectiveCvState, current?.FixtureStatus);
        var preserve = !luxDecides && input.EffectiveCvState == "on" && current?.FixtureStatus == status;
        var confidence = preserve ? current!.StatusConfidence : labelConfidence;
        await PoleStatusPublication.StageAsync(db, pole.PoleId, pole.CommuneId, status, confidence,
            input.CapturedAt, input.Now, null, null, ct);
        audit.Record(new(input.Now, AuditActorKind.User, actor.UserId, actor.Role, pole.CommuneId,
            AuditEntityType.WorkOrder, input.WorkOrderId, AuditAction.Applied, before,
            new { input.PhotoId, input.PoleId, fixture_status = status, input.CapturedAt, input.DataSource, provenance = input.LabelSnapshot,
                lux_id = lux?.LuxId }));
        await db.SaveChangesAsync(ct);

        var raised = input.EffectiveCvState == "off" ? FaultType.LampOut
            : luxDecides && status == FixtureStatus.Dim ? FaultType.LampDim : (FaultType?)null;
        var open = FaultStatusSets.Open.ToArray();
        if (raised is { } type && !await db.Set<Fault>().AnyAsync(f => f.PoleId == pole.PoleId
            && open.Contains(f.FaultStatus) && (f.OverrideFaultType == type
                || f.OverrideFaultType == null && f.FaultType == type), ct))
        {
            // Trusted registry SQL only; never constructed from request input.
            var nextIdSql = $"SELECT {PrefixedIds.Fault.DefaultValueSql} AS \"Value\"";
            var id = await db.Database.SqlQueryRaw<string>(nextIdSql).SingleAsync(ct);
            var fault = new Fault
            {
                FaultId = id, PoleId = pole.PoleId, CommuneId = pole.CommuneId, SegmentId = pole.SegmentId,
                FaultType = type, FaultStatus = FaultStatus.Confirmed,
                SourceChannel = human ? SourceChannel.FieldReport : SourceChannel.Cv, DataSource = pole.DataSource, OriginPolePhotoId = input.PhotoId,
                DetectionModelVersion = human ? null : input.AiArtifactVersion, ReportedBy = human ? input.ReviewedBy : null,
                Severity = SurveyPublicationRules.SeverityFor(type, pole.NearSensitivePoi, options.Value),
                StatusConfidence = labelConfidence, DetectedAt = input.CapturedAt, CreatedAt = input.Now, UpdatedAt = input.Now,
                ConfirmedBy = actor.UserId, ConfirmedAt = input.Now,
            };
            db.Add(fault);
            audit.Record(new(input.Now, AuditActorKind.User, actor.UserId, actor.Role, pole.CommuneId,
                AuditEntityType.Fault, id, AuditAction.Created, null,
                new { fault.FaultId, fault.PoleId, fault.FaultType, fault.FaultStatus, fault.DataSource,
                    fault.OriginPolePhotoId, fault.DetectionModelVersion, fault.SourceChannel, fault.StatusConfidence, fault.ReportedBy, fault.ConfirmedBy,
                    provenance = input.LabelSnapshot, lux_id = lux?.LuxId }));
            await db.SaveChangesAsync(ct);
        }
        return new(true, preserve ? "classification_preserved" : "applied");
    }
}
