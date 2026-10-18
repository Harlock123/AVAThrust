using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAThrust.Engine;
using AVAThrust.Views;
using Controls = AVAThrust.Engine.Controls;

namespace AVAThrust.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set THRUST_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avathrust-ui-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("THRUST_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAThrust-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 200)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("JS ", 41200, 6);
        g.HighScores.Insert("AVA", 12800, 2);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 300 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    private static void Play(int level, string name, Func<GameEngine, bool> moment, int seconds = 200)
    {
        var (w, c, g) = Open();
        g.Go(level);
        g.SetLives(20);
        g.Pilot.Reset();
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void Over_the_planet() => Play(0, "surface", g => g.World.Clock > 2.2);

    [AvaloniaFact]
    public void Down_the_shaft() => Play(1, "shaft", g => g.World.Ship.Y > 9 * Field.T * 3 && !g.World.Pod.Attached && g.World.Ship.Thrusting);

    [AvaloniaFact]
    public void Fuel() => Play(4, "fuel", g => g.World.Draining >= 0 && g.World.Clock > 1, 300);

    [AvaloniaFact]
    public void Lifting_the_pod() => Play(2, "pod", g => g.World.Pod.Attached && !g.World.Pod.Anchored && g.World.Ship.Y < g.World.Planet.Pod.Y - 150);

    [AvaloniaFact]
    public void A_limpet_gun() => Play(3, "gun", g => g.World.Shots.Count(s => !s.Mine) >= 1 && g.World.Shots.Any(s => !s.Mine && Sim.Dist(s.X, s.Y, g.World.Ship.X, g.World.Ship.Y) < 260), 300);

    [AvaloniaFact]
    public void Towing_out() => Play(5, "towing", g => g.World.Pod.Attached && !g.World.Pod.Anchored && g.World.Ship.Y < (Field.Space + 8) * Field.T, 400);

    [AvaloniaFact]
    public void Reactor_critical()
    {
        var (w, c, g) = Open();
        g.Go(0);
        var world = g.World;
        var r = world.Planet.Reactor;
        (world.Ship.X, world.Ship.Y) = (r.X - 140, r.Y - 110);
        world.ReactorHeat = Sim.ReactorHits - 0.5;
        world.Shots.Add(new Shot { X = r.X, Y = r.Y - 100, Vy = Sim.ShotSpeed, Life = 1, Mine = true });
        for (int i = 0; i < 60; i++) { (world.Ship.X, world.Ship.Y, world.Ship.Vx, world.Ship.Vy) = (r.X - 140, r.Y - 110, 0, 0); g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Assert.True(world.ReactorCritical);
        Shot(w, c, "critical");
    }

    [AvaloniaFact]
    public void Game_over()
    {
        var (w, c, g) = Open();
        g.Go(0);
        g.SetLives(1);
        g.World.Score = 6750;
        Sim.Die(g.World, "CRASHED");
        for (int i = 0; i < 600 && !(g.State == GameState.GameOver && g.StateTime > 1); i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Shot(w, c, "game-over");
    }
}
