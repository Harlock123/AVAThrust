using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAThrust.Engine;

namespace AVAThrust.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAThrust/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused;

    private const double ViewTop = Field.Panel, ViewH = Field.Height - Field.Panel;
    private double _camX, _camY;

    private Point Sc(double x, double y) => new(x - _camX + Field.Width / 2, y - _camY + ViewTop + ViewH / 2);

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Height));
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            case GameState.GameOver: DrawGameOver(dc); break;
            default:
                DrawPlanet(dc, g.World);
                DrawHud(dc, g.World);
                if (g.Message is { } msg)
                {
                    double wdt = msg.Length * 18 + 40;
                    dc.FillRectangle(Brush(0xC0000000), new Rect(512 - wdt / 2, ViewTop + 120, wdt, 56), 6);
                    Text(dc, msg, new Point(512, ViewTop + 148), 40, g.Phase == Phase.Dying ? Red : Yellow, center: true);
                }
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, Field.Height - 18), 22, Yellow, center: true);
    }

    // ───────────────────────────── the planet ─────────────────────────────

    private void DrawPlanet(DrawingContext dc, World w)
    {
        var p = w.Planet;
        var s = w.Ship;
        // The camera: on the ship (between the ship and the pod when towing), kept inside the planet's sides.
        double fx = s.X, fy = s.Y;
        if (w.Pod.Attached) { fx = (s.X + w.Pod.X) / 2; fy = (s.Y + w.Pod.Y) / 2; }
        _camX = Math.Clamp(fx, Field.Width / 2, Field.Cols * Field.T - Field.Width / 2);
        _camY = Math.Clamp(fy, -ViewH / 2 + 120, p.Height - ViewH / 2 + 40);
        using var clip = dc.PushClip(new Rect(0, ViewTop, Field.Width, ViewH));
        // Stars over the surface.
        for (int k = 0; k < 90; k++)
        {
            double x = Hash(k, 1, 9) % (uint)(Field.Cols * Field.T), y = Hash(k, 2, 9) % (uint)(Field.Space * Field.T) - 200;
            var at = Sc(x, y);
            if (at.Y > ViewTop && at.Y < Field.Height) dc.FillEllipse(at.X, at.Y, 1 + Hash(k, 3, 9) % 2 * 0.6, 0xFFB0B0D0);
        }
        DrawRock(dc, p);
        DrawReactor(dc, w);
        foreach (var t in w.Tanks) if (!t.Dead) DrawTank(dc, t);
        foreach (var g in w.Guns) if (!g.Dead) DrawGun(dc, g, p.RockColour);
        // The pod: on its pedestal, or on the rod.
        if (!w.Pod.Lost)
        {
            var pc = Sc(w.Pod.X, w.Pod.Y);
            if (w.Pod.Attached && !s.Dead) dc.DrawLine(Pen(0xFFE0E0E0, 2), Sc(s.X, s.Y), pc);
            dc.DrawEllipse(Brush(0xFF203040), Pen(0xFFFFE060, 2.5), pc, Pod.Radius, Pod.Radius);
            dc.FillEllipse(pc.X, pc.Y, 4, (int)(_clock * 3) % 2 == 0 ? 0xFFFFE060 : 0xFF806020);
        }
        // Tractor beam.
        if (s.Shielding && !s.Dead)
        {
            if (w.Draining >= 0)
            {
                var t = w.Tanks[w.Draining];
                for (int k = 0; k < 3; k++)
                    dc.DrawLine(Pen(0x9060FF60, 2), Sc(s.X - 6 + k * 6, s.Y + 8), Sc(t.X - 8 + k * 8, t.Y - 22));
            }
        }
        if (!s.Dead && !w.Escaped) DrawShip(dc, Sc(s.X, s.Y), s.Angle, s.Thrusting, s.Shielding, 1);
        foreach (var b in w.Shots)
        {
            var at = Sc(b.X, b.Y);
            dc.FillEllipse(at.X, at.Y, b.Mine ? 2.5 : 3, b.Mine ? 0xFFFFFFFF : 0xFFFF6040);
        }
        foreach (var b in w.Bursts)
        {
            var at = Sc(b.X, b.Y);
            double r = (b.Big ? 46 : 14) * (0.3 + b.T);
            for (int k = 0; k < (b.Big ? 14 : 6); k++)
            {
                double a = k * Math.PI * 2 / (b.Big ? 14 : 6) + Hash(k, (int)b.X, 4) % 100 * 0.01;
                dc.FillEllipse(at.X + Math.Cos(a) * r, at.Y + Math.Sin(a) * r, b.Big ? 2.5 : 1.5, Alpha(k % 2 == 0 ? 0xFFFFE060 : 0xFFFF6030, 1 - b.T / 0.9));
            }
        }
    }

    /// <summary>The rock, square by square: the solid part of each, cut at forty-five degrees where it turns, edged in the planet's colour.</summary>
    private void DrawRock(DrawingContext dc, PlanetDef p)
    {
        const double T = Field.T;
        int c0 = Math.Max(0, (int)((_camX - Field.Width / 2) / T) - 1), c1 = Math.Min(Field.Cols, (int)((_camX + Field.Width / 2) / T) + 2);
        int r0 = Math.Max(0, (int)((_camY - ViewH / 2) / T) - 1), r1 = Math.Min(p.Rows, (int)((_camY + ViewH / 2) / T) + 2);
        uint fill = Shade(p.RockColour, 0.22), edge = p.RockColour;
        var corners = new (double X, double Y, double V)[4];
        var pts = new List<(Point P, bool Cut)>(8);
        for (int i = c0; i < c1; i++)
        for (int j = r0; j < r1; j++)
        {
            corners[0] = (i, j, p.Corner(i, j));
            corners[1] = (i + 1, j, p.Corner(i + 1, j));
            corners[2] = (i + 1, j + 1, p.Corner(i + 1, j + 1));
            corners[3] = (i, j + 1, p.Corner(i, j + 1));
            int solid = corners.Count(k => k.V > 0.5);
            if (solid == 0) continue;
            if (solid == 4) { var a0 = Sc(i * T, j * T); dc.FillRectangle(Brush(fill), new Rect(a0.X, a0.Y, T + 0.6, T + 0.6)); continue; }
            pts.Clear();
            for (int k = 0; k < 4; k++)
            {
                var a = corners[k];
                var b = corners[(k + 1) % 4];
                if (a.V > 0.5) pts.Add((Sc(a.X * T, a.Y * T), false));
                if (a.V > 0.5 != b.V > 0.5)
                {
                    double t = Math.Clamp((0.5 - a.V) / (b.V - a.V), 0, 1);
                    pts.Add((Sc((a.X + (b.X - a.X) * t) * T, (a.Y + (b.Y - a.Y) * t) * T), true));
                }
            }
            if (pts.Count >= 3) Poly(dc, fill, null, 0, pts.Select(q => q.P).ToArray());
            for (int k = 0; k < pts.Count; k++)
                if (pts[k].Cut && pts[(k + 1) % pts.Count].Cut) dc.DrawLine(Pen(edge, 2), pts[k].P, pts[(k + 1) % pts.Count].P);
        }
    }

    private static uint Shade(uint c, double k)
    {
        uint Ch(int sh) => (uint)Math.Clamp(((c >> sh) & 0xFF) * k, 0, 255);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    private void DrawShip(DrawingContext dc, Point at, double angle, bool thrust, bool shield, double k)
    {
        double s = Math.Sin(angle), c = Math.Cos(angle);
        Point P(double px, double py) => new(at.X + (px * c - py * s) * k, at.Y + (px * s + py * c) * k);
        if (thrust)
        {
            double f = 10 + Hash((int)(_clock * 30), 1, 2) % 8;
            Poly(dc, 0xFFFF9030, null, 0, P(-5, 10), P(5, 10), P(0, 10 + f));
        }
        Poly(dc, 0xFF102030, 0xFF80E0FF, 2, P(0, -13), P(10, 9), P(4, 6), P(-4, 6), P(-10, 9));
        dc.FillEllipse(P(0, -2).X, P(0, -2).Y, 2.5 * k, 0xFFFFFFFF);
        if (shield) dc.DrawEllipse(Brush(0x2040FF60), Pen(0xC060FF80, 2), at, 19 * k, 19 * k);
    }

    private void DrawGun(DrawingContext dc, Gun g, uint colour)
    {
        var d = g.Def;
        var at = Sc(d.X, d.Y);
        double nx = d.Nx, ny = d.Ny, tx = -ny, ty = nx;
        // A half-dome on the rock, its barrel out of the top.
        var pts = new Point[9];
        for (int k = 0; k <= 8; k++)
        {
            double a = Math.PI * k / 8;
            pts[k] = new Point(at.X + (tx * Math.Cos(a) + nx * Math.Sin(a)) * 11, at.Y + (ty * Math.Cos(a) + ny * Math.Sin(a)) * 11);
        }
        Poly(dc, 0xFF402020, 0xFFFF6040, 2, pts);
        dc.DrawLine(Pen(0xFFFF6040, 3), new Point(at.X + nx * 9, at.Y + ny * 9), new Point(at.X + nx * 18, at.Y + ny * 18));
    }

    private void DrawTank(DrawingContext dc, Tank t)
    {
        var at = Sc(t.X, t.Y);
        dc.DrawRectangle(Brush(0xFF103018), Pen(0xFF60FF80, 2), new Rect(at.X - 12, at.Y - 22, 24, 22));
        dc.FillRectangle(Brush(0xFF60FF80), new Rect(at.X - 9, at.Y - 4 - 14 * t.Fuel / 400, 5, 14 * t.Fuel / 400));
        Text(dc, "F", new Point(at.X + 4, at.Y - 11), 18, 0xFF60FF80, center: true, shadow: false);
    }

    private void DrawReactor(DrawingContext dc, World w)
    {
        var p = w.Planet;
        var at = Sc(p.Reactor.X, p.Reactor.Y);
        bool hot = w.ReactorCritical && (int)(_clock * 6) % 2 == 0;
        uint col = hot ? 0xFFFF4040 : w.Quiet > 0 ? 0xFF8080A0 : 0xFFE0E0FF;
        dc.DrawRectangle(Brush(0xFF181828), Pen(col, 2), new Rect(at.X - 26, at.Y - 22, 52, 22));
        Poly(dc, 0xFF181828, col, 2, new Point(at.X - 16, at.Y - 22), new Point(at.X - 10, at.Y - 34), new Point(at.X + 10, at.Y - 34), new Point(at.X + 16, at.Y - 22));
        dc.DrawRectangle(Brush(0xFF181828), Pen(col, 2), new Rect(at.X + 12, at.Y - 48, 8, 26));
        // Smoke from the chimney: more when it's been hit.
        for (int k = 0; k < 3; k++)
        {
            double t = (_clock * (0.6 + w.ReactorHeat * 0.3) + k / 3.0) % 1;
            dc.FillEllipse(at.X + 16 + t * 14, at.Y - 52 - t * 40, 3 + t * 6, Alpha(0xFF909090, 0.6 * (1 - t)));
        }
    }

    // ───────────────────────────── the strip along the top ─────────────────────────────

    private void DrawHud(DrawingContext dc, World w)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF080810), new Rect(0, 0, Field.Width, Field.Panel));
        dc.FillRectangle(Brush(w.Planet.RockColour), new Rect(0, Field.Panel - 3, Field.Width, 3));
        Text(dc, "FUEL", new Point(24, 20), 24, 0xFF60FF80);
        Text(dc, $"{(int)w.Ship.Fuel:0000}", new Point(24, 44), 30, w.Ship.Fuel < 200 && (int)(_clock * 3) % 2 == 0 ? Red : 0xFF60FF80);
        Text(dc, "LIVES", new Point(170, 20), 24, Cyan);
        for (int i = 0; i < Math.Min(8, g.Lives - 1); i++) DrawShip(dc, new Point(180 + i * 26, 46), 0, false, false, 0.75);
        Text(dc, w.Planet.Name, new Point(Field.Width / 2, 20), 26, w.Planet.RockColour, center: true);
        if (w.ReactorCritical)
            Text(dc, $"REACTOR CRITICAL  {Math.Max(0, w.Countdown):0}", new Point(Field.Width / 2, 46), 28, (int)(_clock * 4) % 2 == 0 ? Red : Yellow, center: true);
        else if (w.Quiet > 0)
            Text(dc, "REACTOR HIT - GUNS SILENT", new Point(Field.Width / 2, 46), 22, 0xFFB0B0E0, center: true);
        else
            Text(dc, w.Pod.Attached ? "POD IN TOW - OUT INTO SPACE" : "FETCH THE POD", new Point(Field.Width / 2, 46), 22, 0xFFB0B0E0, center: true);
        Text(dc, "SCORE", new Point(Field.Width - 24, 20), 24, Yellow, right: true);
        Text(dc, $"{g.Score:000000}", new Point(Field.Width - 24, 44), 30, Yellow, right: true);
    }

    // ───────────────────────────── title, game over and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        for (int k = 0; k < 120; k++) dc.FillEllipse(Hash(k, 1, 7) % 1024, Hash(k, 2, 7) % 768, 1 + Hash(k, 3, 7) % 2 * 0.6, 0xFF8080A0);
        Text(dc, "AVA THRUST", new Point(cx, 100), 120, 0xFF80E0FF, center: true);
        Text(dc, "AFTER JEREMY SMITH'S 1986 GAME FOR SUPERIOR SOFTWARE", new Point(cx, 170), 22, Dim, center: true);
        // A ship towing its pod, swinging.
        double swing = Math.Sin(_clock * 1.6) * 0.5;
        var ship = new Point(cx, 280);
        var pod = new Point(cx + Math.Sin(swing) * 90, 280 + Math.Cos(swing) * 90);
        dc.DrawLine(Pen(0xFFE0E0E0, 2), ship, pod);
        dc.DrawEllipse(Brush(0xFF203040), Pen(0xFFFFE060, 3), pod, 16, 16);
        DrawShip(dc, ship, -swing * 0.4, true, false, 1.6);
        // A strip of planet below.
        uint rock = 0xFF40C040;
        Poly(dc, Shade(rock, 0.22), rock, 2, new Point(0, 470), new Point(180, 470), new Point(220, 430), new Point(380, 430), new Point(420, 470), new Point(1024, 470), new Point(1024, 520), new Point(0, 520));
        Text(dc, "STEAL THE POWER PODS FROM THE PLANETS. FLY DOWN THROUGH THE CAVERNS PAST THE LIMPET GUNS,", new Point(cx, 552), 20, White, center: true);
        Text(dc, "LIFT THE POD WITH YOUR TRACTOR BEAM, AND TOW IT BACK OUT INTO SPACE. MIND THE FUEL, AND THE SWING.", new Point(cx, 578), 20, White, center: true);
        Text(dc, "SHOOT THE REACTOR ENOUGH AND IT GOES CRITICAL: GET OUT IN TIME FOR A BONUS.", new Point(cx, 604), 20, White, center: true);
        Text(dc, "PRESS ENTER TO START", new Point(cx, 656), 40, (int)(_clock * 2) % 2 == 0 ? Yellow : Orange, center: true);
        Text(dc, "LEFT/RIGHT: TURN   UP: THRUST   SPACE: FIRE   SHIFT OR X: SHIELD AND TRACTOR BEAM   P: PAUSE", new Point(cx, 706), 19, Dim, center: true);
        Text(dc, $"HIGH SCORE {Engine.HighScores.TopScore:000000}", new Point(cx, 740), 22, Cyan, center: true);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        Text(dc, "GAME OVER", new Point(cx, 260), 110, Red, center: true);
        Text(dc, $"PODS RECOVERED {Engine.Cleared}", new Point(cx, 350), 34, White, center: true);
        Text(dc, $"SCORE {Engine.Score}", new Point(cx, 430), 44, Yellow, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "THE BEST PILOTS", new Point(cx, 120), 64, 0xFFFFE040, center: true);
        Text(dc, "RANK NAME    SCORE     PODS", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,5}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
