using LuxMap.Modules.Assets.Entities;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Assets;

/// <summary>
/// Gives a pole that the import is creating its first published status (IMPORT-INITIAL-STATUS), so a demo lamp shows green
/// without a survey.
/// </summary>
/// <remarks>
/// <para>
/// A port because <c>pole_current_status</c> has ONE writer, <c>PoleStatusPublication</c> in Survey, and Assets cannot
/// reference Survey. Survey implements it with that same writer.
/// </para>
/// <para>
/// The pole is not saved yet, so it has no <c>pole_id</c> (the database generates it). The implementation therefore hangs
/// the status on <see cref="Pole.CurrentStatus"/> and lets the one <c>SaveChanges</c> of the import insert both.
/// </para>
/// </remarks>
public interface IInitialPoleStatus
{
    /// <summary>
    /// Stages a status for a NEW pole. <paramref name="status"/> is <c>normal</c>, <c>dim</c> or <c>out</c>:
    /// <c>unknown</c> is "no row", so it is never staged.
    /// </summary>
    void StageForNewPole(Pole pole, FixtureStatus status);
}
