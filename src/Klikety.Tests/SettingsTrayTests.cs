using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

using Klikety;

namespace Klikety.Tests;

[Collection("Settings UI")]
public sealed class SettingsTrayTests {
    [Fact]
    public void TraySettingsItemIsEnabledNamedAndInvokesTheSuppliedApplicationActionOnEveryClick() {
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                var opened = 0;
                var item = App.CreateSettingsMenuItem(() => opened++);
                var menu = new ContextMenu();
                menu.Items.Add(item);
                Assert.Equal("Settings...", item.Header);
                Assert.True(item.IsEnabled);
                Assert.Equal("Settings...", UIElementAutomationPeer.CreatePeerForElement(item)?.GetName());
                Assert.Equal(0, opened);
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.Equal(2, opened);
                Assert.Single(menu.Items);
            } catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Tray Settings command check timed out.");
        if (failure is not null) { throw new Xunit.Sdk.XunitException(failure.ToString()); }
    }
}
