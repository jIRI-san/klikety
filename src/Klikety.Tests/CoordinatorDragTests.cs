using System.Drawing;

using Klikety.Config;
using Klikety.Input;
using Klikety.Services;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public class CoordinatorDragTests {
    private static (NavigatorCoordinator Coordinator, FakeHotKeyService HotKey, FakeKeyboardHookService Hook,
        FakeMouseActionService Mouse, FakeOverlayWindow Overlay, FakeGridRenderer Renderer, FakePlatformServices Platform,
        FakeModifierDetector ModifierDetector) CreateCoordinator(
        NavigationMode mode = NavigationMode.Both, ConfigModel? configOverride = null) =>
        CoordinatorTestHelper.CreateCoordinator(mode, configOverride);

    // --- Bounds Validation Tests ---

    [Fact]
    public void ActionOutOfBounds_Suppressed() {
        var (_, hotKey, hook, mouse, overlay, _, platform, _) = CreateCoordinator();

        platform.Screen.Bounds = new Rectangle(0, 0, 100, 100);
        platform.Cursor.Position = new Point(50, 50);

        hotKey.SimulateActivation();
        hook.SimulateKeyDown(VKey.A);
        hook.SimulateKeyDown(VKey.W);
        hook.SimulateKeyUp(VKey.Space);
        hook.SimulateKeyDown(VKey.Space);

        Assert.False(overlay.IsVisible);
        Assert.Contains(mouse.Calls, c => c.Action == MouseAction.LeftClick);
    }

    [Fact]
    public void ActionOutOfBounds_RestoresCursorAndSuppresses() {
        var config = new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
            },
        };
        var (_, hotKey, hook, mouse, overlay, _, platform, _) = CreateCoordinator(configOverride: config);

        platform.Screen.Bounds = new Rectangle(0, 0, 1920, 1080);
        platform.Cursor.Position = new Point(960, 540);

        hotKey.SimulateActivation();
        Assert.True(overlay.IsVisible);

        hook.SimulateKeyDown(VKey.A);
        hook.SimulateKeyDown(VKey.Q);
        hook.SimulateKeyUp(VKey.Space);
        hook.SimulateKeyDown(VKey.Space);

        Assert.False(overlay.IsVisible);
    }

    // --- Action Dispatch Tests ---

    [Fact]
    public void MoveOnly_DeactivatesOverlay_NoClick() {
        var config = new ConfigModel {
            ActionBindings = new Dictionary<string, MouseAction>(StringComparer.OrdinalIgnoreCase) {
                { "B", MouseAction.MoveOnly },
            },
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
            },
        };
        var (_, hotKey, hook, mouse, overlay, _, _, _) = CreateCoordinator(configOverride: config);

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.B);

        Assert.False(overlay.IsVisible);
        var actionCall = Assert.Single(mouse.Calls, c => c.Action is not null);
        Assert.Equal(MouseAction.MoveOnly, actionCall.Action);
    }

    [Fact]
    public void Modifiers_CapturedAndPassedToSendAction() {
        var (_, hotKey, hook, mouse, _, _, _, modifierDetector) = CreateCoordinator();
        modifierDetector.Modifiers = ActionModifiers.Shift | ActionModifiers.Ctrl;

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.Space);

        var actionCall = Assert.Single(mouse.Calls, c => c.Action is not null);
        Assert.Equal(ActionModifiers.Shift | ActionModifiers.Ctrl, actionCall.Modifiers);
    }

    [Fact]
    public void MoveOnly_IgnoresModifiers() {
        var config = new ConfigModel {
            ActionBindings = new Dictionary<string, MouseAction>(StringComparer.OrdinalIgnoreCase) {
                { "B", MouseAction.MoveOnly },
            },
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
            },
        };
        var (_, hotKey, hook, mouse, _, _, _, modifierDetector) = CreateCoordinator(configOverride: config);
        modifierDetector.Modifiers = ActionModifiers.Shift;

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.B);

        var actionCall = Assert.Single(mouse.Calls, c => c.Action is not null);
        Assert.Equal(ActionModifiers.None, actionCall.Modifiers);
    }

    [Fact]
    public void NoModifiers_PassesNoneToSendAction() {
        var (_, hotKey, hook, mouse, _, _, _, modifierDetector) = CreateCoordinator();
        modifierDetector.Modifiers = ActionModifiers.None;

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.Space);

        var actionCall = Assert.Single(mouse.Calls, c => c.Action is not null);
        Assert.Equal(ActionModifiers.None, actionCall.Modifiers);
    }

    [Fact]
    public void ClickThroughSafety_HideBeforeSendAction() {
        var (_, hotKey, hook, mouse, overlay, _, _, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKeyDown(VKey.A);
        hook.SimulateKeyDown(VKey.W);
        hook.SimulateKeyUp(VKey.Space);
        hook.SimulateKeyDown(VKey.Space);

        Assert.False(overlay.IsVisible);
        Assert.True(overlay.HideCount > 0);
        Assert.Contains(mouse.Calls, c => c.Action == MouseAction.LeftClick);
    }

    // --- Drag-and-drop tests ---

    private static ConfigModel DragConfig() => new() {
        ActionBindings = new Dictionary<string, MouseAction>(StringComparer.OrdinalIgnoreCase) {
            { "Z", MouseAction.DragDrop },
            { "V", MouseAction.RightClick },
            { "B", MouseAction.MoveOnly },
        },
        Modes = new ModesConfig {
            UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
        },
    };

    [Fact]
    public void DragDrop_StartsPhase_ResetsOverlay_ShowsStatusText() {
        var config = DragConfig();
        var (_, hotKey, hook, mouse, overlay, _, _, _) = CreateCoordinator(configOverride: config);

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.Z);

        Assert.True(overlay.IsVisible);
        Assert.Equal("Select drag target", overlay.StatusText);
        Assert.DoesNotContain(mouse.Calls, c => c.Action is not null);
        Assert.Empty(mouse.DragCalls);
    }

    [Fact]
    public void DragDrop_SecondAction_LeftClick_SendsDrag() {
        var config = DragConfig();
        var (_, hotKey, hook, mouse, overlay, _, _, modifierDetector) = CreateCoordinator(configOverride: config);
        modifierDetector.Modifiers = ActionModifiers.None;

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.Z);

        hook.SimulateKey(VKey.S);
        hook.SimulateKey(VKey.E);
        hook.SimulateKey(VKey.Space);

        Assert.False(overlay.IsVisible);
        var drag = Assert.Single(mouse.DragCalls);
        Assert.Equal(MouseAction.LeftClick, drag.Button);
        Assert.Equal(ActionModifiers.None, drag.Modifiers);
    }

    [Fact]
    public void DragDrop_SecondAction_RightClick_SendsRightDrag() {
        var config = DragConfig();
        var (_, hotKey, hook, mouse, _, _, _, _) = CreateCoordinator(configOverride: config);

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.Z);

        hook.SimulateKey(VKey.S);
        hook.SimulateKey(VKey.E);
        hook.SimulateKey(VKey.V);

        var drag = Assert.Single(mouse.DragCalls);
        Assert.Equal(MouseAction.RightClick, drag.Button);
    }

    [Fact]
    public void DragDrop_SecondAction_WithModifiers_PassesModifiers() {
        var config = DragConfig();
        var (_, hotKey, hook, mouse, _, _, _, modifierDetector) = CreateCoordinator(configOverride: config);
        modifierDetector.Modifiers = ActionModifiers.Shift | ActionModifiers.Ctrl;

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.Z);

        hook.SimulateKey(VKey.S);
        hook.SimulateKey(VKey.E);
        hook.SimulateKey(VKey.Space);

        var drag = Assert.Single(mouse.DragCalls);
        Assert.Equal(ActionModifiers.Shift | ActionModifiers.Ctrl, drag.Modifiers);
    }

    [Fact]
    public void DragDrop_Escape_AbortsDrag_RestoresCursorToOrigin() {
        var config = DragConfig();
        var (_, hotKey, hook, mouse, overlay, _, platform, _) = CreateCoordinator(configOverride: config);
        platform.Cursor.Position = new Point(100, 200);

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.Z);

        hook.SimulateKey(VKey.Escape);

        Assert.False(overlay.IsVisible);
        Assert.Empty(mouse.DragCalls);
        var moveCall = mouse.Calls.Last(c => c.Action is null);
        Assert.Equal(new Point(100, 200), moveCall.Point);
    }

    [Fact]
    public void DragDrop_MoveOnlyInDragMode_Ignored() {
        var config = DragConfig();
        var (_, hotKey, hook, mouse, overlay, _, _, _) = CreateCoordinator(configOverride: config);

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.Z);

        hook.SimulateKey(VKey.S);
        hook.SimulateKey(VKey.E);
        hook.SimulateKey(VKey.B);

        Assert.True(overlay.IsVisible);
        Assert.Empty(mouse.DragCalls);
    }

    [Fact]
    public void DragDrop_DragDropInDragMode_Ignored() {
        var config = DragConfig();
        var (_, hotKey, hook, mouse, overlay, _, _, _) = CreateCoordinator(configOverride: config);

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.Z);

        hook.SimulateKey(VKey.S);
        hook.SimulateKey(VKey.E);
        hook.SimulateKey(VKey.Z);

        Assert.True(overlay.IsVisible);
        Assert.Empty(mouse.DragCalls);
    }

    [Fact]
    public void DragDrop_FocusLoss_AbortsDrag_RestoresCursor() {
        var config = DragConfig();
        var (_, hotKey, hook, mouse, overlay, _, platform, _) = CreateCoordinator(configOverride: config);
        platform.Cursor.Position = new Point(300, 400);

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.Z);

        overlay.SimulateFocusLoss();

        Assert.False(overlay.IsVisible);
        Assert.Empty(mouse.DragCalls);
        var moveCall = mouse.Calls.Last(c => c.Action is null);
        Assert.Equal(new Point(300, 400), moveCall.Point);
    }

    [Fact]
    public void DragDrop_StatusTextClearedOnCompletion() {
        var config = DragConfig();
        var (_, hotKey, hook, _, overlay, _, _, _) = CreateCoordinator(configOverride: config);

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.Z);

        Assert.Equal("Select drag target", overlay.StatusText);

        hook.SimulateKey(VKey.S);
        hook.SimulateKey(VKey.E);
        hook.SimulateKey(VKey.Space);

        Assert.Null(overlay.StatusText);
    }

    [Fact]
    public void DragDrop_Completion_NoIntermediateMoveTo_Origin() {
        var config = DragConfig();
        var (_, hotKey, hook, mouse, overlay, _, platform, _) = CreateCoordinator(configOverride: config);
        platform.Cursor.Position = new Point(500, 500);

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.Z);

        hook.SimulateKey(VKey.S);
        hook.SimulateKey(VKey.E);
        hook.SimulateKey(VKey.Space);

        Assert.False(overlay.IsVisible);
        Assert.DoesNotContain(mouse.Calls, c => c.Action is null && c.Point == new Point(500, 500)
            && mouse.Calls.IndexOf(c) > mouse.Calls.FindIndex(x => x.Action is null && x.Point != new Point(500, 500)));
    }
}
