using System.Drawing;
using System.IO;

using Klikety.Services;

namespace Klikety.Tests;

public class DisplayTopologyStoreTests : IDisposable {
    private readonly string _tempDir;
    private readonly string _path;

    public DisplayTopologyStoreTests() {
        _tempDir = Path.Combine(Path.GetTempPath(), "klikety-display-topologies-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _path = Path.Combine(_tempDir, "display-topologies.json");
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void RoundTripsKnownFingerprint() {
        var a = Row(@"\\?\DISPLAY#A", new Rectangle(0, 0, 1920, 1080));
        var b = Row(@"\\?\DISPLAY#B", new Rectangle(1920, 0, 1920, 1080));
        var first = new DisplayTopologyStore(_path);
        var original = first.Resolve([a, b]);

        Assert.Equal(1, original[a.DevicePath]);
        Assert.Equal(2, original[b.DevicePath]);
        Assert.True(File.Exists(_path));

        var aMoved = Row(a.DevicePath, new Rectangle(3840, 0, 1920, 1080));
        var bMoved = Row(b.DevicePath, new Rectangle(-1920, 0, 1920, 1080));
        var second = new DisplayTopologyStore(_path);
        var reused = second.Resolve([aMoved, bMoved]);

        Assert.Equal(original[a.DevicePath], reused[a.DevicePath]);
        Assert.Equal(original[b.DevicePath], reused[b.DevicePath]);
        Assert.Equal(1, reused[a.DevicePath]);
        Assert.Equal(2, reused[b.DevicePath]);
        Assert.NotEqual(1, DisplayNumbering.AssignSpatially([aMoved, bMoved])[a.DevicePath]);
    }

    private static DisplayInfo Row(string devicePath, Rectangle bounds) =>
        new(bounds, 1.0, @"\\.\DISPLAY1", devicePath);
}
