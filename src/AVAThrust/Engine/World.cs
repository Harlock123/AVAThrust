namespace AVAThrust.Engine;

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>The ship: where, how fast, which way it points (0 is up, turning clockwise), and its fuel.</summary>
public sealed class Ship
{
    public const double Turn = 4.2, Thrust = 300, MaxSpeed = 420, Fuel0 = 1000, Radius = 11;
    public double X, Y, Vx, Vy, Angle, Fuel = Fuel0, FireTimer;
    public bool Dead, Thrusting, Shielding, FirePrev;
    public Ship Copy() => (Ship)MemberwiseClone();

    /// <summary>The hull's three corners: the nose, and the two back corners.</summary>
    public IEnumerable<(double X, double Y)> Hull()
    {
        double s = Math.Sin(Angle), c = Math.Cos(Angle);
        foreach (var (px, py) in new[] { (0.0, -13.0), (-10.0, 9.0), (10.0, 9.0) })
            yield return (X + px * c - py * s, Y + px * s + py * c);
    }
}

/// <summary>The pod: on its pedestal, or swinging on the rod under the ship.</summary>
public sealed class Pod
{
    public const double Radius = 10, Rod = 56, Mass = 1.0;
    public double X, Y, Vx, Vy;
    /// <summary>The rod's length now: from where the beam caught the pod, easing to its full length.</summary>
    public double Length = Rod;
    /// <summary>Still sitting on its pedestal (until the rod lifts it).</summary>
    public bool Anchored = true;
    public bool Attached, Lost;
    public Pod Copy() => (Pod)MemberwiseClone();
}

public sealed class Gun
{
    public GunDef Def;
    public double Cool;
    public bool Dead;
    public Gun Copy() => (Gun)MemberwiseClone();
}

public sealed class Tank
{
    public double X, Y, Fuel = 400;
    public bool Dead;
    public Tank Copy() => (Tank)MemberwiseClone();
}

public sealed class Shot
{
    public double X, Y, Vx, Vy, Life;
    public bool Mine;
    public Shot Copy() => (Shot)MemberwiseClone();
}

public sealed record Burst(double X, double Y, bool Big)
{
    public double T;
}

/// <summary>Everything on a planet that moves or changes, kept together so it can be copied whole.</summary>
public sealed class World
{
    public PlanetDef Planet = null!;
    public Ship Ship = new();
    public Pod Pod = new();
    public List<Gun> Guns = [];
    public List<Tank> Tanks = [];
    public List<Shot> Shots = [];
    public List<Burst> Bursts = [];
    public List<Sfx> Heard = [];
    public Dice Dice = new(1);
    public double Clock;
    /// <summary>The reactor: how hot the shots have made it (it cools); quiet (guns silent) for a while after a hit; and,
    /// once it's taken too much, the countdown to the planet blowing up.</summary>
    public double ReactorHeat, Quiet, Countdown = -1;
    /// <summary>Out into space: with the pod (the planet done) or without it.</summary>
    public bool Escaped;
    public int Score;
    public string Cause = "";
    /// <summary>Which tank the tractor beam is draining (or -1).</summary>
    public int Draining = -1;
    public bool ReactorCritical => Countdown >= 0;

    public World Copy() => new()
    {
        Planet = Planet, Ship = Ship.Copy(), Pod = Pod.Copy(), Guns = Guns.Select(g => g.Copy()).ToList(), Tanks = Tanks.Select(t => t.Copy()).ToList(),
        Shots = Shots.Select(s => s.Copy()).ToList(), Dice = Dice, Clock = Clock, ReactorHeat = ReactorHeat, Quiet = Quiet, Countdown = Countdown,
        Escaped = Escaped, Score = Score, Cause = Cause, Draining = Draining,
    };
}
