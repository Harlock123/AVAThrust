# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A gravity-and-inertia cave flyer in the style of Jeremy Smith's 1986 game Thrust for Superior Software, in C# on
.NET 10 and Avalonia 12, drawn through Avalonia's `DrawingContext`. One self-contained executable per platform (Windows, macOS and
Linux, x64 and ARM). Everything in it is original: the planets are generated and drawn in code, and the sounds,
tunes and voice are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAThrust.slnx                       # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAThrust.slnx                        # every test runs headless: no window opens
dotnet run --project src/AVAThrust                # play it; add -- --no-audio for silence
THRUST_SHOTS=$PWD/Images dotnet test tests/AVAThrust.Tests --filter ScreenshotTests   # refresh the README's pictures
THRUST_PROBE=1 dotnet test tests/AVAThrust.Tests --filter PilotProbe --logger "console;verbosity=detailed"
python3 tools/make_sounds.py                      # rebuild src/AVAThrust/Sounds (standard-library Python only)
```

The probe reports how the computer pilot gets on, planet by planet, for tuning; `THRUST_PROBE=lazy` runs one that
turns and thrusts at random. Without the variable it does nothing. `tools/speech.py` is the small formant speech
synthesiser that `make_sounds.py` imports for the voice.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAThrust/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, attract, play, pause, game over, initials, high scores) and the
    fixed-step loop, and starts each planet with `Sim.Start`.
  - `Sim.cs` steps a planet (`Sim.Step`): the ship turning and thrusting against gravity, the tractor beam, the
    pod swinging on its rod, the limpet guns, the reactor and the way out.
  - `World.cs` holds everything that moves or changes, kept together so it can be copied whole, plus the copyable
    `Dice` random source.
  - `Planet.cs` has `Controls`, the screen measures (`Field`), and `PlanetBook`, which generates each planet's
    caverns, guns, fuel, pod and reactor from a seed and keeps only planets that pass its checks.
  - `Pilot.cs` is the computer pilot, used by the attract mode and the tests. It finds its way square by square.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepad) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAThrust/Views/`: `GameCanvas` (split over partial files) draws a fixed 1024x768 picture scaled to the
  window, following the ship, and runs the 60 Hz timer.
- `src/AVAThrust/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVAThrust/Sounds/`: the generated WAV files.
- `tests/AVAThrust.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `PlanetTests` checks every planet has room
  down to the pod and back, and that a planet comes out the same every time.

## Rules that keep it working

- **Fixed steps.** The simulation advances in fixed 1/60 s steps (`Field.Step`), independent of the frame rate,
  so tests can play whole planets deterministically from a seed. Draw random numbers from the world's seeded
  `Dice` (`w.Dice`), never from `Random.Shared`. Planets are built from fixed seeds, so the same level always
  gives the same planet.
- **Keep `World` copyable.** Any new state that changes during play belongs in `World` and must be copied with it.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the world and draws it.
- **Input goes through `InputHandler`.** Up/down on keys and stick must produce the same `dy` sign the engine
  expects (negative is up, which fires the thruster). Test new controls through `InputHandler`, not by building
  `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAThrust`). A `GameEngine`
  built without a table loads that file, so tests load the table from a temporary folder and set `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If you change how planets are built, `PlanetTests` must still pass for every level.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release. After a release it asks the portfolio site to rebuild, if the `SITE_REFRESH_TOKEN` secret is set.
