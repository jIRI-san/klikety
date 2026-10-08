using Klikety.Config;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Tests.Fakes;

using Microsoft.Extensions.Logging.Abstractions;

namespace Klikety.Tests;

public class ElementHintsCoordinatorTests {
    private static (NavigatorCoordinator Coordinator, FakeHotKeyService Hotkey, FakeKeyboardHookService Hook,
        FakeMouseActionService Mouse, FakeOverlayWindow Overlay, FakePlatformServices Platform) Create(
        FakeElementHintService service, FakeElementPointGuard? guard = null, bool defaultHints = false) {
        var config = new ConfigModel {
            ActionBindings = new() {
                ["X"] = MouseAction.DoubleClick, ["V"] = MouseAction.RightClick,
                ["C"] = MouseAction.MiddleClick, ["B"] = MouseAction.MoveOnly, ["Z"] = MouseAction.DragDrop
            },
            Modes = new ModesConfig {
                UniformGrid = new() { Enabled = true, Default = !defaultHints, TwoKey = true, ArrowKeys = true },
                ElementHints = new() { Enabled = true, Default = defaultHints, ChordKey = VKey.Tab, TwoKey = true, ArrowKeys = true },
            },
        };
        var platform = new FakePlatformServices();
        platform.ForegroundWindow.Handle = 123;
        platform.ForegroundWindow.Bounds = new(0, 0, 800, 600);
        var hook = new FakeKeyboardHookService();
        var hotkey = new FakeHotKeyService();
        var overlay = new FakeOverlayWindow { RaiseFocusLostOnHide = true };
        var mouse = new FakeMouseActionService();
        var factory = new ModeSessionFactory(config, new ActionMapper(config.ActionBindings), new FakeGridRenderer(),
            elementHintsService: service);
        var coordinator = new NavigatorCoordinator(hotkey, hook, mouse, overlay, factory, platform,
            new FakeModifierDetector(), config, NullLogger.Instance, keyLabelResolverFactory: _ => new FakeKeyLabelResolver(),
            elementPointGuard: guard ?? new FakeElementPointGuard());
        return (coordinator, hotkey, hook, mouse, overlay, platform);
    }

    [Fact]
    public void CaptureTargetBeforeOverlayAndAllowNonQwertyAndLockedFallback() {
        var service = new FakeElementHintService();
        var h = Create(service);
        using var coordinator = h.Coordinator;
        h.Platform.KeyboardLayout.Qwerty = false;
        h.Hotkey.SimulateActivation();
        h.Hook.SimulateKey(VKey.Tab);
        Assert.Equal((nint)123, Assert.Single(service.Scans).Context.Hwnd);
        h.Hook.SimulateKey(VKey.A); h.Hook.SimulateKey(VKey.Q);
        Assert.DoesNotContain(h.Mouse.Calls, c => c.Action is not null);
        h.Hook.SimulateKey(VKey.Return);
        h.Hook.SimulateKey(VKey.Space);
        Assert.Single(h.Mouse.Calls, c => c.Action == MouseAction.LeftClick);
        Assert.Equal(1, service.RetireCount);
    }

    [Fact]
    public async Task HideBeforeValidationSnapshotModifiersAndDispatchExactlyOnce() {
        var service = new FakeElementHintService { ValidationCompletion = new() };
        var h = Create(service);
        using var coordinator = h.Coordinator;
        var visibility = new List<bool>();
        coordinator.ElementValidationChanged += visibility.Add;
        service.Validating = () => {
            Assert.False(h.Overlay.IsVisible); Assert.False(h.Hook.IsEnabled);
            Assert.True(coordinator.IsElementValidationPending);
            Assert.Equal([true], visibility);
        };
        h.Hotkey.SimulateActivation(); h.Hook.SimulateKey(VKey.Tab);
        h.Hook.SimulateKey(VKey.A); h.Hook.SimulateKey(VKey.Q);
        h.Hook.SimulateKeyDown(VKey.Space, Services.HookModifierFlags.Control | Services.HookModifierFlags.Shift);
        h.Hook.SimulateKey(VKey.Space);
        h.Hook.SimulateKeyUp(VKey.Control);
        service.ValidationCompletion.SetResult(service.Response with { Point = new(20, 20) });
        for (int i = 0; i < 50 && !h.Mouse.Calls.Any(c => c.Action is not null); i++) { await Task.Delay(10); }
        var action = Assert.Single(h.Mouse.Calls, c => c.Action is not null);
        Assert.Equal(ActionModifiers.Ctrl | ActionModifiers.Shift, action.Modifiers);
        Assert.Single(service.Validations);
        Assert.Equal([true, false], visibility);
        Assert.False(coordinator.IsElementValidationPending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectionAndLateCompletionNeverInjectInput(bool late) {
        var service = new FakeElementHintService { ValidationCompletion = new() };
        var h = Create(service, new FakeElementPointGuard { Allowed = false });
        using var coordinator = h.Coordinator;
        h.Hotkey.SimulateActivation(); h.Hook.SimulateKey(VKey.Tab);
        h.Hook.SimulateKey(VKey.A); h.Hook.SimulateKey(VKey.Q); h.Hook.SimulateKey(VKey.Space);
        if (late) { coordinator.DeactivateOverlay(); h.Hotkey.SimulateActivation(); }
        service.ValidationCompletion.SetResult(service.Response with { Point = new(20, 20) });
        await Task.Delay(30);
        Assert.DoesNotContain(h.Mouse.Calls, c => c.Action is not null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultHintsHelpAndLayoutRefreshKeepPagePrefixOrSelectionWithoutRescan(bool selected) {
        var defaults = new ConfigModel();
        int capacity = defaults.HorizontalKeys.Length * defaults.VerticalKeys.Length;
        var service = new FakeElementHintService { Response = FakeElementHintService.Result(capacity + 5) };
        var h = Create(service, defaultHints: true);
        using var coordinator = h.Coordinator;
        h.Hotkey.SimulateActivation();
        h.Hook.SimulateKey(VKey.Right); h.Hook.SimulateKey(VKey.A);
        if (selected) { h.Hook.SimulateKey(VKey.Q); }
        h.Hook.SimulateKey(VKey.OemQuestion);
        Assert.NotNull(h.Overlay.CurrentHelp);
        Assert.Contains(h.Overlay.CurrentHelp!.Entries, e => e.Key == VKey.Return);
        h.Hook.SimulateKey(VKey.Escape);
        h.Platform.KeyboardLayout.Layout = 2;
        h.Overlay.SimulateKeyboardLayoutChange();
        if (!selected) { h.Hook.SimulateKey(VKey.Q); }
        h.Hook.SimulateKey(VKey.Space);
        Assert.Single(service.Scans);
        Assert.Equal(capacity + 1, Assert.Single(service.Validations));
        Assert.Single(h.Mouse.Calls, c => c.Action is not null);
    }

    [Theory]
    [InlineData(VKey.Space, MouseAction.LeftClick)]
    [InlineData(VKey.X, MouseAction.DoubleClick)]
    [InlineData(VKey.V, MouseAction.RightClick)]
    [InlineData(VKey.C, MouseAction.MiddleClick)]
    [InlineData(VKey.B, MouseAction.MoveOnly)]
    public void AllPhysicalActionsUseValidatedPoint(VKey key, MouseAction expected) {
        var service = new FakeElementHintService();
        var h = Create(service);
        using var coordinator = h.Coordinator;
        h.Hotkey.SimulateActivation(); h.Hook.SimulateKey(VKey.Tab);
        h.Hook.SimulateKey(VKey.A); h.Hook.SimulateKey(VKey.Q);
        h.Hook.SimulateKeyDown(key, Services.HookModifierFlags.Alt);
        var action = Assert.Single(h.Mouse.Calls, c => c.Action is not null);
        Assert.Equal(expected, action.Action);
        Assert.Equal(new System.Drawing.Point(20, 20), action.Point);
        Assert.Equal(expected == MouseAction.MoveOnly ? ActionModifiers.None : ActionModifiers.Alt, action.Modifiers);
    }

    [Theory]
    [InlineData(Automation.HintOutcome.Timeout)]
    [InlineData(Automation.HintOutcome.StaleTarget)]
    [InlineData(Automation.HintOutcome.NoSafePoint)]
    [InlineData(Automation.HintOutcome.AccessDenied)]
    [InlineData(Automation.HintOutcome.ProviderError)]
    public void FailedValidationNotifiesWithoutInputOrRecordedStep(Automation.HintOutcome outcome) {
        var service = new FakeElementHintService();
        var h = Create(service);
        using var coordinator = h.Coordinator;
        string? notification = null;
        coordinator.RuntimeNotification += value => notification = value;
        h.Hotkey.SimulateActivation(); h.Hook.SimulateKey(VKey.Tab);
        h.Hook.SimulateKey(VKey.OemPipe); h.Hook.SimulateKey(VKey.F1);
        h.Hook.SimulateKey(VKey.A); h.Hook.SimulateKey(VKey.Q);
        service.Response = service.Response with { Outcome = outcome };
        h.Hook.SimulateKey(VKey.Space);
        Assert.DoesNotContain(h.Mouse.Calls, c => c.Action is not null);
        Assert.NotNull(notification);
        Assert.False(h.Overlay.IsVisible);
    }

    [Fact]
    public void ValidatedDragStartResetsDefaultModeAndPreservesExistingFinishSemantics() {
        var service = new FakeElementHintService();
        var h = Create(service, defaultHints: true);
        using var coordinator = h.Coordinator;
        h.Hotkey.SimulateActivation();
        h.Hook.SimulateKey(VKey.A); h.Hook.SimulateKey(VKey.Q); h.Hook.SimulateKey(VKey.Z);
        Assert.True(h.Overlay.IsVisible);
        Assert.Equal("Select drag target", h.Overlay.StatusText);
        Assert.Equal(2, service.Scans.Count);
        h.Hook.SimulateKey(VKey.A); h.Hook.SimulateKey(VKey.Q); h.Hook.SimulateKey(VKey.V);
        Assert.Single(h.Mouse.DragCalls);
        Assert.Equal(MouseAction.RightClick, h.Mouse.DragCalls[0].Button);
        Assert.False(h.Overlay.IsVisible);
    }

    [Fact]
    public void ScopeChangeRetiresOldSnapshotAndKeepsApplicationIdentity() {
        var service = new FakeElementHintService();
        var h = Create(service, defaultHints: true);
        using var coordinator = h.Coordinator;
        h.Hotkey.SimulateActivation(); h.Hook.SimulateKey(VKey.OemPeriod);
        Assert.Equal(2, service.Scans.Count);
        Assert.All(service.Scans, s => Assert.Equal((nint)123, s.Context.Hwnd));
        Assert.Equal(new Automation.HintRect(0, 0, 800, 600), service.Scans[1].Region);
        coordinator.DeactivateOverlay();
        Assert.Equal(2, service.RetireCount);
    }

    [Theory]
    [InlineData("topology")]
    [InlineData("config-disposal")]
    [InlineData("focus-loss")]
    public async Task LifecycleInvalidationRetiresSnapshotAndRejectsPendingApproval(string transition) {
        var service = new FakeElementHintService { ValidationCompletion = new() };
        var h = Create(service, defaultHints: true);
        using var coordinator = h.Coordinator;
        var finished = new TaskCompletionSource();
        coordinator.ElementValidationChanged += pending => { if (!pending) { finished.TrySetResult(); } };
        h.Hotkey.SimulateActivation();
        h.Hook.SimulateKey(VKey.A); h.Hook.SimulateKey(VKey.Q); h.Hook.SimulateKey(VKey.Space);
        Assert.True(coordinator.IsElementValidationPending);
        switch (transition) {
            case "topology": h.Overlay.SimulateDisplayChange(); break;
            case "config-disposal": coordinator.Dispose(); break;
            case "focus-loss": h.Overlay.SimulateFocusLoss(); break;
        }
        Assert.False(coordinator.IsElementValidationPending);
        Assert.Equal(1, service.RetireCount);
        service.ValidationCompletion.SetResult(service.Response with { Point = new(20, 20) });
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.DoesNotContain(h.Mouse.Calls, c => c.Action is not null);
        Assert.False(h.Overlay.IsVisible);
        Assert.False(h.Hook.IsEnabled);
        Assert.Single(service.Scans);
        if (transition == "config-disposal") {
            h.Hotkey.SimulateActivation(); h.Hook.SimulateKey(VKey.Space);
            Assert.Single(service.Scans);
            Assert.DoesNotContain(h.Mouse.Calls, c => c.Action is not null);
        }
    }
}
