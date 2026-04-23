using System.Windows;

namespace Klikety;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // TODO (Phase 7): bootstrap tray icon and hotkey service.
        // Until then, shut down immediately so the process doesn't become a zombie.
#if DEBUG
        Shutdown();
#endif
    }
}

