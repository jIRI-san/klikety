using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

using Klikety;
using Klikety.Config;

namespace Klikety.Tests;

[Collection("Settings UI")]
public sealed class SettingsAccessibilityTests {
    private static readonly double[] LogicalWidths = [980, 1080, 1280];
    [Fact]
    public void AllPagesHaveNamedControlsCollapsedAdvancedAndUnclippedLogicalLayouts() {
        var root = Path.Combine(Path.GetTempPath(), "Klikety-accessibility-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "config.json");
        File.WriteAllText(path, "{\"configVersion\":9}");
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                var window = new SettingsWindow(path, _ => { },
                    () => new(new ConfigModel(), false, false), _ => SettingsApplyOutcome.Success,
                    _ => SettingsApplyOutcome.Success, confirmDiscard: _ => true);
                var categories = Assert.IsType<ListBox>(window.FindName("Categories"));
                var host = Assert.IsType<ContentControl>(window.FindName("PageHost"));
                var content = Assert.IsType<System.Windows.Controls.Grid>(window.Content);
                var save = Assert.IsType<Button>(window.FindName("SaveButton"));
                var close = Assert.IsType<Button>(window.FindName("CloseButton"));
                Assert.Equal("Close Settings", UIElementAutomationPeer.CreatePeerForElement(close)?.GetName());
                Assert.True(close.IsTabStop);
                for (var index = 0; index < 7; index++) {
                    categories.SelectedIndex = index;
                    var page = Assert.IsType<StackPanel>(host.Content);
                    var elements = Descendants(page).ToArray();
                    foreach (var advanced in elements.OfType<Expander>()) {
                        Assert.False(advanced.IsExpanded);
                        advanced.IsExpanded = true;
                    }
                    foreach (var picker in elements.OfType<SettingsModifierPicker>()) {
                        var peer = UIElementAutomationPeer.CreatePeerForElement(picker);
                        Assert.Equal(AutomationControlType.ComboBox, peer.GetAutomationControlType());
                        Assert.False(string.IsNullOrWhiteSpace(peer.GetName()));
                        Assert.NotNull(peer.GetPattern(PatternInterface.ExpandCollapse));
                        Assert.NotNull(peer.GetPattern(PatternInterface.Value));
                    }
                    foreach (var control in elements.OfType<Control>().Where(item =>
                                 item is Button or TextBox or ComboBox or CheckBox or SettingsModifierPicker)) {
                        var peer = UIElementAutomationPeer.CreatePeerForElement(control);
                        Assert.False(string.IsNullOrWhiteSpace(peer?.GetName()), $"{index}: {control.GetType().Name}");
                        if (control is SettingsModifierPicker modifierPicker) {
                            Assert.False(control.IsTabStop);
                            Assert.True(Assert.IsType<System.Windows.Controls.Primitives.ToggleButton>(
                                Assert.IsType<System.Windows.Controls.Grid>(modifierPicker.Content).Children[0]).IsTabStop);
                        } else if (control is not TextBox { IsReadOnly: true }) { Assert.True(control.IsTabStop); }
                        var id = AutomationProperties.GetAutomationId(control);
                        if (control is Button move && (id.EndsWith(".up", StringComparison.Ordinal) || id.EndsWith(".down", StringComparison.Ordinal))) {
                            var icon = Assert.IsType<System.Windows.Shapes.Path>(move.Content);
                            Assert.NotNull(icon.Data);
                            Assert.Equal(36, move.Width);
                            Assert.Equal(id.EndsWith(".up", StringComparison.Ordinal) ? "Move up" : "Move down", move.ToolTip);
                            Assert.True(System.Windows.Data.BindingOperations.IsDataBound(icon, System.Windows.Shapes.Shape.StrokeProperty));
                        }
                    }
                    // Logical WPF bounds only. These are not native 100/150/200% DPI evidence.
                    foreach (var width in LogicalWidths) {
                        content.Measure(new Size(width, 700));
                        content.Arrange(new Rect(0, 0, width, 700));
                        content.UpdateLayout();
                        var footer = save.TransformToAncestor(content).TransformBounds(new Rect(save.RenderSize));
                        Assert.True(footer.Right <= width + 0.1 && footer.Bottom <= 700.1);
                        var closeBounds = close.TransformToAncestor(content).TransformBounds(new Rect(close.RenderSize));
                        Assert.True(closeBounds.Left >= footer.Right && closeBounds.Right <= width + 0.1 &&
                            closeBounds.Bottom <= 700.1, $"width {width}: Close button clipped or overlapping Save");
                        foreach (var control in elements.OfType<Control>().Where(item =>
                                     item is Button or TextBox or ComboBox or CheckBox or SettingsModifierPicker && page.IsAncestorOf(item))) {
                            var bounds = control.TransformToAncestor(page).TransformBounds(new Rect(control.RenderSize));
                            Assert.True(bounds.Right <= page.ActualWidth + 0.1, $"{index} width {width}: {control.GetType().Name} clipped");
                        }
                        foreach (var label in elements.OfType<Label>()) {
                            if (label.Target is not FrameworkElement target ||
                                label.Parent is not System.Windows.Controls.Grid row) { continue; }
                            var captionBounds = label.TransformToAncestor(row).TransformBounds(new Rect(label.RenderSize));
                            var targetBounds = target.TransformToAncestor(row).TransformBounds(new Rect(target.RenderSize));
                            Assert.True(Math.Abs(CenterY(captionBounds) - CenterY(targetBounds)) <= 0.5,
                                $"{index} width {width}: {label.Content} label is not centered with its field");
                            if (target.Parent is WrapPanel inputs) {
                                foreach (var input in inputs.Children.OfType<Control>()) {
                                    var inputBounds = input.TransformToAncestor(row).TransformBounds(new Rect(input.RenderSize));
                                    Assert.True(Math.Abs(CenterY(inputBounds) - CenterY(targetBounds)) <= 0.5,
                                        $"{index} width {width}: {label.Content} picker and capture button are not centered");
                                }
                            }
                            Assert.Equal(VerticalAlignment.Center, Assert.IsAssignableFrom<Control>(target).VerticalContentAlignment);
                        }
                    }
                    foreach (var advanced in elements.OfType<Expander>()) { advanced.IsExpanded = false; }
                }
                var status = Assert.IsType<TextBlock>(window.FindName("Status"));
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
                Assert.Equal(status.Text, AutomationProperties.GetName(status));
                window.Close();
            } catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF layout check timed out.");
        try { if (failure is not null) { throw new Xunit.Sdk.XunitException(failure.ToString()); } } finally {
            foreach (var file in Directory.GetFiles(root)) { File.Delete(file); }
            Directory.Delete(root);
        }
    }

    private static double CenterY(Rect bounds) => bounds.Top + bounds.Height / 2;

    private static IEnumerable<FrameworkElement> Descendants(DependencyObject root) {
        var seen = new HashSet<DependencyObject>();
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.Count > 0) {
            var next = pending.Pop();
            if (!seen.Add(next)) { continue; }
            if (next is FrameworkElement element) { yield return element; }
            foreach (var child in LogicalTreeHelper.GetChildren(next).OfType<DependencyObject>()) { pending.Push(child); }
            if (next is Visual or System.Windows.Media.Media3D.Visual3D) {
                for (var index = 0; index < VisualTreeHelper.GetChildrenCount(next); index++) { pending.Push(VisualTreeHelper.GetChild(next, index)); }
            }
        }
    }
}
