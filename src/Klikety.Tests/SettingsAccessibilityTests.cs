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
        File.WriteAllText(path, "{\"configVersion\":7}");
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                var window = new SettingsWindow(path, true, _ => { },
                    () => new(new ConfigModel(), false, false), _ => SettingsApplyOutcome.Success,
                    _ => SettingsApplyOutcome.Success, confirmDiscard: _ => true);
                var categories = Assert.IsType<ListBox>(window.FindName("Categories"));
                var host = Assert.IsType<ContentControl>(window.FindName("PageHost"));
                var content = Assert.IsType<System.Windows.Controls.Grid>(window.Content);
                var save = Assert.IsType<Button>(window.FindName("SaveButton"));
                for (var index = 0; index < 7; index++) {
                    categories.SelectedIndex = index;
                    var page = Assert.IsType<StackPanel>(host.Content);
                    var elements = Descendants(page).ToArray();
                    foreach (var advanced in elements.OfType<Expander>()) {
                        Assert.False(advanced.IsExpanded);
                        advanced.IsExpanded = true;
                    }
                    foreach (var control in elements.OfType<Control>().Where(item => item is Button or TextBox or ComboBox or CheckBox)) {
                        var peer = UIElementAutomationPeer.CreatePeerForElement(control);
                        Assert.False(string.IsNullOrWhiteSpace(peer?.GetName()), $"{index}: {control.GetType().Name}");
                        if (control is not TextBox { IsReadOnly: true }) { Assert.True(control.IsTabStop); }
                    }
                    // Logical WPF bounds only. These are not native 100/150/200% DPI evidence.
                    foreach (var width in LogicalWidths) {
                        content.Measure(new Size(width, 700));
                        content.Arrange(new Rect(0, 0, width, 700));
                        content.UpdateLayout();
                        var footer = save.TransformToAncestor(content).TransformBounds(new Rect(save.RenderSize));
                        Assert.True(footer.Right <= width + 0.1 && footer.Bottom <= 700.1);
                        foreach (var control in elements.OfType<Control>().Where(item => item is Button or TextBox or ComboBox or CheckBox)) {
                            var bounds = control.TransformToAncestor(page).TransformBounds(new Rect(control.RenderSize));
                            Assert.True(bounds.Right <= page.ActualWidth + 0.1, $"{index} width {width}: {control.GetType().Name} clipped");
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
        try { if (failure is not null) { throw new Xunit.Sdk.XunitException(failure.ToString()); } }
        finally {
            foreach (var file in Directory.GetFiles(root)) { File.Delete(file); }
            Directory.Delete(root);
        }
    }

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
