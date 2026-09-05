namespace LuxMap.Shared.Contracts.Enums;

/// <summary>
/// What "an open fault" means — defined ONCE, because several tickets need the same answer.
/// </summary>
/// <remarks>
/// Contract section 1 lists six <see cref="FaultStatus"/> values and never says which of them count
/// as open. Yet <c>open_fault_count</c> appears in <c>GET /poles</c> (section 2.1) and the same idea
/// drives BE-28's statistics and BE-40's default listing. Left to each ticket, three tickets would
/// each pick a set, all three would look reasonable, and the numbers would disagree with nothing to
/// explain why.
/// <para>
/// <b>Open</b> = <see cref="FaultStatus.Detected"/>, <see cref="FaultStatus.Confirmed"/>,
/// <see cref="FaultStatus.InProgress"/> — everything still awaiting work.
/// </para>
/// <para>
/// The three excluded values are excluded for different reasons, which is why the set is written out
/// rather than expressed as "not finished": <c>rejected</c> means an engineer decided it was never a
/// fault, <c>resolved</c> means the work is done, and <c>verified</c> means the fix was checked.
/// Only the first of those is a judgement about the fault itself.
/// </para>
/// <para>
/// ⚠️ Not in the Contract. Registered as drift so it is agreed rather than assumed.
/// </para>
/// </remarks>
public static class FaultStatusSets
{
    public static readonly IReadOnlySet<FaultStatus> Open =
        new HashSet<FaultStatus> { FaultStatus.Detected, FaultStatus.Confirmed, FaultStatus.InProgress };

    public static bool IsOpen(FaultStatus status) => Open.Contains(status);
}
