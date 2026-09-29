namespace LuxMap.Modules.Faults;

/// <summary>
/// Which work order currently holds a fault. Declared here, implemented by the WorkOrders module.
/// </summary>
/// <remarks>
/// <para>
/// A port rather than a project reference because the dependency already runs the other way:
/// WorkOrders references Faults to drive fault transitions, so Faults referencing WorkOrders would
/// be a cycle (BE-40 D-9).
/// </para>
/// <para>
/// "Currently holds" = the link row with <c>released_at IS NULL</c> — at most one per fault,
/// enforced by <c>ux_work_order_fault_fault_id_active</c> (drift WO-7). BE-19 asks the same
/// question to refuse a decision on a fault under an active repair (drift WO-10).
/// </para>
/// <para>
/// ⚠️ The answer is NOT narrowed to work orders the caller may open. A field engineer sees the ID
/// of a work order assigned to someone else, although opening it answers 404 (drift WO-1): the ID
/// is a fact about the fault, and <c>null</c> would claim no work order exists (BE-40 D-3).
/// </para>
/// </remarks>
public interface IActiveWorkOrderLookup
{
    /// <summary>fault_id → work_order_id for those of <paramref name="faultIds"/> held by a work order.</summary>
    Task<IReadOnlyDictionary<string, string>> ActiveWorkOrdersAsync(
        IReadOnlyCollection<string> faultIds, CancellationToken ct);

    /// <summary>
    /// fault_id → work_order_id for those of <paramref name="faultIds"/> held by a REPAIR work order —
    /// any unreleased link, whatever the work order's status (open, assigned, in progress, done).
    /// </summary>
    /// <remarks>
    /// Drift WO-10 / BE-19 D-3: while a repair holds a fault, the repair drives its status and a
    /// Manager's review of it answers 409. An inspection does not block review.
    /// </remarks>
    Task<IReadOnlyDictionary<string, string>> ActiveRepairsAsync(
        IReadOnlyCollection<string> faultIds, CancellationToken ct);
}
