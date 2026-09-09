using System.Drawing;

using Klikety.Config;
using Klikety.Input;

namespace Klikety.Navigation;

/// <summary>
/// A self-contained navigation mode session. Each mode (UniformGrid, Crosshair,
/// LogCrosshair) implements this interface, owning its state machine and rendering.
/// The coordinator delegates key input and manages overlay lifecycle.
/// </summary>
public interface IModeSession {
    /// <summary>Activates the session for the given screen bounds and cursor origin.</summary>
    void Activate(Rectangle screenBounds, Point origin);

    /// <summary>Forwards a key-down event to the session's state machine.</summary>
    void OnKey(VKey key);

    /// <summary>Replays the current visual state without emitting navigation events.</summary>
    void Redraw();

    /// <summary>
    /// Resets internal state without firing surface events.
    /// Called by the coordinator during deactivation or mode switch.
    /// Does NOT clear the canvas — coordinator owns that.
    /// </summary>
    void Deactivate();

    /// <summary>Fired when the session resolves a final action point + mouse action.</summary>
    event Action<Point, MouseAction>? ActionRequested;

    /// <summary>Fired when the user cancels (Escape at top level).</summary>
    event Action? Cancelled;

    /// <summary>Fired when the session needs the cursor moved (cell entry, level exit).</summary>
    event Action<Point>? CursorMoveRequested;
}
