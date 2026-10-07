using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

internal static class UiaFixture {
    internal static void Run() {
        var thread = new Thread(() => {
            var panel = new StackPanel();
            var button = new Button { Content = new TextBlock { Text = "Nested button text" }, Height = 40 };
            panel.Children.Add(button);
            panel.Children.Add(new TextBox { Text = "Private text never transmitted", Height = 40 });
            panel.Children.Add(new TextBox { Text = "Readonly content", IsReadOnly = true, Height = 40 });
            panel.Children.Add(new CheckBox { Content = "Toggle", Height = 40 });
            panel.Children.Add(new Button { Content = "Disabled", IsEnabled = false, Height = 40 });
            panel.Children.Add(new TextBlock { Text = "Passive text", Height = 40 });
            var window = new Window {
                Title = "Klikety controlled UIA fixture", Left = 100, Top = 100,
                Width = 600, Height = 400, Content = panel, ShowActivated = true, WindowStyle = WindowStyle.None
            };
            window.Show();
            Console.WriteLine(new WindowInteropHelper(window).Handle.ToInt64());
            Console.Out.Flush();
            _ = Task.Run(() => {
                string? command;
                while ((command = Console.ReadLine()) is not null) {
                    window.Dispatcher.Invoke(() => {
                        switch (command) {
                            case "move": window.Left += 10; break;
                            case "disable": button.IsEnabled = false; break;
                            case "replace": panel.Children.Remove(button); panel.Children.Insert(0, new Button { Content = "Replacement", Height = 40 }); break;
                            case "close": window.Close(); break;
                        }
                    });
                    Console.WriteLine("done"); Console.Out.Flush();
                }
                window.Dispatcher.InvokeShutdown();
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
    }
}
