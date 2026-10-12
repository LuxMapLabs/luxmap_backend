using System.Net;
using System.Security.Cryptography;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Citizen.Entities;
using LuxMap.Modules.Identity.Accounts;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Citizen;

/// <summary>Issue, revoke and list the QR codes on poles (CITIZEN-QR P2a, CQ-1 / CQ-10, D-3).</summary>
public sealed class QrCodeService(
    LuxMapDbContext db, ICurrentActorAccessor actor, IAuditTrail audit, EmailOptions web, TimeProvider clock)
{
    /// <summary>The public report page of the web app (D-12); the code follows it.</summary>
    public const string PublicPath = "/r/";

    public const int CodeLength = 16;

    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    private string ActorId => actor.UserId ?? throw new InvalidOperationException("QR codes are managed by a signed-in Manager.");

    /// <summary>A new code for the pole; the code in use, if any, is revoked in the same transaction.</summary>
    /// <remarks>
    /// Issuing is serialised on the pole row: read the pole through the commune filter FIRST (out of scope is a 404, and a
    /// Manager of another commune never waits on this lock), then <c>FOR UPDATE</c> it, then look for the code in use. Two
    /// presses at once therefore end with one code in use, never two — <c>ux_qr_code_pole_id_active</c> is the last line.
    /// A collision of two random 16-character codes (~95 bits) is not handled: at a million codes its odds are ~10⁻¹⁷.
    /// </remarks>
    public async Task<QrCodeItem> IssueAsync(IssueQrCodeRequest request, CancellationToken ct)
    {
        var poleId = request.PoleId?.Trim();
        if (string.IsNullOrEmpty(poleId)) throw Invalid("pole_id", "pole_id is required.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var pole = await db.Set<Pole>().AsNoTracking()
            .Where(p => p.PoleId == poleId).Select(p => new { p.PoleId, p.CommuneId }).SingleOrDefaultAsync(ct)
            ?? throw new LuxMapException(ErrorCodes.PoleNotFound, HttpStatusCode.NotFound,
                "That pole does not exist, or it is outside your permitted commune scope.");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM pole WHERE pole_id = {pole.PoleId} AND commune_id = {pole.CommuneId} FOR UPDATE", ct);

        var now = UtcMicrosecondClock.UtcNow(clock);
        var current = await db.Set<QrCode>().SingleOrDefaultAsync(qr => qr.PoleId == pole.PoleId && qr.RevokedAt == null, ct);
        var qrId = await NextQrId(ct);

        // One audit event per SaveChanges (BE-23a): revoking the old code and creating the new one are two saves.
        if (current is not null)
        {
            Revoke(current, now, replacedBy: qrId);
            await db.SaveChangesAsync(ct);
        }

        var issued = new QrCode
        {
            QrId = qrId, Code = RandomNumberGenerator.GetString(Alphabet, CodeLength), PoleId = pole.PoleId,
            CommuneId = pole.CommuneId, CreatedBy = ActorId, CreatedAt = now,
        };
        db.Add(issued);
        // Never the code itself (D-16): the audit row says which pole got a new code, not what it is.
        Record(issued, AuditAction.Created, null, new { issued.QrId, issued.PoleId, replaces = current?.QrId });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Item(issued);
    }

    /// <summary>Takes a code out of use. A revoked code is history and never comes back (409 on a second revoke).</summary>
    /// <remarks>
    /// Takes the SAME pole lock as <see cref="IssueAsync"/>, in the same order (pole, then code). Locking only the code row is
    /// not enough: an issue that read the code as in use before this revoke committed would then overwrite who revoked it and
    /// when, with a second <c>revoked</c> audit event (Codex review P2a).
    /// </remarks>
    public async Task<QrCodeItem> RevokeAsync(string qrId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var scoped = await db.Set<QrCode>().AsNoTracking()
            .Where(qr => qr.QrId == qrId).Select(qr => new { qr.QrId, qr.PoleId, qr.CommuneId }).SingleOrDefaultAsync(ct)
            ?? throw NotFound();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM pole WHERE pole_id = {scoped.PoleId} AND commune_id = {scoped.CommuneId} FOR UPDATE", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM qr_code WHERE qr_id = {scoped.QrId} AND commune_id = {scoped.CommuneId} FOR UPDATE", ct);

        var qr = await db.Set<QrCode>().SingleAsync(q => q.QrId == scoped.QrId, ct);
        if (qr.RevokedAt is not null)
        {
            throw new LuxMapException(ErrorCodes.QrCodeAlreadyRevoked, HttpStatusCode.Conflict,
                "This QR code was already revoked; issue a new one for the pole instead.",
                new Dictionary<string, object?> { ["qr_id"] = qr.QrId });
        }

        Revoke(qr, UtcMicrosecondClock.UtcNow(clock), replacedBy: null);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Item(qr);
    }

    /// <summary>Codes in the caller's communes, oldest first by the section 0 rule (<c>created_at, length(id), id</c>).</summary>
    public async Task<PagedResult<QrCodeItem>> ListAsync(
        IReadOnlyList<string>? communes, string? poleId, bool? active, PageRequest page, CancellationToken ct)
    {
        var codes = db.Set<QrCode>().AsNoTracking();
        if (communes is not null) codes = codes.Where(qr => communes.Contains(qr.CommuneId));
        if (!string.IsNullOrWhiteSpace(poleId)) codes = codes.Where(qr => qr.PoleId == poleId.Trim());
        if (active is { } inUse) codes = inUse ? codes.Where(qr => qr.RevokedAt == null) : codes.Where(qr => qr.RevokedAt != null);

        var total = await codes.CountAsync(ct);
        var found = await codes.OrderBy(qr => qr.CreatedAt).ThenBy(qr => qr.QrId.Length).ThenBy(qr => qr.QrId)
            .Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return PagedResult<QrCodeItem>.From(page, total, [.. found.Select(Item)]);
    }

    private void Revoke(QrCode qr, DateTime now, string? replacedBy)
    {
        qr.RevokedAt = now;
        qr.RevokedBy = ActorId;
        Record(qr, AuditAction.Revoked, new { revoked_at = (DateTime?)null }, new { qr.QrId, qr.PoleId, qr.RevokedAt, replaced_by = replacedBy });
    }

    private void Record(QrCode qr, AuditAction action, object? before, object after)
        => audit.Record(new(UtcMicrosecondClock.UtcNow(clock), AuditActorKind.User, ActorId, actor.Role, qr.CommuneId,
            AuditEntityType.QrCode, qr.QrId, action, before, after));

    private QrCodeItem Item(QrCode qr) => new()
    {
        QrId = qr.QrId, Code = qr.Code, Url = new Uri(web.WebAppBaseUrl, PublicPath + qr.Code).ToString(),
        PoleId = qr.PoleId, CommuneId = qr.CommuneId, Active = qr.RevokedAt is null,
        CreatedAt = qr.CreatedAt, CreatedBy = qr.CreatedBy, RevokedAt = qr.RevokedAt, RevokedBy = qr.RevokedBy,
    };

    private async Task<string> NextQrId(CancellationToken ct)
    {
        // The same draw the column DEFAULT makes; the id is needed before saving, for the audit event.
        var sql = $"SELECT {PrefixedIds.QrCode.DefaultValueSql} AS \"Value\"";
        return await db.Database.SqlQueryRaw<string>(sql).SingleAsync(ct);
    }

    private static LuxMapException NotFound()
        => new(ErrorCodes.QrCodeNotFound, HttpStatusCode.NotFound,
            "That QR code does not exist, or it is outside your permitted commune scope.");

    private static LuxMapException Invalid(string field, string message)
        => new(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, message,
            new Dictionary<string, object?> { ["field"] = field });
}
