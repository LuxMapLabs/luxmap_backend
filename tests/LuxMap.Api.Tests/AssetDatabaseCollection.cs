namespace LuxMap.Api.Tests;

/// <summary>
/// The one collection for every test class that writes assets. Sixteen classes, one shared
/// development database, and therefore NO parallelism between them.
/// </summary>
/// <remarks>
/// <b>Why this is one collection and must stay one.</b> xUnit runs classes of the SAME collection
/// one after another and classes of DIFFERENT collections at the same time. These classes were split
/// across <c>AssetSchemaCollection</c> and <c>AssetImportCollection</c>, so half of them inserted
/// poles while the other half was mid-test — and they all draw from a single global
/// <c>pole_id_seq</c>. <see cref="PrefixedIdOverflowTests"/> pins that sequence with <c>setval</c> to
/// reach a padding-width boundary, which is not something another writer can be running alongside:
/// the rewind hands the neighbour an id that is already taken, and the neighbour's <c>nextval</c>
/// steals the id the pin was aimed at. Either way the loser dies on <c>pk_pole</c> with nothing in
/// the message about a race.
/// <para>
/// Splitting them again for speed reopens exactly that, and it was measured rather than assumed —
/// five full runs each way, same machine, same database, immediately after one another:
/// </para>
/// <list type="table">
///   <item><description>split across two collections — 4.1–4.3 s, and <b>4 of 5 runs red</b></description></item>
///   <item><description>merged into this one — 5.2–5.4 s, and <b>5 of 5 runs green</b></description></item>
/// </list>
/// <para>
/// About one second, for a suite that otherwise fails a different set of tests on every run.
/// </para>
/// <para>
/// Both fixtures are declared here, so both are built for any run that touches either — including a
/// filtered run of a single class, which now also pays for <see cref="AssetSchemaFixture"/>'s
/// 2500-pole seed. That is the cost of the guarantee, not an oversight.
/// </para>
/// </remarks>
[CollectionDefinition(nameof(AssetDatabaseCollection))]
public sealed class AssetDatabaseCollection
    : ICollectionFixture<AssetSchemaFixture>, ICollectionFixture<AssetImportFixture>;
