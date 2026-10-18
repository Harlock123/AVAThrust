namespace AVAThrust.Engine;

/// <summary>
/// The controls: left and right turn the ship, up fires the thruster; fire; and shield (which is also the tractor
/// beam, for fuel and the pod).
/// </summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false, bool Shield = false);

/// <summary>The screen, and a planet as squares of rock and open space.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>The instrument strip across the top.</summary>
    public const double Panel = 64;
    public const double T = 24;
    public const int Cols = 64;
    /// <summary>Open space above this row; the planet's surface and its caverns below.</summary>
    public const int Space = 10;
}

/// <summary>A limpet gun on the rock, and which way it points (out of the rock).</summary>
public readonly record struct GunDef(double X, double Y, double Nx, double Ny);

/// <summary>A planet: its rock, the pod on its pedestal, the reactor, the guns and the fuel.</summary>
public sealed class PlanetDef
{
    public int Level, Rows;
    public string Name = "";
    /// <summary>Rock or not, square by square.</summary>
    public bool[,] Rock = new bool[0, 0];
    public double Gravity;
    public (double X, double Y) Pod, Reactor, Start;
    public List<GunDef> Guns = [];
    public List<(double X, double Y)> Fuel = [];
    public uint RockColour;
    public double Height => Rows * Field.T;

    public bool RockAt(int c, int r) => c < 0 || c >= Field.Cols || r >= Rows || r >= 0 && Rock[c, r];

    /// <summary>
    /// How solid a corner of the squares is (0 to 1): the share of the four squares round it that are rock. The
    /// rock's edge is where this is a half, so corners are cut at forty-five degrees, the way the planets look.
    /// </summary>
    public double Corner(int i, int j) =>
        ((RockAt(i - 1, j - 1) ? 1 : 0) + (RockAt(i, j - 1) ? 1 : 0) + (RockAt(i - 1, j) ? 1 : 0) + (RockAt(i, j) ? 1 : 0)) / 4.0;

    /// <summary>Whether a point is in the rock: the corners' solidity, blended across the square.</summary>
    public bool Solid(double x, double y)
    {
        if (y < 0) return false;
        if (x < 0 || x >= Field.Cols * Field.T || y >= Height) return true;
        double fx = x / Field.T, fy = y / Field.T;
        int i = (int)Math.Floor(fx), j = (int)Math.Floor(fy);
        double u = fx - i, v = fy - j;
        double a = Corner(i, j), b = Corner(i + 1, j), c = Corner(i, j + 1), d = Corner(i + 1, j + 1);
        // Within a square, the cut corner runs straight across: take the larger of the two triangle blends.
        double s = a * (1 - u) * (1 - v) + b * u * (1 - v) + c * (1 - u) * v + d * u * v;
        return s > 0.5;
    }

    public (int C, int R) Cell(double x, double y) => ((int)Math.Floor(x / Field.T), (int)Math.Floor(y / Field.T));
}

/// <summary>The planets, each deeper, with stronger gravity and more guns than the last.</summary>
public static class PlanetBook
{
    private static readonly (string Name, int Rows, double Gravity, int Guns, int Turns, uint Colour)[] Plans =
    [
        ("PLANET ONE", 34, 60, 3, 2, 0xFF40C040),
        ("PLANET TWO", 42, 68, 5, 3, 0xFFC08030),
        ("PLANET THREE", 50, 76, 7, 4, 0xFF4080E0),
        ("PLANET FOUR", 56, 84, 9, 5, 0xFFC040C0),
        ("PLANET FIVE", 62, 92, 11, 6, 0xFFE0E040),
        ("PLANET SIX", 68, 100, 13, 7, 0xFFE04040),
    ];

    public static int Count => Plans.Length;

    public static PlanetDef Build(int level)
    {
        var plan = Plans[level % Plans.Length];
        for (int attempt = 0; ; attempt++)
        {
            var p = Try(level, plan, 3000 + level * 131 + attempt * 7919);
            if (p != null) return p;
        }
    }

    private static PlanetDef? Try(int level, (string Name, int Rows, double Gravity, int Guns, int Turns, uint Colour) plan, int seed)
    {
        var d = new Dice(seed);
        int cols = Field.Cols, rows = plan.Rows;
        var p = new PlanetDef { Level = level, Name = plan.Name, Rows = rows, Gravity = plan.Gravity * (1 + level / Plans.Length * 0.3), RockColour = plan.Colour };
        p.Rock = new bool[cols, rows];
        // The surface: rolling ground, with a flat landing near the middle.
        int[] ground = new int[cols];
        double hgt = Field.Space + 3;
        for (int c = 0; c < cols; c++)
        {
            hgt = Math.Clamp(hgt + (d.Next(3) - 1) * 0.8, Field.Space + 1, Field.Space + 6);
            ground[c] = (int)Math.Round(hgt);
        }
        for (int c = 0; c < cols; c++)
        for (int r = 0; r < rows; r++)
            p.Rock[c, r] = r >= ground[c] || c == 0 || c == cols - 1;
        for (int r = 0; r < Field.Space - 2; r++) { p.Rock[0, r] = false; p.Rock[cols - 1, r] = false; }
        // The way down: from a hole in the surface, back and forth, down to the pod's cavern at the bottom.
        void Carve(double x, double y, double rad)
        {
            for (int c = (int)(x - rad - 1); c <= (int)(x + rad + 1); c++)
            for (int r = (int)(y - rad - 1); r <= (int)(y + rad + 1); r++)
                if (c > 0 && c < cols - 1 && r >= 0 && r < rows - 1 && (c + 0.5 - x) * (c + 0.5 - x) + (r + 0.5 - y) * (r + 0.5 - y) <= rad * rad) p.Rock[c, r] = false;
        }
        double cx = 12 + d.Next(cols - 24), cy = ground[(int)cx] - 1;
        var way = new List<(double X, double Y)> { (cx, cy - 2) };
        int turns = plan.Turns;
        double bottom = rows - 6;
        for (int k = 0; k <= turns; k++)
        {
            // Down a shaft...
            double ny = Field.Space + 6 + (bottom - Field.Space - 6) * (k + 1) / (turns + 1);
            while (cy < ny) { cy += 0.5; Carve(cx, cy, 3.0); way.Add((cx, cy)); }
            if (k == turns) break;
            // ...then along a gallery, the other way each time.
            double nx = Math.Clamp(cx + (d.Next(2) == 0 ? -1 : 1) * (10 + d.Next(18)), 6, cols - 7);
            int dir = Math.Sign(nx - cx);
            while (Math.Abs(cx - nx) > 0.5) { cx += dir * 0.5; Carve(cx, cy, 3.0); way.Add((cx, cy)); }
        }
        // The pod's cavern: wide, with a flat floor; the pod on its pedestal in the middle.
        for (double x = cx - 7; x <= cx + 7; x += 0.5) Carve(Math.Clamp(x, 4, cols - 5), bottom - 2.5, 3.5);
        int floor = (int)bottom + 1;
        for (int c = (int)cx - 9; c <= (int)cx + 9; c++)
            if (c > 0 && c < cols - 1)
            {
                for (int r = floor; r < rows; r++) p.Rock[c, r] = true;
                for (int r = floor - 6; r < floor; r++) if (Math.Abs(c - cx) < 8) p.Rock[c, r] = false;
            }
        // (Three squares wide, so its top is flat.)
        for (int c = (int)cx - 1; c <= (int)cx + 1; c++) p.Rock[c, floor - 1] = true;
        p.Pod = (((int)cx + 0.5) * Field.T, (floor - 1) * Field.T - Pod.Radius - 1);
        // The reactor: on a flat stretch of the surface, away from the hole.
        int rc = -1;
        for (int tries = 0; tries < 200 && rc < 0; tries++)
        {
            int c = 4 + d.Next(cols - 8);
            if (Math.Abs(c - way[0].X) > 8 && ground[c - 1] == ground[c] && ground[c + 1] == ground[c]) rc = c;
        }
        if (rc < 0) return null;
        p.Reactor = ((rc + 0.5) * Field.T, ground[rc] * Field.T);
        p.Start = (way[0].X * Field.T, (Field.Space - 6) * Field.T);
        // Fuel: on floors along the way, spread out.
        var floors = new List<(int C, int R)>();
        for (int c = 2; c < cols - 2; c++)
        for (int r = Field.Space; r < rows - 1; r++)
            if (!p.Rock[c, r] && !p.Rock[c, r - 1] && !p.Rock[c - 1, r] && !p.Rock[c + 1, r] && p.Rock[c, r + 1] && p.Rock[c - 1, r + 1] && p.Rock[c + 1, r + 1] && r > ground[c] + 1)
                floors.Add((c, r));
        for (int tries = 0; tries < 400 && p.Fuel.Count < 2 + level && floors.Count > 0; tries++)
        {
            var f = floors[d.Next(floors.Count)];
            var at = ((f.C + 0.5) * Field.T, (f.R + 1) * Field.T);
            if (Math.Abs(at.Item1 - p.Pod.X) < 6 * Field.T && Math.Abs(at.Item2 - p.Pod.Y) < 4 * Field.T) continue;
            if (p.Fuel.Any(o => Math.Abs(o.X - at.Item1) + Math.Abs(o.Y - at.Item2) < 10 * Field.T)) continue;
            p.Fuel.Add(at);
        }
        // Limpet guns: on rock faces along the way, pointing out.
        var faces = new List<GunDef>();
        for (int c = 1; c < cols - 1; c++)
        for (int r = Field.Space; r < rows - 1; r++)
        {
            if (!p.Rock[c, r]) continue;
            foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, -1), (0, 1) })
            {
                int oc = c + dc, or = r + dr;
                if (oc < 1 || oc >= cols - 1 || or < 0 || or >= rows || p.Rock[oc, or]) continue;
                // A flat face: rock either side of it along the face.
                if (dc != 0 && !(p.Rock[c, r - 1] && p.Rock[c, r + 1])) continue;
                if (dr != 0 && !(p.Rock[c - 1, r] && p.Rock[c + 1, r])) continue;
                faces.Add(new GunDef((c + 0.5 + dc * 0.5) * Field.T, (r + 0.5 + dr * 0.5) * Field.T, dc, dr));
            }
        }
        for (int tries = 0; tries < 2000 && p.Guns.Count < plan.Guns && faces.Count > 0; tries++)
        {
            var gd = faces[d.Next(faces.Count)];
            if (gd.Y < (Field.Space + 5) * Field.T) continue;
            if (Math.Abs(gd.X - p.Pod.X) < 5 * Field.T && Math.Abs(gd.Y - p.Pod.Y) < 4 * Field.T) continue;
            if (p.Guns.Any(o => Math.Abs(o.X - gd.X) + Math.Abs(o.Y - gd.Y) < 9 * Field.T)) continue;
            if (p.Fuel.Any(o => Math.Abs(o.X - gd.X) + Math.Abs(o.Y - gd.Y) < 3 * Field.T)) continue;
            p.Guns.Add(gd);
        }
        if (p.Guns.Count < plan.Guns / 2 + 1) return null;
        // Proof there's room all the way down for the ship with the pod hanging below.
        if (Route(p, p.Cell(p.Start.X, p.Start.Y), p.Cell(p.Pod.X, p.Pod.Y - 2 * Field.T), 1) == null) return null;
        if (Route(p, p.Cell(p.Pod.X, p.Pod.Y - 3 * Field.T), p.Cell(p.Start.X, Field.T), 1, tow: true) == null) return null;
        return p;
    }

    /// <summary>
    /// Whether a square has open space round it (so many squares each way) for the ship; and, towing, down below it
    /// as far as the pod hangs.
    /// </summary>
    public static bool Roomy(PlanetDef p, int c, int r, int room, bool tow = false)
    {
        for (int i = -room; i <= room; i++)
        for (int j = -room; j <= room; j++)
            if (p.RockAt(c + i, r + j)) return false;
        // (The pod swings: room either side of it too.)
        if (tow)
            for (int i = -2; i <= 2; i++)
            for (int j = 1; j <= 3; j++)
                if (p.RockAt(c + i, r + j)) return false;
        return true;
    }

    /// <summary>The way, square by square, between two squares where there's room for the ship.</summary>
    public static List<(int C, int R)>? Route(PlanetDef p, (int C, int R) from, (int C, int R) to, int room, bool tow = false)
    {
        var prev = new Dictionary<(int, int), (int, int)> { [from] = from };
        var q = new Queue<(int, int)>([from]);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            if (cur == to)
            {
                var path = new List<(int C, int R)>();
                for (var x = cur; x != from; x = prev[x]) path.Add(x);
                path.Add(from);
                path.Reverse();
                return path;
            }
            foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var n = (cur.Item1 + dc, cur.Item2 + dr);
                if (n.Item2 < 0 || prev.ContainsKey(n) || !Roomy(p, n.Item1, n.Item2, n.Item2 < Field.Space - 2 ? 0 : room, tow && n.Item2 >= Field.Space - 2)) continue;
                prev[n] = cur;
                q.Enqueue(n);
            }
        }
        return null;
    }
}
