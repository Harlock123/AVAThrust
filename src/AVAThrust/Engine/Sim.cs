namespace AVAThrust.Engine;

/// <summary>
/// A planet, step by step: the ship turning and thrusting against gravity, the tractor beam taking up fuel and the
/// pod, the pod swinging on its rod, the limpet guns, shots, the reactor, and the way out into space.
/// </summary>
public static class Sim
{
    private const double Dt = Field.Step;
    public const double ShotSpeed = 380, ShotLife = 1.3, FireGap = 0.18, GunShotSpeed = 150, GunRange = 430;
    public const double ThrustBurn = 12, ShieldBurn = 18, TractorReach = 78, FuelRate = 260;
    public const double ReactorHits = 6, QuietTime = 8, CountdownTime = 10;
    public const int GunPoints = 750, TankPoints = 150;

    public static void Start(World w, PlanetDef p, int seed)
    {
        w.Planet = p;
        w.Dice = new Dice(seed);
        w.Guns = p.Guns.Select(g => new Gun { Def = g, Cool = 1 + w.Dice.Next() * 2 }).ToList();
        w.Tanks = p.Fuel.Select(f => new Tank { X = f.X, Y = f.Y }).ToList();
        w.Pod = new Pod { X = p.Pod.X, Y = p.Pod.Y };
        w.ReactorHeat = 0;
        w.Quiet = 0;
        w.Countdown = -1;
        w.Clock = 0;
        // (Each planet starts with at least a full tank.)
        double fuel = Math.Max(w.Ship.Fuel, Ship.Fuel0);
        Respawn(w);
        w.Ship.Fuel = fuel;
    }

    /// <summary>A fresh ship up in space over the way in (the pod back on its pedestal if it was lost).</summary>
    public static void Respawn(World w)
    {
        var p = w.Planet;
        double fuel = w.Ship.Fuel;
        // (A new ship comes with a full tank.)
        w.Ship = new Ship { X = p.Start.X, Y = p.Start.Y, Fuel = w.Ship.Dead ? Math.Max(fuel, Ship.Fuel0) : fuel };
        if (w.Pod.Attached || w.Pod.Lost) w.Pod = new Pod { X = p.Pod.X, Y = p.Pod.Y };
        w.Shots.Clear();
        w.Escaped = false;
        w.Cause = "";
        w.Draining = -1;
    }

    public static void Step(World w, Controls c)
    {
        w.Clock += Dt;
        foreach (var b in w.Bursts) b.T += Dt;
        w.Bursts.RemoveAll(b => b.T > 0.9);
        var s = w.Ship;
        w.ReactorHeat = Math.Max(0, w.ReactorHeat - Dt * 0.25);
        if (w.Quiet > 0) w.Quiet -= Dt;
        if (w.Countdown >= 0 && !w.Escaped)
        {
            w.Countdown -= Dt;
            if (w.Countdown <= 0 && !s.Dead) { w.Countdown = 0; Die(w, "THE PLANET BLEW UP"); }
        }
        if (!s.Dead && !w.Escaped) Fly(w, s, c);
        Guns(w);
        Shots(w);
    }

    private static void Fly(World w, Ship s, Controls c)
    {
        var p = w.Planet;
        s.Angle += c.Dx * Ship.Turn * Dt;
        s.Thrusting = c.Dy < 0 && s.Fuel > 0;
        s.Shielding = c.Shield && s.Fuel > 0;
        double ax = 0, ay = p.Gravity;
        if (s.Thrusting)
        {
            ax += Math.Sin(s.Angle) * Ship.Thrust;
            ay -= Math.Cos(s.Angle) * Ship.Thrust;
            s.Fuel = Math.Max(0, s.Fuel - ThrustBurn * Dt);
        }
        if (s.Shielding) s.Fuel = Math.Max(0, s.Fuel - ShieldBurn * Dt);
        s.Vx += ax * Dt;
        s.Vy += ay * Dt;
        double sp = Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy);
        if (sp > Ship.MaxSpeed) { s.Vx *= Ship.MaxSpeed / sp; s.Vy *= Ship.MaxSpeed / sp; }
        s.X += s.Vx * Dt;
        s.Y += s.Vy * Dt;
        var pod = w.Pod;
        if (pod.Attached)
        {
            if (!pod.Anchored)
            {
                pod.Vy += p.Gravity * Dt;
                pod.X += pod.Vx * Dt;
                pod.Y += pod.Vy * Dt;
            }
            pod.Length += Math.Clamp(Pod.Rod - pod.Length, -30 * Dt, 30 * Dt);
            // (The rod shares the thrust out between the ship and the heavier pod.)
            Rod(s, pod);
        }
        // Into the rock: the end of the ship.
        foreach (var (hx, hy) in s.Hull())
            if (p.Solid(hx, hy)) { Die(w, "CRASHED"); return; }
        if (pod.Attached && !pod.Anchored)
            for (int k = 0; k < 8; k++)
            {
                double a = k * Math.PI / 4;
                if (p.Solid(pod.X + Math.Cos(a) * Pod.Radius, pod.Y + Math.Sin(a) * Pod.Radius)) { pod.Lost = true; Die(w, "THE POD HIT THE ROCK"); return; }
            }
        if (Math.Abs(s.X - p.Reactor.X) < 30 && s.Y > p.Reactor.Y - 34 && s.Y < p.Reactor.Y) { Die(w, "CRASHED"); return; }
        // The tractor beam: fuel from a tank under the ship, or the pod.
        w.Draining = -1;
        if (s.Shielding)
        {
            for (int i = 0; i < w.Tanks.Count; i++)
            {
                var t = w.Tanks[i];
                if (t.Dead || t.Fuel <= 0 || Math.Abs(t.X - s.X) > 26 || t.Y - s.Y < 0 || t.Y - s.Y > TractorReach + 20) continue;
                double take = Math.Min(t.Fuel, FuelRate * Dt);
                t.Fuel -= take;
                s.Fuel = Math.Min(Ship.Fuel0 * 2, s.Fuel + take);
                w.Draining = i;
                if (t.Fuel <= 0) { t.Dead = true; w.Heard.Add(Sfx.Fuel); }
                break;
            }
            if (!pod.Attached && !pod.Lost && Dist(s.X, s.Y, pod.X, pod.Y) < TractorReach)
            {
                pod.Attached = true;
                pod.Anchored = true;
                pod.Length = Math.Max(30, Dist(s.X, s.Y, pod.X, pod.Y));
                pod.Vx = pod.Vy = 0;
                w.Heard.Add(Sfx.Tractor);
            }
        }
        if (c.Fire && !s.FirePrev && s.FireTimer <= 0 && w.Shots.Count(x => x.Mine) < 5)
        {
            s.FireTimer = FireGap;
            double sx = Math.Sin(s.Angle), sy = -Math.Cos(s.Angle);
            w.Shots.Add(new Shot { X = s.X + sx * 14, Y = s.Y + sy * 14, Vx = s.Vx + sx * ShotSpeed, Vy = s.Vy + sy * ShotSpeed, Life = ShotLife, Mine = true });
            w.Heard.Add(Sfx.Fire);
        }
        s.FirePrev = c.Fire;
        s.FireTimer -= Dt;
        // Out into space.
        if (s.Y < 0)
        {
            w.Escaped = true;
            w.Heard.Add(Sfx.Escape);
        }
        if (s.X < 4) { s.X = 4; s.Vx = Math.Abs(s.Vx) * 0.3; }
        if (s.X > Field.Cols * Field.T - 4) { s.X = Field.Cols * Field.T - 4; s.Vx = -Math.Abs(s.Vx) * 0.3; }
    }

    /// <summary>
    /// The rod: holds the pod at its length from the ship, the heavier pod moving the less. While the pod still sits
    /// on its pedestal the ship alone gives way, until the rod pulls the pod up off it.
    /// </summary>
    public static void Rod(Ship s, Pod pod)
    {
        double dx = pod.X - s.X, dy = pod.Y - s.Y, len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6) return;
        double nx = dx / len, ny = dy / len, err = len - pod.Length;
        if (pod.Anchored)
        {
            if (err > 0 && ny > 0.3) pod.Anchored = false;
            else
            {
                s.X += nx * err;
                s.Y += ny * err;
                double towards = s.Vx * nx + s.Vy * ny;
                if (towards > 0) { s.Vx -= nx * towards; s.Vy -= ny * towards; }
                return;
            }
        }
        double ms = 1, mp = Pod.Mass, m = ms + mp;
        s.X += nx * err * mp / m;
        s.Y += ny * err * mp / m;
        pod.X -= nx * err * ms / m;
        pod.Y -= ny * err * ms / m;
        double along = (pod.Vx - s.Vx) * nx + (pod.Vy - s.Vy) * ny;
        s.Vx += nx * along * mp / m;
        s.Vy += ny * along * mp / m;
        pod.Vx -= nx * along * ms / m;
        pod.Vy -= ny * along * ms / m;
    }

    public static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    /// <summary>A clear line between two points (no rock on it).</summary>
    public static bool Sight(PlanetDef p, double ax, double ay, double bx, double by)
    {
        double d = Dist(ax, ay, bx, by);
        int n = Math.Max(2, (int)(d / 8));
        for (int i = 1; i < n; i++)
            if (p.Solid(ax + (bx - ax) * i / n, ay + (by - ay) * i / n)) return false;
        return true;
    }

    private static void Guns(World w)
    {
        var s = w.Ship;
        foreach (var g in w.Guns)
        {
            if (g.Dead) continue;
            g.Cool -= Dt;
            if (g.Cool > 0 || w.Quiet > 0 || s.Dead || w.Escaped) continue;
            double gx = g.Def.X + g.Def.Nx * 8, gy = g.Def.Y + g.Def.Ny * 8;
            double dx = s.X - gx, dy = s.Y - gy, d = Math.Sqrt(dx * dx + dy * dy);
            // Only out of its face, and only at a ship it can see.
            if (d > GunRange || dx * g.Def.Nx + dy * g.Def.Ny < d * 0.2 || !Sight(w.Planet, gx, gy, s.X, s.Y)) { g.Cool = 0.3; continue; }
            g.Cool = 1.4 + w.Dice.Next() * 1.4 - w.Planet.Level * 0.08;
            double spread = (w.Dice.Next() - 0.5) * 0.3, a = Math.Atan2(dy, dx) + spread;
            w.Shots.Add(new Shot { X = gx, Y = gy, Vx = Math.Cos(a) * GunShotSpeed, Vy = Math.Sin(a) * GunShotSpeed, Life = 3.2 });
            w.Heard.Add(Sfx.GunFire);
        }
    }

    private static void Shots(World w)
    {
        var p = w.Planet;
        var s = w.Ship;
        foreach (var b in w.Shots)
        {
            b.X += b.Vx * Dt;
            b.Y += b.Vy * Dt;
            b.Life -= Dt;
            if (b.Life <= 0) continue;
            if (p.Solid(b.X, b.Y)) { b.Life = 0; continue; }
            if (b.Mine)
            {
                foreach (var g in w.Guns)
                    if (!g.Dead && Dist(b.X, b.Y, g.Def.X, g.Def.Y) < 14)
                    {
                        g.Dead = true;
                        b.Life = 0;
                        w.Score += GunPoints;
                        w.Bursts.Add(new Burst(g.Def.X, g.Def.Y, true));
                        w.Heard.Add(Sfx.GunDown);
                    }
                foreach (var t in w.Tanks)
                    if (!t.Dead && Math.Abs(b.X - t.X) < 13 && b.Y > t.Y - 22 && b.Y < t.Y)
                    {
                        t.Dead = true;
                        b.Life = 0;
                        w.Score += TankPoints;
                        w.Bursts.Add(new Burst(t.X, t.Y - 10, true));
                        w.Heard.Add(Sfx.Boom);
                    }
                if (Math.Abs(b.X - p.Reactor.X) < 26 && b.Y > p.Reactor.Y - 34 && b.Y < p.Reactor.Y)
                {
                    b.Life = 0;
                    w.ReactorHeat += 1;
                    w.Quiet = QuietTime;
                    w.Bursts.Add(new Burst(b.X, b.Y, false));
                    w.Heard.Add(Sfx.ReactorHit);
                    if (w.ReactorHeat >= ReactorHits && w.Countdown < 0) { w.Countdown = CountdownTime; w.Heard.Add(Sfx.Critical); }
                }
            }
            else if (!s.Dead && !w.Escaped)
            {
                if (Dist(b.X, b.Y, s.X, s.Y) < Ship.Radius + 2)
                {
                    b.Life = 0;
                    if (s.Shielding) w.Heard.Add(Sfx.Deflect);
                    else Die(w, "SHOT DOWN");
                }
                else if (w.Pod.Attached && Dist(b.X, b.Y, w.Pod.X, w.Pod.Y) < Pod.Radius + 2)
                {
                    b.Life = 0;
                    if (s.Shielding) w.Heard.Add(Sfx.Deflect);
                    else { w.Pod.Lost = true; Die(w, "THE POD WAS SHOT"); }
                }
            }
        }
        w.Shots.RemoveAll(b => b.Life <= 0);
    }

    public static void Die(World w, string cause)
    {
        var s = w.Ship;
        if (s.Dead) return;
        s.Dead = true;
        w.Cause = cause;
        w.Bursts.Add(new Burst(s.X, s.Y, true));
        if (w.Pod.Attached) w.Bursts.Add(new Burst(w.Pod.X, w.Pod.Y, true));
        w.Heard.Add(Sfx.Explode);
    }
}
