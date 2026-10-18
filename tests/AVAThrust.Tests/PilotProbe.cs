using AVAThrust.Engine;

namespace AVAThrust.Tests;

/// <summary>How the computer pilot gets on on each planet (set THRUST_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_pilot_does()
    {
        if (Environment.GetEnvironmentVariable("THRUST_PROBE") is not { } mode) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int level = 0; level < PlanetBook.Count; level++)
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "thrust-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.Go(level);
            g.SetLives(30);
            var deaths = new Dictionary<string, int>();
            var doing = new Dictionary<string, int>();
            var last = g.Phase;
            int t = 0, incomplete = 0;
            string? msg = null;
            for (; t < 60 * 400 && g.State == GameState.Playing && g.Cleared == 0; t++)
            {
                var c = mode == "lazy" ? new Controls(t / 90 % 3 - 1, t % 7 < 3 ? -1 : 0, t % 20 == 0) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[g.World.Cause] = deaths.GetValueOrDefault(g.World.Cause) + 1;
                if (g.Message != msg) { msg = g.Message; if (msg?.StartsWith("MISSION INCOMPLETE") == true) incomplete++; }
                last = g.Phase;
                doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
            }
            var w = g.World;
            output.WriteLine($"{w.Planet.Name}: {(g.Cleared > 0 ? "ESCAPED" : "no")} after {t / 60}s, fuel {w.Ship.Fuel:0}, {deaths.Values.Sum()} lost ({string.Join(", ", deaths.Select(d => $"{d.Key} x{d.Value}"))}), incomplete {incomplete}, guns down {w.Guns.Count(x => x.Dead)}/{w.Guns.Count}; {string.Join(", ", doing.OrderByDescending(d => d.Value).Take(4).Select(d => $"{d.Key} {d.Value / 60}s"))}");
        }
        output.WriteLine($"total {sw.ElapsedMilliseconds} ms");
    }
}
