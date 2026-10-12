using System.ComponentModel.DataAnnotations;

namespace LuxMap.Modules.Citizen;

/// <summary><c>POST /qr-codes</c>: issue a code for a pole, revoking the one in use if there is one.</summary>
public sealed record IssueQrCodeRequest
{
    [Required]
    public string? PoleId { get; init; }
}

/// <summary>A QR code as a Manager sees it. <see cref="Url"/> is what the web app draws as the QR image (CQ-10).</summary>
public sealed record QrCodeItem
{
    public required string QrId { get; init; }

    /// <summary>The value printed on the pole. Whoever holds it can file a citizen report — handle like a door key, not a secret.</summary>
    public required string Code { get; init; }

    /// <summary><c>{WEB_APP_BASE_URL}/r/{code}</c> — the public report page (D-12).</summary>
    public required string Url { get; init; }

    public required string PoleId { get; init; }
    public required string CommuneId { get; init; }

    /// <summary>In use: not revoked. At most one per pole.</summary>
    public required bool Active { get; init; }

    public required DateTime CreatedAt { get; init; }
    public required string CreatedBy { get; init; }
    public required DateTime? RevokedAt { get; init; }
    public required string? RevokedBy { get; init; }
}
