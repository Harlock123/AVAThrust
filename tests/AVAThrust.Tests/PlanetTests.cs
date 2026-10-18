using AVAThrust.Engine;

namespace AVAThrust.Tests;

public class PlanetTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public void There_is_room_down_to_the_pod_and_back_up_towing_it(int level)
    {
        var p = PlanetBook.Build(level);
        Assert.NotNull(PlanetBook.Route(p, p.Cell(p.Start.X, p.Start.Y), p.Cell(p.Pod.X, p.Pod.Y - 2 * Field.T), 1));
        Assert.NotNull(PlanetBook.Route(p, p.Cell(p.Pod.X, p.Pod.Y - 3 * Field.T), p.Cell(p.Start.X, Field.T), 1, tow: true));
    }

    [Fact]
    public void Each_planet_is_deeper_with_stronger_gravity_and_more_guns()
    {
        PlanetDef? last = null;
        for (int level = 0; level < PlanetBook.Count; level++)
        {
            var p = PlanetBook.Build(level);
            if (last != null)
            {
                Assert.True(p.Rows > last.Rows);
                Assert.True(p.Gravity > last.Gravity);
                Assert.True(p.Guns.Count >= last.Guns.Count);
            }
            last = p;
        }
    }

    [Fact]
    public void The_pod_sits_on_a_pedestal_the_reactor_on_the_surface_and_the_fuel_on_floors()
    {
        var p = PlanetBook.Build(2);
        Assert.False(p.Solid(p.Pod.X, p.Pod.Y));
        Assert.True(p.Solid(p.Pod.X, p.Pod.Y + Pod.Radius + 4));
        Assert.True(p.Solid(p.Reactor.X, p.Reactor.Y + 4));
        Assert.False(p.Solid(p.Reactor.X, p.Reactor.Y - 40));
        Assert.True(p.Reactor.Y < (Field.Space + 8) * Field.T);
        foreach (var (x, y) in p.Fuel)
        {
            Assert.True(p.Solid(x, y + 4));
            Assert.False(p.Solid(x, y - 20));
        }
    }

    [Fact]
    public void Guns_stand_on_rock_and_point_out_into_the_open()
    {
        var p = PlanetBook.Build(4);
        Assert.NotEmpty(p.Guns);
        foreach (var g in p.Guns)
        {
            Assert.True(p.Solid(g.X - g.Nx * 6, g.Y - g.Ny * 6));
            Assert.False(p.Solid(g.X + g.Nx * 16, g.Y + g.Ny * 16));
        }
    }

    [Fact]
    public void Corners_are_cut_at_forty_five_degrees()
    {
        var p = new PlanetDef { Rows = 8, Rock = new bool[Field.Cols, 8] };
        for (int c = 5; c <= 6; c++) for (int r = 2; r <= 3; r++) p.Rock[c, r] = true;
        double t = Field.T;
        // A block of rock: solid in the middle, its outer corners cut away, open beside it.
        Assert.True(p.Solid(6 * t, 3 * t));
        Assert.True(p.Solid(6 * t, 2 * t + 4));
        Assert.False(p.Solid(5 * t + 2, 2 * t + 2));
        Assert.False(p.Solid(4.5 * t, 3 * t));
    }

    [Fact]
    public void The_same_planet_every_time()
    {
        var a = PlanetBook.Build(1);
        var b = PlanetBook.Build(1);
        Assert.Equal(a.Pod, b.Pod);
        Assert.Equal(a.Guns, b.Guns);
    }
}
