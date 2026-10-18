using Avalonia.Input;
using AVAThrust.Engine;

namespace AVAThrust.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avathrust-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>The first planet, its guns silenced.</summary>
    private static World Planet(int level = 0)
    {
        var w = new World();
        Sim.Start(w, PlanetBook.Build(level), 1);
        w.Guns.Clear();
        return w;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    /// <summary>The ship held still at a place for a moment (for setting up a test).</summary>
    private static void Put(World w, double x, double y)
    {
        var s = w.Ship;
        (s.X, s.Y, s.Vx, s.Vy, s.Angle) = (x, y, 0, 0, 0);
    }

    // ───────────────────────────── flying ─────────────────────────────

    [Fact]
    public void Gravity_pulls_it_down_and_thrust_pushes_it_the_way_it_points()
    {
        var w = Planet();
        double y = w.Ship.Y;
        Run(w, 30);
        Assert.True(w.Ship.Y > y + 2, "falls");
        Assert.Equal(w.Planet.Gravity * 0.5, w.Ship.Vy, 0);
        Put(w, w.Ship.X, 220);
        Run(w, 30, new Controls(Dy: -1));
        Assert.True(w.Ship.Vy < -100, "thrust up");
        w.Ship.Angle = Math.PI / 2;
        double vx = w.Ship.Vx;
        Run(w, 20, new Controls(Dy: -1));
        Assert.True(w.Ship.Vx > vx + 50, "pointing right, thrust right");
    }

    [Fact]
    public void Left_and_right_turn_it()
    {
        var w = Planet();
        Run(w, 30, new Controls(Dx: 1));
        Assert.Equal(Ship.Turn / 2, w.Ship.Angle, 2);
    }

    [Fact]
    public void Thrust_and_the_shield_burn_fuel_and_with_none_left_there_is_no_thrust()
    {
        var w = Planet();
        Put(w, w.Ship.X, 220);
        Run(w, 60, new Controls(Dy: -1, Shield: true));
        Assert.Equal(Ship.Fuel0 - Sim.ThrustBurn - Sim.ShieldBurn, w.Ship.Fuel, 0);
        w.Ship.Fuel = 0;
        double vy = w.Ship.Vy;
        Run(w, 30, new Controls(Dy: -1));
        Assert.True(w.Ship.Vy > vy);
    }

    [Fact]
    public void Touching_rock_is_a_crash()
    {
        var w = Planet();
        var p = w.Planet;
        Put(w, p.Reactor.X + 200, p.Start.Y);
        for (int i = 0; i < 60 * 10 && !w.Ship.Dead; i++) Sim.Step(w, default);
        Assert.True(w.Ship.Dead);
        Assert.Equal("CRASHED", w.Cause);
    }

    // ───────────────────────────── the pod and the fuel ─────────────────────────────

    [Fact]
    public void The_tractor_beam_lifts_the_pod_and_it_swings_on_its_rod()
    {
        var w = Planet();
        var pod = w.Pod;
        Put(w, pod.X, pod.Y - 60);
        Sim.Step(w, new Controls(Shield: true));
        Assert.True(pod.Attached);
        Assert.True(pod.Anchored);
        // Up: the rod pulls the pod off its pedestal.
        for (int i = 0; i < 60 && pod.Anchored; i++) Sim.Step(w, new Controls(Dy: -1));
        Assert.False(pod.Anchored);
        Run(w, 30, new Controls(Dy: -1));
        Assert.Equal(Pod.Rod, Sim.Dist(w.Ship.X, w.Ship.Y, pod.X, pod.Y), 1);
        Assert.True(pod.Y > w.Ship.Y);
    }

    [Fact]
    public void Towing_the_pod_makes_the_ship_heavier()
    {
        var alone = Planet();
        Put(alone, alone.Pod.X, alone.Pod.Y - 160);
        Run(alone, 40, new Controls(Dy: -1));
        var towing = Planet();
        Put(towing, towing.Pod.X, towing.Pod.Y - 160);
        towing.Pod.Attached = true;
        towing.Pod.Anchored = false;
        (towing.Pod.X, towing.Pod.Y) = (towing.Ship.X, towing.Ship.Y + Pod.Rod);
        Run(towing, 40, new Controls(Dy: -1));
        Assert.True(towing.Ship.Vy > alone.Ship.Vy + 30, $"towing {towing.Ship.Vy} alone {alone.Ship.Vy}");
    }

    [Fact]
    public void The_pod_against_rock_is_lost_with_the_ship()
    {
        var w = Planet();
        var pod = w.Pod;
        Put(w, pod.X, pod.Y - 60);
        Sim.Step(w, new Controls(Shield: true));
        for (int i = 0; i < 60 && pod.Anchored; i++) Sim.Step(w, new Controls(Dy: -1));
        // Then straight back down onto the floor.
        for (int i = 0; i < 300 && !w.Ship.Dead; i++) Sim.Step(w, default);
        Assert.True(w.Ship.Dead);
        Assert.True(pod.Lost || w.Cause == "CRASHED");
        Sim.Respawn(w);
        Assert.False(w.Pod.Attached);
        Assert.Equal(w.Planet.Pod, (w.Pod.X, w.Pod.Y));
        Assert.Equal(w.Planet.Start.X, w.Ship.X);
    }

    [Fact]
    public void The_tractor_beam_draws_fuel_from_a_tank_below()
    {
        var w = Planet();
        var t = w.Tanks[0];
        Put(w, t.X, t.Y - 46);
        w.Ship.Fuel = 300;
        // (Held in place over the tank.)
        for (int i = 0; i < 120; i++) { Put(w, t.X, t.Y - 46); w.Ship.Fuel = Math.Max(w.Ship.Fuel, 0); Sim.Step(w, new Controls(Shield: true)); }
        Assert.True(w.Ship.Fuel > 600);
        Assert.True(t.Dead || t.Fuel < 100);
    }

    // ───────────────────────────── guns, shots, the reactor ─────────────────────────────

    [Fact]
    public void A_gun_fires_at_a_ship_it_can_see_and_the_shield_turns_shots_away()
    {
        var w = new World();
        Sim.Start(w, PlanetBook.Build(2), 1);
        var g = w.Guns[0];
        Put(w, g.Def.X + g.Def.Nx * 140, g.Def.Y + g.Def.Ny * 140);
        g.Cool = 0;
        bool fired = false;
        for (int i = 0; i < 300 && !w.Ship.Dead; i++)
        {
            Put(w, g.Def.X + g.Def.Nx * 140, g.Def.Y + g.Def.Ny * 140);
            w.Ship.Fuel = Ship.Fuel0;
            Sim.Step(w, new Controls(Shield: true));
            fired |= w.Shots.Any(s => !s.Mine);
        }
        Assert.True(fired);
        Assert.False(w.Ship.Dead);
        for (int i = 0; i < 600 && !w.Ship.Dead; i++) { Put(w, g.Def.X + g.Def.Nx * 140, g.Def.Y + g.Def.Ny * 140); Sim.Step(w, default); }
        Assert.Equal("SHOT DOWN", w.Cause);
    }

    [Fact]
    public void A_shot_takes_out_a_gun()
    {
        var w = new World();
        Sim.Start(w, PlanetBook.Build(2), 1);
        var g = w.Guns[0];
        w.Shots.Add(new Shot { X = g.Def.X + g.Def.Nx * 40, Y = g.Def.Y + g.Def.Ny * 40, Vx = -g.Def.Nx * Sim.ShotSpeed, Vy = -g.Def.Ny * Sim.ShotSpeed, Life = 1, Mine = true });
        Run(w, 10);
        Assert.True(g.Dead);
        Assert.Equal(Sim.GunPoints, w.Score);
    }

    [Fact]
    public void Hits_on_the_reactor_silence_the_guns_and_enough_set_off_the_countdown()
    {
        var w = Planet();
        var r = w.Planet.Reactor;
        int shots = 0;
        for (; shots < 10 && !w.ReactorCritical; shots++)
        {
            w.Shots.Add(new Shot { X = r.X, Y = r.Y - 120, Vy = Sim.ShotSpeed, Life = 1, Mine = true });
            Run(w, 20);
            if (shots == 0) Assert.True(w.Quiet > 0);
        }
        Assert.True(w.ReactorCritical);
        Assert.InRange(shots, (int)Sim.ReactorHits, (int)Sim.ReactorHits + 2);
        Assert.InRange(w.Countdown, Sim.CountdownTime - 1, Sim.CountdownTime);
        Put(w, w.Planet.Start.X, w.Planet.Start.Y);
        for (int i = 0; i < 60 * 11; i++) { Put(w, w.Planet.Start.X, w.Planet.Start.Y); Sim.Step(w, default); }
        Assert.Equal("THE PLANET BLEW UP", w.Cause);
    }

    // ───────────────────────────── the game ─────────────────────────────

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    [Fact]
    public void Out_into_space_with_the_pod_scores_the_bonus_and_on_to_the_next_planet()
    {
        var g = Engine();
        g.Go();
        var w = g.World;
        w.Pod.Attached = true;
        w.Pod.Anchored = false;
        Put(w, w.Planet.Start.X, 4);
        w.Ship.Vy = -100;
        (w.Pod.X, w.Pod.Y) = (w.Ship.X, w.Ship.Y + Pod.Rod);
        (w.Pod.Vx, w.Pod.Vy) = (0, -100);
        g.Run(10);
        Assert.Equal(Phase.Escaped, g.Phase);
        Assert.Equal(GameEngine.PodBonus, g.Score);
        g.Run((int)(60 * GameEngine.EscapedSeconds) + 4);
        Assert.Equal(1, g.Level);
        Assert.Equal(1, g.Cleared);
    }

    [Fact]
    public void Out_without_the_pod_is_mission_incomplete_and_round_again()
    {
        var g = Engine();
        g.Go();
        var w = g.World;
        Put(w, w.Planet.Start.X, 4);
        w.Ship.Vy = -100;
        g.Run(3);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.StartsWith("MISSION INCOMPLETE", g.Message);
        Assert.Equal(GameEngine.Lives0, g.Lives);
        Assert.Equal(0, g.Level);
    }

    [Fact]
    public void A_ship_lost_and_the_next_comes_in_over_the_planet_and_the_last_is_game_over()
    {
        var g = Engine();
        g.Go();
        Sim.Die(g.World, "CRASHED");
        g.Run((int)(60 * GameEngine.DyingSeconds) + 3);
        Assert.Equal(GameEngine.Lives0 - 1, g.Lives);
        Assert.False(g.World.Ship.Dead);
        Assert.Equal(g.World.Planet.Start.X, g.World.Ship.X);
        Assert.InRange(g.World.Ship.Y, g.World.Planet.Start.Y, g.World.Planet.Start.Y + 4);
        g.SetLives(1);
        Sim.Die(g.World, "CRASHED");
        g.Run((int)(60 * GameEngine.DyingSeconds) + 3);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void The_title_gives_way_to_the_demonstration_and_enter_starts_a_game()
    {
        var g = Engine();
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 6);
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(GameEngine.Lives0, g.Lives);
    }

    [Fact]
    public void Paused_nothing_moves()
    {
        var g = Engine();
        g.Go();
        g.Run(10);
        g.Pause();
        double y = g.World.Ship.Y;
        g.Run(60);
        Assert.Equal(y, g.World.Ship.Y);
        g.Resume();
        g.Run(10);
        Assert.NotEqual(y, g.World.Ship.Y);
    }

    [Fact]
    public void The_keys_turn_thrust_fire_and_shield()
    {
        using var input = new InputHandler();
        foreach (var (key, check) in new (Key, Func<Controls, bool>)[]
                 {
                     (Key.Left, c => c.Dx == -1), (Key.Right, c => c.Dx == 1), (Key.Up, c => c.Dy == -1),
                     (Key.Space, c => c.Fire), (Key.X, c => c.Shield), (Key.LeftShift, c => c.Shield),
                 })
        {
            input.OnKeyDown(key);
            Assert.True(check(input.Player), key.ToString());
            input.OnKeyUp(key);
        }
        var w = Planet();
        input.OnKeyDown(Key.Up);
        Run(w, 60, input.Player);
        Assert.True(w.Ship.Vy < -50);
    }
}
