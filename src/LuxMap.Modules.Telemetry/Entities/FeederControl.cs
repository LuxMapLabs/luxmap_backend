using LuxMap.Shared.Authorization;

namespace LuxMap.Modules.Telemetry.Entities;

/// <summary>
/// Which relay of which device switches a feeder (I-12). A feeder has zero or one of these.
/// </summary>
/// <remarks>
/// <para>
/// A "channel" is one RELAY, and a relay feeds exactly one feeder, so the relay is folded into the
/// feeder rather than modelled on its own: the key is <see cref="FeederId"/>, and
/// <c>(node_id, relay_no)</c> is unique so two feeders cannot claim the same relay.
/// </para>
/// <para>
/// ⚠️ <b>A separate table, NOT columns on <c>feeder</c>.</b> <see cref="ControlMode"/> is operational
/// state the device reports continuously, while <c>feeder</c> is inventory the Manager edits through
/// <c>/assets</c> — and <c>PUT /assets/feeders/{id}</c> is a FULL replacement that would wipe a mode
/// it knows nothing about. Same reason <c>pole_current_status</c> is not four columns on
/// <c>pole</c> (BE-09 rule 2).
/// </para>
/// </remarks>
public class FeederControl : ICommuneScoped
{
    public required string FeederId { get; set; }

    public required string NodeId { get; set; }

    /// <summary>
    /// Carried so both foreign keys can include it: the database refuses a device and a feeder in
    /// different communes (the O-7 composite-key pattern).
    /// </summary>
    public required string CommuneId { get; set; }

    /// <summary>
    /// The cabinet of BOTH the device and the feeder (CAB-4). Carried so two database-only foreign keys can
    /// include it — <c>(feeder_id, cabinet_id)</c> and <c>(node_id, cabinet_id)</c> — and refuse a relay that
    /// crosses cabinets, a feeder never recorded in a cabinet, and moving a switched feeder or device.
    /// </summary>
    public required string CabinetId { get; set; }

    /// <summary>1-based relay number on the device — what the device itself knows (I-17).</summary>
    public short RelayNo { get; set; }

    /// <summary>Mode the device last reported for this relay. NULL until it reports.</summary>
    public FeederControlMode? ControlMode { get; set; }

    public DateTime? ModeReportedAt { get; set; }

    /// <summary>
    /// The <c>seq</c> of the lighting command whose acknowledgement last set <see cref="ControlMode"/> (LIGHT-CTRL D-10). A
    /// report is written only when its <c>seq</c> is above this, so a late acknowledgement of an old command never overwrites
    /// a newer mode. NULL until the first acknowledged command.
    /// </summary>
    public long? ModeSeq { get; set; }
}

/// <summary>
/// on | off | auto — the relay state a device reports (I-6, I-12). Storage-only for now: no
/// endpoint emits it until the lighting-control ticket (D-R7).
/// </summary>
public enum FeederControlMode
{
    On,
    Off,
    Auto,
}
