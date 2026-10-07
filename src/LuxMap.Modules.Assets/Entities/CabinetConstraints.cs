namespace LuxMap.Modules.Assets.Entities;

/// <summary>
/// The cabinet rule that lives in the DATABASE ONLY (CAB-4), created by raw SQL in migration
/// <c>AddElectricalCabinet</c>. The EF model does not know it.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Why not EF keys.</b> Each foreign key below needs a unique target that includes a column people
/// legitimately change — <c>feeder.cabinet_id</c>, <c>iot_node.cabinet_id</c>. Declared through
/// <c>HasAlternateKey</c> that column becomes a key property, and EF throws from <c>DetectChanges</c> on any
/// change to it on a tracked entity (the <c>Feeder.CommuneId</c> trap of O-7). EF also allows no alternate key
/// over a nullable column.
/// </para>
/// <para>
/// ⚠️ <b>The model snapshot cannot see them</b>, so no later migration will drop or recreate them by itself
/// — and none will notice if one is dropped by hand. <c>CabinetTests</c> reads <c>pg_constraint</c> to pin
/// that every name here exists.
/// </para>
/// <para>
/// Why a database rule at all: today the only writer of <c>iot_node</c> and <c>feeder_control</c> is raw
/// SQL (<c>scripts/seed_mock_set.py</c>), which no service check reaches.
/// </para>
/// <para>
/// CAB-5 (no device on a <c>field</c> cabinet) and its three constraints were REMOVED on 07/10/2026 —
/// migration <c>DropFieldCabinetRule</c>; Mỹ: the rule is not needed. A device itself is still never
/// <c>field</c> (<c>ck_iot_node_data_source_not_field</c>).
/// </para>
/// </remarks>
public static class CabinetConstraints
{
    /// <summary>Unique <c>feeder (feeder_id, cabinet_id)</c> — target of <see cref="RelayFeederSameCabinet"/>.</summary>
    public const string FeederCabinetKey = "ux_feeder_feeder_id_cabinet_id";

    /// <summary>Unique <c>iot_node (node_id, cabinet_id)</c> — target of <see cref="RelayNodeSameCabinet"/>.</summary>
    public const string NodeCabinetKey = "ux_iot_node_node_id_cabinet_id";

    /// <summary>
    /// <c>feeder_control (feeder_id, cabinet_id) → feeder</c>. With its twin below: a relay switches only a
    /// feeder of the device's own cabinet (CAB-4). It also refuses moving or detaching a switched feeder.
    /// </summary>
    public const string RelayFeederSameCabinet = "fk_feeder_control_feeder_same_cabinet";

    /// <summary><c>feeder_control (node_id, cabinet_id) → iot_node</c>.</summary>
    public const string RelayNodeSameCabinet = "fk_feeder_control_node_same_cabinet";
}
