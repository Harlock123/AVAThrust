namespace AVAThrust.Engine;

/// <summary>
/// The computer pilot, for the demonstration and the tests. It finds its way through the caverns square by square
/// (where there's room for the ship, and the pod below it), to a fuel tank when it runs low, down to the pod, and
/// with the pod back up and out into space. It flies at the furthest point of the way it can see, turning the ship
/// to point its thrust where it's needed and firing the thruster as often as it must to hold the speed it wants;
/// slower with the pod, which swings. It raises the shield when a shot comes at it, and fires at any gun in its
/// sights.
/// </summary>
public sealed class Pilot
{
    private List<(int C, int R)>? _path;
    private (int C, int R) _goal;
    private double _duty, _replan;
    private bool _firePrev;
    public const double Cruise = 120, Towing = 70;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _path = null;
        _heldFor = null;
        _hold = 0;
        _duty = 0;
        _replan = 0;
        _firePrev = false;
        Doing = "";
    }

    public Controls Decide(GameEngine g) => g.Phase != Phase.Play ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        var s = w.Ship;
        var p = w.Planet;
        if (s.Dead || w.Escaped) return default;
        // Where to: fuel when low, the pod, or (with the pod) out.
        (double X, double Y) target;
        bool tractor = false, towing = w.Pod.Attached;
        int tank = -1;
        // (Low, or not enough for the climb out with the pod, by the depth.)
        if (!towing && (s.Fuel < Ship.Fuel0 * 0.6 || s.Fuel < 250 + p.Height * 0.5))
            tank = Enumerable.Range(0, w.Tanks.Count).Where(i => !w.Tanks[i].Dead && w.Tanks[i].Fuel > 0)
                .OrderBy(i => Sim.Dist(s.X, s.Y, w.Tanks[i].X, w.Tanks[i].Y)).DefaultIfEmpty(-1).First();
        if (tank >= 0)
        {
            var t = w.Tanks[tank];
            target = (t.X, t.Y - 46);
            tractor = Sim.Dist(s.X, s.Y, target.X, target.Y) < 22;
            Doing = "after fuel";
        }
        else if (!towing)
        {
            target = (w.Pod.X, w.Pod.Y - 60);
            // (Only once all but still: grabbing on the way down pushes the pod into its pedestal.)
            tractor = Sim.Dist(s.X, s.Y, w.Pod.X, w.Pod.Y) < Sim.TractorReach - 8 && Math.Abs(s.X - w.Pod.X) < 14 && Math.Abs(s.Vx) < 15 && Math.Abs(s.Vy) < 15;
            Doing = "down to the pod";
        }
        else
        {
            target = (p.Start.X, -60);
            Doing = "out with the pod";
        }
        var here = p.Cell(s.X, s.Y);
        var goal = p.Cell(target.X, Math.Max(2, target.Y));
        _replan -= Field.Step;
        if (_path == null || goal != _goal || _replan <= 0 || !_path.Contains(here))
        {
            _goal = goal;
            _replan = 1;
            _path = (towing ? PlanetBook.Route(p, here, goal, 1, tow: true) : null) ?? PlanetBook.Route(p, here, goal, 1) ?? PlanetBook.Route(p, here, goal, 0);
        }
        // The furthest square on the way that can be seen, with room for the ship.
        (double X, double Y) aim = target;
        if (_path != null && _path.Count > 1)
        {
            int at = Math.Max(0, _path.IndexOf(here));
            int ahead = towing ? 3 : 6;
            aim = Centre(_path[Math.Min(_path.Count - 1, at + 1)]);
            // (Towing, the pod must have a clear way too, a rod's length below.)
            bool Seen((double X, double Y) c) => Clear(p, s.X, s.Y, c.X, c.Y) && (!towing || Clear(p, w.Pod.X, w.Pod.Y, c.X, c.Y + Pod.Rod));
            var two = Centre(_path[Math.Min(_path.Count - 1, at + 2)]);
            if (Seen(two)) aim = two;
            for (int i = Math.Min(_path.Count - 1, at + ahead); i > at; i--)
            {
                var c = Centre(_path[i]);
                if (Seen(c)) { aim = c; break; }
            }
            if (at + ahead >= _path.Count - 1 && Seen(target)) aim = target;
        }
        double dx = aim.X - s.X, dy = aim.Y - s.Y, dist = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        double left = Sim.Dist(s.X, s.Y, target.X, target.Y);
        double cruise = towing ? Towing : Cruise;
        double speed = Math.Min(cruise, Math.Max(towing ? 30 : 20, left * 1.2));
        double vx = dx / dist * speed, vy = dy / dist * speed;
        if (towing)
        {
            // Towing: try a spread of ways on, each played out on a copy, and take the one that gets furthest along the
            // way without the pod (or the ship) touching rock.
            if (Sim.Dist(w.Pod.X, w.Pod.Y, p.Pod.X, p.Pod.Y) < 50) { vx = 0; vy = -40; }
            else if (s.Y < (Field.Space - 1) * Field.T) { vx = 0; vy = -Towing; }
            else
            {
                if (--_hold <= 0 || _path != _heldFor)
                {
                    _hold = 10;
                    _heldFor = _path;
                    _want = Choose(w, dx, dy);
                }
                (vx, vy) = _want;
            }
        }
        var ctl = Steer(w, s, vx, vy, ref _duty);
        return ctl with { Shield = tractor || Danger(w, s), Fire = Shoot(w, s) };
    }

    private int _hold;
    private List<(int C, int R)>? _heldFor;
    private (double X, double Y) _want;

    /// <summary>The best of a spread of velocities toward the aim, by trying each out for a second and a quarter.</summary>
    private (double, double) Choose(World w, double dx, double dy)
    {
        var p = w.Planet;
        double head = Math.Atan2(dy, dx);
        var tries = new List<(double, double)> { (0, 0), (0, -25), (0, -45) };
        foreach (double turn in new[] { 0, -0.5, 0.5, -1.0, 1.0, -1.6, 1.6 })
            foreach (double sp in new[] { 15.0, 30.0, 45.0 })
                tries.Add((Math.Cos(head + turn) * sp, Math.Sin(head + turn) * sp));
        double bestScore = double.MinValue;
        var best = (0.0, 0.0);
        foreach (var (vx, vy) in tries)
        {
            var c = w.Copy();
            double duty = _duty;
            bool ok = true;
            int close = 0;
            for (int i = 0; i < 100 && ok; i++)
            {
                var ctl = Steer(c, c.Ship, vx, vy, ref duty);
                Sim.Step(c, ctl);
                if (c.Ship.Dead) ok = false;
                // (Near misses count against it: the swing that follows may not miss.)
                if (i % 5 == 0)
                    for (int k = 0; k < 8; k++)
                    {
                        double a = k * Math.PI / 4;
                        if (p.Solid(c.Pod.X + Math.Cos(a) * (Pod.Radius + 12), c.Pod.Y + Math.Sin(a) * (Pod.Radius + 12))) close++;
                    }
            }
            if (!ok) continue;
            double score = Progress(p, c.Ship.X, c.Ship.Y) - Math.Abs(c.Pod.X - c.Ship.X) * 0.02 - close * 0.4;
            if (score > bestScore) { bestScore = score; best = (vx, vy); }
        }
        return best;
    }

    /// <summary>How far along the way a point is (in squares), less how far off it.</summary>
    private double Progress(PlanetDef p, double x, double y)
    {
        if (_path == null) return 0;
        double best = double.MinValue;
        for (int i = 0; i < _path.Count; i++)
        {
            var c = Centre(_path[i]);
            double off = Sim.Dist(x, y, c.X, c.Y) / Field.T;
            if (off < 3) best = Math.Max(best, i - off * 0.5);
        }
        return best == double.MinValue ? -1000 : best;
    }

    private static (double X, double Y) Centre((int C, int R) c) => ((c.C + 0.5) * Field.T, (c.R + 0.5) * Field.T);

    /// <summary>Turn so the thrust points where it's wanted, and fire it as often as it takes.</summary>
    private static Controls Steer(World w, Ship s, double vx, double vy, ref double duty)
    {
        double g = w.Planet.Gravity * (w.Pod.Attached ? 1 + Pod.Mass : 1);
        double k = w.Pod.Attached ? 1.6 : 2.4;
        double ax = Math.Clamp((vx - s.Vx) * k, -160, 160), ay = Math.Clamp((vy - s.Vy) * k, -160, 160);
        if (w.Pod.Attached && !w.Pod.Anchored)
        {
            // Damp the swing: keep the ship over the pod, moving with it.
            var pod = w.Pod;
            ax = Math.Clamp(ax + (pod.X - s.X) * 0.6 + (pod.Vx - s.Vx) * 2.0, -160, 160);
        }
        // Thrust must give this, and hold up against gravity too.
        double tx = ax * (w.Pod.Attached ? 1 + Pod.Mass : 1), ty = ay * (w.Pod.Attached ? 1 + Pod.Mass : 1) - g;
        // (Never point down: thrusting down only speeds a fall.)
        if (ty > -20) ty = -20;
        double want = Math.Atan2(tx, -ty);
        want = Math.Clamp(want, -1.3, 1.3);
        double err = Wrap(want - s.Angle);
        int turn = Math.Abs(err) < 0.04 ? 0 : Math.Sign(err);
        double need = Math.Sqrt(tx * tx + ty * ty) / Ship.Thrust;
        bool thrust = false;
        if (Math.Abs(err) < 0.45)
        {
            duty += Math.Min(1, need * Math.Cos(err));
            if (duty >= 1) { duty -= 1; thrust = true; }
        }
        return new Controls(turn, thrust ? -1 : 0);
    }

    private static double Wrap(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    /// <summary>A gun's shot coming close.</summary>
    private static bool Danger(World w, Ship s)
    {
        foreach (var b in w.Shots)
        {
            if (b.Mine) continue;
            double dx = s.X - b.X, dy = s.Y - b.Y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 80 && dx * b.Vx + dy * b.Vy > 0) return true;
            if (w.Pod.Attached && Sim.Dist(w.Pod.X, w.Pod.Y, b.X, b.Y) < 80 && (w.Pod.X - b.X) * b.Vx + (w.Pod.Y - b.Y) * b.Vy > 0) return true;
        }
        return false;
    }

    /// <summary>A gun in the sights: fire (a tap at a time).</summary>
    private bool Shoot(World w, Ship s)
    {
        double fx = Math.Sin(s.Angle), fy = -Math.Cos(s.Angle);
        foreach (var g in w.Guns)
        {
            if (g.Dead) continue;
            double dx = g.Def.X - s.X, dy = g.Def.Y - s.Y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d > 300 || (dx * fx + dy * fy) / d < 0.97 || !Sim.Sight(w.Planet, s.X, s.Y, g.Def.X - g.Def.Nx * -6, g.Def.Y + g.Def.Ny * 6)) continue;
            _firePrev = !_firePrev;
            return _firePrev;
        }
        _firePrev = false;
        return false;
    }

    /// <summary>A clear line between two points, wide enough for the ship.</summary>
    private static bool Clear(PlanetDef p, double ax, double ay, double bx, double by)
    {
        foreach (var (ox, oy) in new[] { (0.0, 0.0), (-14.0, -14.0), (14.0, 14.0), (-14.0, 14.0), (14.0, -14.0) })
            if (!Sim.Sight(p, ax + ox, ay + oy, bx + ox, by + oy)) return false;
        return true;
    }
}
