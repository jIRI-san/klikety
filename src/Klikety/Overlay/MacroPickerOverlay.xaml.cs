using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Klikety.Config;
using Klikety.Input;
using Klikety.Interop;
using Klikety.Services;

namespace Klikety.Overlay;

public partial class MacroPickerOverlay : Window, IMacroPickerWindow {
    public event Action<int>? SlotSelected;
    public event Action? PickerClosed;

    private MacroDefinition?[] _macros = [];
    private readonly Dictionary<Key, int> _keyToSlot = [];
    private bool _slotSelected;

    public MacroPickerOverlay() {
        InitializeComponent();
        KeyDown += OnKeyDown;
        Deactivated += (_, _) => {
            Debug.WriteLine($"[MacroPicker] Deactivated (slotSelected={_slotSelected})");
            if (!_slotSelected) {
                DismissPicker();
            }
        };
        Activated += (_, _) => Debug.WriteLine("[MacroPicker] Activated");
    }

    void IMacroPickerWindow.Show(MacroDefinition?[] macros, VKey[] slotKeys) {
        _macros = macros;
        _slotSelected = false;
        _keyToSlot.Clear();
        SlotPanel.Children.Clear();

        var count = Math.Min(macros.Length, Math.Min(slotKeys.Length, 10));
        for (var i = 0; i < count; i++) {
            var wpfKey = KeyInterop.KeyFromVirtualKey((int)slotKeys[i]);
            _keyToSlot[wpfKey] = i;

            var label = slotKeys[i].ToString();
            var macro = macros[i];
            var name = macro?.Name ?? "<empty>";
            var isEmpty = macro is null;
            var badge = macro?.PositionMode == MacroPositionMode.WindowRelative ? " [W]" : macro is not null ? " [S]" : "";

            var row = new TextBlock {
                Text = $"  {label}  —  {name}{badge}",
                FontSize = 18,
                FontFamily = new FontFamily("Consolas"),
                Foreground = isEmpty ? Brushes.Gray : Brushes.White,
                Margin = new Thickness(0, 2, 0, 2),
            };
            SlotPanel.Children.Add(row);
        }

        Debug.WriteLine("[MacroPicker] About to Show()");
        Show();
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        Debug.WriteLine($"[MacroPicker] SetForegroundWindow hwnd={hwnd}");
        NativeMethods.SetForegroundWindow(hwnd);
        Activate();
        Keyboard.Focus(this);
        Debug.WriteLine($"[MacroPicker] Show complete, IsActive={IsActive}, IsFocused={IsFocused}");
    }

    void IMacroPickerWindow.Close() {
        DismissPicker();
    }

    private void OnKeyDown(object sender, KeyEventArgs e) {
        Debug.WriteLine($"[MacroPicker] KeyDown: {e.Key}");
        if (e.Key == Key.Escape) {
            DismissPicker();
            return;
        }

        if (_keyToSlot.TryGetValue(e.Key, out var slot)) {
            if (_macros.Length > slot && _macros[slot] is not null) {
                _slotSelected = true;
                Hide();
                SlotSelected?.Invoke(slot);
            }
        }
    }

    private void DismissPicker() {
        Debug.WriteLine("[MacroPicker] DismissPicker called");
        Hide();
        PickerClosed?.Invoke();
    }
}
