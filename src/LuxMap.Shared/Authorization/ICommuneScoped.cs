namespace LuxMap.Shared.Authorization;

/// <summary>
/// An entity that carries <c>commune_id</c> directly and MUST be restricted to the caller's
/// commune scope (Contract section 7).
/// </summary>
/// <remarks>
/// Implementing this interface is a promise with teeth: <c>LuxMapDbContext</c> verifies it while
/// building the model, and an entity that implements it without calling <c>HasCommuneScope()</c>
/// makes <b>the application fail to start</b>. Forgetting the scope becomes a loud error instead
/// of a silent leak.
/// <para>
/// ⚠️ The guard only sees entities that ALREADY implement this interface. An entity that has
/// <c>commune_id</c> but forgets to implement it slips through — that is a real limit, and only
/// code review catches it.
/// </para>
/// <para>
/// Entities whose commune is several relationships away (<c>SurveyFrame</c>,
/// <c>TelemetryReading</c>) do NOT implement this; they go through explicit scoped queries.
/// </para>
/// </remarks>
public interface ICommuneScoped
{
    string CommuneId { get; }
}
