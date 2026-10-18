namespace AVAThrust.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>The planet named; a moment before the ship comes in over it.</summary>
    Start,
    Play,
    /// <summary>Lost: a moment, then a fresh ship over the planet (the pod back on its pedestal), or the end.</summary>
    Dying,
    /// <summary>Out into space with the pod: the bonus, then the next planet.</summary>
    Escaped,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Fire, GunFire, GunDown, Boom, Explode, Tractor, Fuel, ReactorHit, Critical, Deflect, Escape, ExtraLife, LowFuel, Incomplete,
    SayCritical,
    Music, MusicStop, TitleTune, GameOver, Victory, Cleared,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 70;
    public const double HighScoreSeconds = 9;
    public const double StartSeconds = 2.4, DyingSeconds = 2.2, EscapedSeconds = 4.5;
    public const int Lives0 = 3, ExtraEvery = 10000, PodBonus = 4000, LevelBonus = 1000, ReactorBonus = 2000;

    private double _accumulator, _idle;
    private bool _startPrev, _tunePending = true, _music, _lowWarned, _criticalSaid;
    private int _nextExtra, _seed;

    public GameEngine(HighScoreTable? table = null, int seed = 1)
    {
        HighScores = table ?? HighScoreTable.Load();
        _seed = seed;
        NewGame();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    /// <summary>Lives left, the one being played among them.</summary>
    public int Lives { get; private set; }
    public int Score => World.Score;
    public int Level { get; private set; }
    /// <summary>Planets left behind this game.</summary>
    public int Cleared { get; private set; }
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0 && (MessageTime -= dt) <= 0) Message = null;
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || c.Fire && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Music(false); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > 4)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Cleared);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame()
    {
        World = new World();
        Lives = Lives0;
        Cleared = 0;
        _nextExtra = ExtraEvery;
        Enter(0);
    }

    private void Enter(int level)
    {
        Level = level;
        int score = World.Score;
        Sim.Start(World, PlanetBook.Build(level), _seed * 31 + level);
        World.Score = score;
        _lowWarned = false;
        SetPhase(Phase.Start);
        Say(World.Planet.Name, StartSeconds);
        Pilot.Reset();
    }

    private void Say(string text, double seconds)
    {
        Message = text;
        MessageTime = seconds;
    }

    public void StartGame()
    {
        State = GameState.Playing;
        NewGame();
        StateTime = 0;
        NewEntryIndex = -1;
    }

    private void StartAttract()
    {
        State = GameState.Attract;
        _seed++;
        NewGame();
        StateTime = 0;
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Music(false);
        Message = null;
        NewGame();
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Play) Music(true);
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var w = World;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > StartSeconds) { SetPhase(Phase.Play); Music(true); }
                break;
            case Phase.Play:
            {
                int before = w.Score;
                Sim.Step(w, c);
                if (w.Score != before) ExtraLives();
                var ship = w.Ship;
                if (ship.Fuel < 200 && !_lowWarned && !ship.Dead) { _lowWarned = true; Play(Sfx.LowFuel); Say("FUEL LOW", 2); }
                if (ship.Fuel > 300) _lowWarned = false;
                if (_criticalSaid != w.ReactorCritical) { _criticalSaid = w.ReactorCritical; if (w.ReactorCritical) Play(Sfx.SayCritical); }
                if (ship.Dead) { SetPhase(Phase.Dying); Music(false); Say(w.Cause, DyingSeconds); }
                else if (w.Escaped && !w.Pod.Attached)
                {
                    // Off without the pod: no good. Round again.
                    Play(Sfx.Incomplete);
                    Say("MISSION INCOMPLETE - BRING THE POD", 2.5);
                    Sim.Respawn(w);
                    Pilot.Reset();
                }
                else if (w.Escaped)
                {
                    SetPhase(Phase.Escaped);
                    Music(false);
                    Play(Sfx.Cleared);
                    int bonus = PodBonus + Level * LevelBonus + (w.ReactorCritical ? ReactorBonus : 0);
                    w.Score += bonus;
                    Say(w.ReactorCritical ? $"THE PLANET IS DESTROYED!  BONUS {bonus}" : $"MISSION COMPLETE!  BONUS {bonus}", EscapedSeconds);
                    ExtraLives();
                }
                break;
            }
            case Phase.Dying:
                Sim.Step(w, default);
                if (PhaseTime > DyingSeconds)
                {
                    Lives--;
                    if (Lives <= 0) { End(); return; }
                    // The planet blown up: a fresh one, the same again.
                    if (w.ReactorCritical) { int score = w.Score; double fuel = w.Ship.Fuel; Sim.Start(w, w.Planet, _seed * 31 + Level); w.Score = score; w.Ship.Fuel = fuel; }
                    Sim.Respawn(w);
                    Pilot.Reset();
                    SetPhase(Phase.Play);
                    Music(true);
                }
                break;
            case Phase.Escaped:
                if (PhaseTime > EscapedSeconds)
                {
                    Cleared++;
                    if ((Level + 1) % PlanetBook.Count == 0) Play(Sfx.Victory);
                    Enter(Level + 1);
                }
                break;
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private void End()
    {
        Play(Sfx.GameOver);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    private void ExtraLives()
    {
        while (World.Score >= _nextExtra)
        {
            _nextExtra += ExtraEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight down onto a planet, past its name.</summary>
    public void Go(int level = 0)
    {
        State = GameState.Playing;
        NewGame();
        if (level != 0) Enter(level);
        SetPhase(Phase.Play);
        Message = null;
    }

    public void SetLives(int n) => Lives = n;
}
