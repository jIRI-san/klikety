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

    public MacroPickerOverlay() {
        InitializeComponent();
        KeyDown += OnKeyDown;
        Deactivated += (_, _) => DismissPicker();
    }

    void IMacroPickerWindow.Show(MacroDefinition?[] macros, VKey[] slotKeys) {
        _macros = macros;
        _keyToSlot.Clear();
        SlotPanel.Children.Clear();

        var count = Math.Min(macros.Length, Math.Min(slotKeys.Length, 10));
        for (var i = 0; i < count; i++) {
            var wpfKey = KeyInterop.KeyFromVirtualKey((int)slotKeys[i]);
            _keyToSlot[wpfKey] = i;

            var label = slotKeys[i].ToString();
            var name = macros[i]?.Name ?? "<empty>";
            var isEmpty = macros[i] is null;

            var row = new TextBlock {
                Text = $"  {label}  —  {name}",
                FontSize = 18,
                FontFamily = new FontFamily("Consolas"),
                Foreground = isEmpty ? Brushes.Gray : Brushes.White,
                Margin = new Thickness(0, 2, 0, 2),
            };
            SlotPanel.Children.Add(row);
        }

        Show();
        NativeMethods.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        Activate();
        Keyboard.Focus(this);
    }

    void IMacroPickerWindow.Close() {
        DismissPicker();
    }

    private void OnKeyDown(object sender, KeyEventArgs e) {
        if (e.Key == Key.Escape) {
            DismissPicker();
            return;
        }

        if (_keyToSlot.TryGetValue(e.Key, out var slot)) {
            if (_macros.Length > slot && _macros[slot] is not null) {
                Hide();
                SlotSelected?.Invoke(slot);
            }
        }
    }

    private void DismissPicker() {
        Hide();
        PickerClosed?.Invoke();
    }
}
