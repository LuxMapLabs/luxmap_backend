using LuxMap.Modules.Assets;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Serialization;

namespace LuxMap.Modules.Survey.Review;

/// <summary>Survey's side of <see cref="IInitialPoleStatus"/>: the asset import seeds a demo status through the one writer.</summary>
public sealed class InitialPoleStatusPublisher : IInitialPoleStatus
{
    public void StageForNewPole(Pole pole, FixtureStatus status)
        => PoleStatusPublication.StageForNewPole(pole, status, UtcMicrosecondClock.UtcNow());
}
