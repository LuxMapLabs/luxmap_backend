namespace LuxMap.Modules.Assets;

/// <summary>
/// How far from its own road segment a pole may stand and still be seen by a survey (POLE-LOCATION), asked from the Assets side.
/// </summary>
/// <remarks>
/// <para>
/// A port because Assets cannot reference Survey — the reference already runs the other way. Same shape as
/// <see cref="ICabinetDeviceLookup"/>. Survey implements it from <c>SurveyProcessing:RouteCorridorM</c>, the very value its
/// pole query filters on.
/// </para>
/// <para>
/// One value, not a copy: a pole moved past it drops out of every later survey without an error anywhere, so the limit a
/// move is checked against must be the limit the survey applies — including after someone retunes it.
/// </para>
/// </remarks>
public interface ISurveyCorridor
{
    /// <summary>Metres, measured in EPSG:3405 between the pole and its segment's line.</summary>
    double RouteCorridorM { get; }
}
