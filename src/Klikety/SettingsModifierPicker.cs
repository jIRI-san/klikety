using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

using Klikety.Config;

namespace Klikety;

internal sealed class SettingsModifierPicker : UserControl {
    private readonly ToggleButton _button;
    private readonly TextBlock _summary;
    private readonly Popup _popup;
    private readonly List<(HotKeyModifiers Flag, CheckBox Box)> _options = [];
    private HotKeyModifiers _value;
    private bool _updating;

    internal SettingsModifierPicker(HotKeyModifiers value) {
        Focusable = true;
        IsTabStop = false;
        VerticalAlignment = VerticalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        _summary = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var arrow = new System.Windows.Shapes.Path {
            Data = Geometry.Parse("M 0,0 L 4,4 L 8,0"), StrokeThickness = 1.5,
            Width = 8, Height = 4, Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var caption = new DockPanel();
        DockPanel.SetDock(arrow, Dock.Right);
        caption.Children.Add(arrow);
        caption.Children.Add(_summary);
        _button = new ToggleButton {
            Content = caption, MinHeight = 32, Padding = new Thickness(10, 5, 10, 5),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        arrow.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new Binding(nameof(Foreground)) { Source = _button });
        AutomationProperties.SetName(_button, "Choose modifiers");
        _button.SetBinding(ToolTipProperty, new Binding {
            Path = new PropertyPath(AutomationProperties.NameProperty), Source = this,
        });
        var choices = new StackPanel();
        Add(choices, HotKeyModifiers.Control, "Ctrl");
        Add(choices, HotKeyModifiers.Alt, "Alt");
        Add(choices, HotKeyModifiers.Shift, "Shift");
        Add(choices, HotKeyModifiers.Win, "Win");
        var border = new Border {
            Child = choices, Padding = new Thickness(12, 6, 12, 6),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
        };
        border.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorTertiaryBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        border.SetBinding(WidthProperty, new Binding(nameof(ActualWidth)) { Source = _button });
        _popup = new Popup {
            Child = border, PlacementTarget = _button, Placement = PlacementMode.Bottom,
            StaysOpen = false, AllowsTransparency = true,
        };
        _popup.SetBinding(Popup.IsOpenProperty, new Binding(nameof(ToggleButton.IsChecked)) {
            Source = _button, Mode = BindingMode.TwoWay,
        });
        _popup.Opened += (_, _) => {
            _options.FirstOrDefault(option => option.Box.IsChecked == true, _options[0]).Box.Focus();
            RaiseExpansionChanged(ExpandCollapseState.Collapsed, ExpandCollapseState.Expanded);
        };
        _popup.Closed += (_, _) => RaiseExpansionChanged(ExpandCollapseState.Expanded, ExpandCollapseState.Collapsed);
        _button.PreviewKeyDown += HandleKeys;
        choices.PreviewKeyDown += HandleKeys;
        GotKeyboardFocus += (_, e) => { if (ReferenceEquals(e.NewFocus, this)) { _button.Focus(); } };
        IsEnabledChanged += (_, _) => { if (!IsEnabled) { _popup.IsOpen = false; } };
        Unloaded += (_, _) => _popup.IsOpen = false;
        var root = new System.Windows.Controls.Grid();
        root.Children.Add(_button);
        root.Children.Add(_popup);
        Content = root;
        Value = value;
    }

    internal HotKeyModifiers Value {
        get => _value;
        set {
            var previous = _value;
            var previousSummary = _summary.Text;
            _value = value;
            _updating = true;
            try {
                foreach (var (flag, box) in _options) { box.IsChecked = value.HasFlag(flag); }
                _summary.Text = ((int)value & ~15) != 0 ? $"Invalid modifiers ({value})"
                    : value == HotKeyModifiers.None ? "None"
                    : string.Join(", ", _options.Where(option => value.HasFlag(option.Flag)).Select(option => (string)option.Box.Content));
                AutomationProperties.SetHelpText(this, _summary.Text);
                AutomationProperties.SetName(_button, "Modifiers: " + _summary.Text);
            } finally { _updating = false; }
            if (previous != value) {
                ValueChanged?.Invoke(this, EventArgs.Empty);
                if (UIElementAutomationPeer.FromElement(this) is { } peer) {
                    peer.RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, previousSummary, _summary.Text);
                }
            }
        }
    }

    internal event EventHandler? ValueChanged;

    private void Add(Panel choices, HotKeyModifiers flag, string label) {
        var box = new CheckBox { Content = label, Margin = new Thickness(0, 5, 0, 5) };
        AutomationProperties.SetName(box, label + " modifier");
        AutomationProperties.SetAutomationId(box, "modifier." + flag);
        _options.Add((flag, box));
        choices.Children.Add(box);
        void Changed() {
            if (!_updating) {
                Value = _options.Where(option => option.Box.IsChecked == true)
                    .Aggregate(HotKeyModifiers.None, (flags, option) => flags | option.Flag);
            }
        }
        box.Checked += (_, _) => Changed();
        box.Unchecked += (_, _) => Changed();
    }

    private void HandleKeys(object sender, KeyEventArgs e) {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.F4 || (key == Key.Down && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))) {
            _popup.IsOpen = !_popup.IsOpen;
            if (!_popup.IsOpen) { _button.Focus(); }
            e.Handled = true;
        } else if (_popup.IsOpen && key == Key.Space && e.OriginalSource is CheckBox box) {
            if (!e.IsRepeat) { box.IsChecked = box.IsChecked != true; }
            e.Handled = true;
        } else if (_popup.IsOpen && key is Key.Escape or Key.Enter or Key.Tab) {
            _popup.IsOpen = false;
            _button.Focus();
            if (key == Key.Tab) {
                _button.MoveFocus(new TraversalRequest(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                    ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next));
            }
            e.Handled = true;
        } else if (_popup.IsOpen && key is Key.Up or Key.Down or Key.Home or Key.End) {
            var current = _options.FindIndex(option => option.Box.IsKeyboardFocused);
            var next = key switch {
                Key.Home => 0, Key.End => _options.Count - 1,
                Key.Up => (current - 1 + _options.Count) % _options.Count,
                _ => (current + 1) % _options.Count,
            };
            _options[next].Box.Focus();
            e.Handled = true;
        }
    }

    private void RaiseExpansionChanged(ExpandCollapseState oldValue, ExpandCollapseState newValue) =>
        UIElementAutomationPeer.FromElement(this)?.RaisePropertyChangedEvent(
            ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty, oldValue, newValue);

    protected override AutomationPeer OnCreateAutomationPeer() => new PickerPeer(this);

    private sealed class PickerPeer(SettingsModifierPicker picker) : UserControlAutomationPeer(picker), IExpandCollapseProvider, IValueProvider {
        protected override string GetClassNameCore() => nameof(SettingsModifierPicker);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ComboBox;
        protected override bool IsKeyboardFocusableCore() => picker.IsEnabled;
        protected override bool HasKeyboardFocusCore() => picker.IsKeyboardFocusWithin;
        protected override void SetFocusCore() => picker._button.Focus();
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface is PatternInterface.ExpandCollapse or PatternInterface.Value ? this : base.GetPattern(patternInterface);
        public ExpandCollapseState ExpandCollapseState => picker.Dispatcher.Invoke(() =>
            picker._popup.IsOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
        public void Expand() => picker.Dispatcher.Invoke(() => {
            if (!picker.IsEnabled) { throw new ElementNotEnabledException(); }
            picker._popup.IsOpen = true;
        });
        public void Collapse() => picker.Dispatcher.Invoke(() => picker._popup.IsOpen = false);
        public bool IsReadOnly => true;
        public string Value => picker.Dispatcher.Invoke(() => picker._summary.Text);
        public void SetValue(string value) => throw new InvalidOperationException("Toggle the individual modifier checkboxes.");
    }
}
