using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

internal static class UiaFixture {
    internal static void Run() {
        var thread = new Thread(() => {
            var panel = new StackPanel();
            var button = new Button { Content = new TextBlock { Text = "Nested button text" }, Height = 40 };
            var buttonLayer = new Grid { Height = 40 };
            buttonLayer.Children.Add(button);
            panel.Children.Add(buttonLayer);
            panel.Children.Add(new TextBox { Text = "Private text never transmitted", Height = 40 });
            panel.Children.Add(new TextBox { Text = "Readonly content", IsReadOnly = true, Height = 40 });
            panel.Children.Add(new CheckBox { Content = "Toggle", Height = 40 });
            panel.Children.Add(new Button { Content = "Disabled", IsEnabled = false, Height = 40 });
            panel.Children.Add(new TextBlock { Text = "Passive text", Height = 40 });
            var window = new Window {
                Title = "Klikety controlled UIA fixture", Left = 100, Top = 100,
                Width = 600, Height = 400, Content = panel, ShowActivated = true, WindowStyle = WindowStyle.None
            };
            Window? other = null;
            bool readyReported = false;
            window.ContentRendered += (_, _) => {
                if (readyReported) { return; }
                readyReported = true;
                Console.WriteLine(new WindowInteropHelper(window).Handle.ToInt64());
                Console.Out.Flush();
                Console.Error.WriteLine($"fixture-ready active={window.IsActive} foreground={GetForegroundWindow().ToInt64()}");
            };
            window.Show();
            _ = Task.Run(() => {
                string? command;
                while ((command = Console.ReadLine()) is not null) {
                    window.Dispatcher.Invoke(() => {
                        switch (command) {
                            case "focus":
                                bool activated = window.Activate();
                                bool accepted = SetForegroundWindow(new WindowInteropHelper(window).Handle);
                                Console.WriteLine($"focus active={activated} accepted={accepted} foreground={GetForegroundWindow().ToInt64()}");
                                Console.Out.Flush();
                                return;
                            case "move": window.Left += 10; break;
                            case "move-control": button.Margin = new Thickness(20, 0, 0, 0); break;
                            case "disable": button.IsEnabled = false; break;
                            case "replace": buttonLayer.Children.Remove(button); buttonLayer.Children.Insert(0, new Button { Content = "Replacement", Height = 40 }); break;
                            case "cover-center":
                                buttonLayer.Children.Add(new Button {
                                    Content = "Center blocker", Width = 120, Height = 20,
                                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                                });
                                break;
                            case "cover-all": buttonLayer.Children.Add(new Button { Content = "Full blocker" }); break;
                            case "other-window":
                                other = new Window {
                                    Left = 800, Top = 100, Width = 300, Height = 200,
                                    WindowStyle = WindowStyle.None, Content = new Button { Content = "Other root" }
                                };
                                other.Show(); other.Activate();
                                break;
                            case "close": window.Close(); break;
                        }
                        window.UpdateLayout();
                    });
                    if (command != "focus") { Console.WriteLine("done"); Console.Out.Flush(); }
                }
                if (other is not null) { window.Dispatcher.Invoke(other.Close); }
                window.Dispatcher.InvokeShutdown();
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
}
