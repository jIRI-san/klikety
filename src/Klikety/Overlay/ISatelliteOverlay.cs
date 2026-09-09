using System.Drawing;

namespace Klikety.Overlay;

/// <summary>
/// Click-through number overlay on a non-navigation display. Not <see cref="Services.IOverlayWindow"/>.
/// </summary>
public interface ISatelliteOverlay : IDisposable {
    void Show(Rectangle physicalBounds, int? number);
    void Hide();
}
