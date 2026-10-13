using System.Collections.Concurrent;

namespace LuxMap.Modules.Citizen;

/// <summary>
/// Photos whose hint could not be computed, kept out of the next openings for a while (Codex review P2c #3). Without it a handful of
/// unreadable photos would take every opening's <c>HintsPerRequest</c> slots and the photos after them would never be looked at.
/// </summary>
/// <remarks>
/// Per instance, in memory: a restart or a second instance simply tries again, which costs one model run. A failure is never stored as
/// a hint — "the model could not look" is not "the model saw nothing".
/// </remarks>
public sealed class CitizenHintBackoff(TimeProvider clock)
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, DateTimeOffset> failedUntil = new(StringComparer.Ordinal);

    public bool IsCooling(string photoId)
    {
        if (!failedUntil.TryGetValue(photoId, out var until)) return false;
        if (until > clock.GetUtcNow()) return true;
        failedUntil.TryRemove(photoId, out _);
        return false;
    }

    public void Failed(string photoId) => failedUntil[photoId] = clock.GetUtcNow() + Cooldown;
}
