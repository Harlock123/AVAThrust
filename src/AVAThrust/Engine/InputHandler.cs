using Avalonia.Input;

namespace AVAThrust.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: left and right, up and down (up the screen is -1), and three buttons.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update()
    {
        _pad.Update();
    }

    /// <summary>
    /// The arrows (or W, A, S and D): left and right turn the ship, up fires the thruster; Space, Z or Ctrl to fire;
    /// X or Shift for the shield (and tractor beam). On a gamepad: the stick, A to fire, B or the left trigger to
    /// shield.
    /// </summary>
    public Controls Player
    {
        get
        {
            var pad = _pad;
            int dx = (Any(Key.Right, Key.D, Key.NumPad6) ? 1 : 0) - (Any(Key.Left, Key.A, Key.NumPad4) ? 1 : 0);
            if (dx == 0) dx = pad.DPadLeft || pad.LeftStickX < -0.4f ? -1 : pad.DPadRight || pad.LeftStickX > 0.4f ? 1 : 0;
            // Up (the thruster) is -1.
            int dy = (Any(Key.Down, Key.S, Key.NumPad2) ? 1 : 0) - (Any(Key.Up, Key.W, Key.NumPad8) ? 1 : 0);
            if (dy == 0) dy = pad.DPadUp || pad.LeftStickY < -0.4f ? -1 : pad.DPadDown || pad.LeftStickY > 0.4f ? 1 : 0;
            bool fire = Any(Key.Space, Key.Z, Key.LeftCtrl, Key.RightCtrl) || pad.ButtonA || pad.RightTrigger > 0.5f;
            bool shield = Any(Key.X, Key.LeftShift, Key.RightShift) || pad.ButtonB || pad.ButtonX || pad.LeftTrigger > 0.5f;
            return new Controls(dx, dy, fire, shield);
        }
    }

    /// <summary>Enter or 1.</summary>
    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
