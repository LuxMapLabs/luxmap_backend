namespace LuxMap.Modules.Assets.Entities;

/// <summary>
/// The cabinet rules that live in the DATABASE ONLY (CAB-4, CAB-5), created by raw SQL in migration
/// <c>AddElectricalCabinet</c>. The EF model does not know them.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Why not EF keys.</b> Each foreign key below needs a unique target that includes a column people
/// legitimately change — <c>feeder.cabinet_id</c>, <c>iot_node.cabinet_id</c>,
/// <c>electrical_cabinet.data_source</c>. Declared through <c>HasAlternateKey</c> that column becomes a key
/// property, and EF throws from <c>DetectChanges</c> on any change to it on a tracked entity (the
/// <c>Feeder.CommuneId</c> trap of O-7). EF also allows no alternate key over a nullable column.
/// </para>
/// <para>
/// ⚠️ <b>The model snapshot cannot see them</b>, so no later migration will drop or recreate them by itself
/// — and none will notice if one is dropped by hand. <c>CabinetConstraintTests</c> reads
/// <c>pg_constraint</c> to pin that every name here exists.
/// </para>
/// <para>
/// Why a database rule at all: today the only writer of <c>iot_node</c> and <c>feeder_control</c> is raw
/// SQL (<c>scripts/seed_mock_set.py</c>), which no service check reaches.
/// </para>
/// </remarks>
public static class CabinetConstraints
{
    /// <summary>Unique <c>feeder (feeder_id, cabinet_id)</c> — target of <see cref="RelayFeederSameCabinet"/>.</summary>
    public const string FeederCabinetKey = "ux_feeder_feeder_id_cabinet_id";

    /// <summary>Unique <c>iot_node (node_id, cabinet_id)</c> — target of <see cref="RelayNodeSameCabinet"/>.</summary>
    public const string NodeCabinetKey = "ux_iot_node_node_id_cabinet_id";

    /// <summary>Unique <c>electrical_cabinet (cabinet_id, data_source)</c> — target of <see cref="NodeCabinetSource"/>.</summary>
    public const string CabinetSourceKey = "ux_electrical_cabinet_cabinet_id_data_source";

    /// <summary>
    /// <c>feeder_control (feeder_id, cabinet_id) → feeder</c>. With its twin below: a relay switches only a
    /// feeder of the device's own cabinet (CAB-4). It also refuses moving or detaching a switched feeder.
    /// </summary>
    public const string RelayFeederSameCabinet = "fk_feeder_control_feeder_same_cabinet";

    /// <summary><c>feeder_control (node_id, cabinet_id) → iot_node</c>.</summary>
    public const string RelayNodeSameCabinet = "fk_feeder_control_node_same_cabinet";

    /// <summary>
    /// <c>iot_node (cabinet_id, cabinet_data_source) → electrical_cabinet (cabinet_id, data_source)</c>,
    /// <b>ON UPDATE CASCADE</b>: the device's copy of its cabinet's provenance follows the cabinet, and
    /// <see cref="DeviceNotOnFieldCabinet"/> then refuses a cabinet turned <c>field</c> under a device (CAB-5).
    /// </summary>
    /// <remarks>
    /// The cascade rewrites only <c>cabinet_data_source</c> on rows of the SAME commune (the device's own
    /// cabinet), so the blind spot of <c>CommuneWriteGuard</c> for database cascades (CLAUDE.md 1c) does not open.
    /// </remarks>
    public const string NodeCabinetSource = "fk_iot_node_cabinet_data_source";

    /// <summary>CHECK on <c>iot_node.cabinet_data_source</c>: never <c>field</c> (D-R10). Declared in EF.</summary>
    public const string DeviceNotOnFieldCabinet = "ck_iot_node_cabinet_not_field";
}
