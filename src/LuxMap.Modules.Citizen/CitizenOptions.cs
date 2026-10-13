namespace LuxMap.Modules.Citizen;

/// <summary>CITIZEN-QR P2b limits (D-8), section <c>Citizen</c> in appsettings; a restart applies them.</summary>
public sealed record CitizenOptions
{
    public const string SectionName = "Citizen";

    public int MaxPhotos { get; init; } = 3;

    public long PhotoMaxBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>Checked from the JPEG header before any decode (review P1 #3).</summary>
    public long PhotoMaxPixels { get; init; } = 40_000_000;

    /// <summary>Photos kept per code per night; beyond it the text is still taken and the photos dropped (review P1 #5).</summary>
    public int PhotosPerCodePerNight { get; init; } = 20;

    /// <summary>Bytes of citizen photos (original + thumbnail) kept per night across the whole system; beyond it, text only.</summary>
    public long PhotoBudgetBytesPerNight { get; init; } = 512L * 1024 * 1024;

    /// <summary>A shared position further than this from the pole is flagged (D-8) — never refused.</summary>
    public double FarFromPoleMeters { get; init; } = 200;

    /// <summary>The whole multipart body the report endpoint will read: every photo at its cap, plus room for the text fields.</summary>
    public long MaxRequestBytes => MaxPhotos * PhotoMaxBytes + 1024 * 1024;

    public void Validate()
    {
        if (MaxPhotos is < 0 or > 10 || PhotoMaxBytes <= 0 || PhotoMaxPixels <= 0 || PhotosPerCodePerNight < 0
            || PhotoBudgetBytesPerNight < 0 || !double.IsFinite(FarFromPoleMeters) || FarFromPoleMeters <= 0)
        {
            throw new InvalidOperationException($"{SectionName} has an out-of-range limit.");
        }
    }
}
