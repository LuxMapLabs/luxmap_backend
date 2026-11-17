using LuxMap.Shared.Contracts.Communes;

namespace LuxMap.Shared.Tests;

/// <summary>MAP-CENTER — the map opens on the poles, kept inside the frame; without poles, on the frame's midpoint.</summary>
public class MapFocusTests
{
    private static readonly CommuneBbox Frame = new(107.20, 10.40, 107.40, 10.60);

    [Fact]
    public void A_commune_opens_on_its_poles_not_on_the_middle_of_its_frame()
    {
        Assert.Equal(new MapCenter(10.42, 107.22), MapFocus.Center(Frame, new PoleCenter(10.42, 107.22, 3)));
    }

    [Fact]
    public void Poles_outside_the_frame_are_pulled_to_its_nearest_edge()
    {
        Assert.Equal(new MapCenter(10.60, 107.30), MapFocus.Center(Frame, new PoleCenter(10.70, 107.30, 1)));
    }

    [Fact]
    public void Without_poles_the_frame_midpoint_is_kept_and_without_a_frame_there_is_no_centre()
    {
        Assert.Equal(Frame.Center(), MapFocus.Center(Frame, null));
        Assert.Equal(Frame.Center(), MapFocus.Center(Frame, new PoleCenter(10.42, 107.22, 0)));
        Assert.Null(MapFocus.Center(null, new PoleCenter(10.42, 107.22, 3)));
    }

    /// <summary>A three-pole commune far away must not drag the map halfway to it; an unframed commune does not count.</summary>
    [Fact]
    public void Several_communes_open_on_the_pole_weighted_mean_inside_the_box_around_their_frames()
    {
        var far = new CommuneBbox(106.48, 10.95, 106.51, 10.98);

        var bounds = MapFocus.Bounds(
        [
            (Frame, new PoleCenter(10.50, 107.30, 9)),
            (far, new PoleCenter(10.96, 106.50, 1)),
            (null, new PoleCenter(10.00, 100.00, 1000)),
        ]);

        Assert.NotNull(bounds);
        Assert.Equal(new CommuneBbox(106.48, 10.40, 107.40, 10.98), bounds.Bbox);
        Assert.Equal(10.546, bounds.Center.Lat, 9);
        Assert.Equal(107.22, bounds.Center.Lng, 9);
    }

    [Fact]
    public void Several_communes_without_poles_open_on_the_box_midpoint_and_none_framed_is_null()
    {
        var other = new CommuneBbox(107.40, 10.60, 107.60, 10.80);

        var center = MapFocus.Bounds([(Frame, null), (other, null)])!.Center;
        Assert.Equal(10.60, center.Lat, 9);
        Assert.Equal(107.40, center.Lng, 9);
        Assert.Null(MapFocus.Bounds([(null, new PoleCenter(10.5, 107.3, 4))]));
    }
}
