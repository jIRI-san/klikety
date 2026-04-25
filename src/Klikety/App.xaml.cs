using System.Diagnostics;
using System.IO;
using System.Windows;

using H.NotifyIcon;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Navigation;
using Klikety.Overlay;
using Klikety.Services;

using Microsoft.Extensions.Logging;

namespace Klikety;

public partial class App : Application {
    private TaskbarIcon? _trayIcon;
    private HotKeyService? _hotKeyService;
    private NavigatorCoordinator? _coordinator;
    private ILoggerFactory? _loggerFactory;

    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        // First-run: extract default config, schemas, and themes
        FirstRunExtractor.EnsureDefaults();

        // Load config
        var configResult = ConfigLoader.Load();
        var config = configResult.Config;

        // Logging
        _loggerFactory = LoggingSetup.CreateLoggerFactory(config.LogLevel, config.FileLoggingEnabled, config.RetainedLogFileCount);
        var logger = _loggerFactory.CreateLogger<App>();

        // Probe hotkey for conflicts
        var hotKeyViolation = StartupValidator.ProbeHotKey(config.HotKey);

        // Collect all violations
        var violations = new List<string>(configResult.Violations);
        if (hotKeyViolation is not null) {
            violations.Add(hotKeyViolation);
        }

        // Load theme
        var (theme, themeWarning) = ThemeLoader.Load(config.Theme);
        if (themeWarning is not null) {
            violations.Add(themeWarning);
        }

        // Create services
        _hotKeyService = new HotKeyService();
        var hookService = new KeyboardHookService();
        var mouseService = new MouseActionService();

        // Create overlay window
        var overlayWindow = new OverlayWindow();

        // Create label generator
        var labelGenerator = new LabelGenerator(config.FirstKeys, config.SecondKeys);
        var gridRenderer = new GridRenderer(overlayWindow.Canvas, theme, labelGenerator, config.MinLabelFontSize);

        // Create state machine
        var actionMapper = new ActionMapper(config.ActionBindings);
        var stateMachine = new NavigatorStateMachine(
            config.FirstKeys,
            config.SecondKeys,
            actionMapper,
            config.NavigationMode,
            config.Level3CellSizeThreshold);

        // Create coordinator
        _coordinator = new NavigatorCoordinator(
            _hotKeyService,
            hookService,
            mouseService,
            overlayWindow,
            stateMachine,
            gridRenderer,
            config,
            logger);

        // Register hotkey
        if (!_hotKeyService.Register(config.HotKey)) {
            LogHotkeyRegistrationFailed(logger, config.HotKey.Modifiers, config.HotKey.Key);
            violations.Add($"Failed to register global hotkey {config.HotKey.Modifiers}+{config.HotKey.Key}.");
        }

        // Setup tray icon
        SetupTrayIcon(violations, logger);
    }

    private void SetupTrayIcon(List<string> violations, ILogger logger) {
        var iconUri = new Uri("pack://application:,,,/Resources/klikety.ico", UriKind.Absolute);
        var iconStream = Application.GetResourceStream(iconUri)?.Stream;

        _trayIcon = new TaskbarIcon {
            ToolTipText = "Klikety",
            Icon = iconStream is not null ? new System.Drawing.Icon(iconStream) : null,
        };

        var contextMenu = new System.Windows.Controls.ContextMenu();

        // About
        var aboutItem = new System.Windows.Controls.MenuItem { Header = "About" };
        aboutItem.Click += (_, _) => {
            var about = new AboutWindow();
            about.ShowDialog();
        };
        contextMenu.Items.Add(aboutItem);

        // Open Configuration Folder
        var configFolderItem = new System.Windows.Controls.MenuItem { Header = "Open Configuration Folder" };
        configFolderItem.Click += (_, _) => {
            var configFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Klikety");
            Process.Start("explorer.exe", configFolder);
        };
        contextMenu.Items.Add(configFolderItem);

        contextMenu.Items.Add(new System.Windows.Controls.Separator());

        // Start with Windows
        var startupItem = new System.Windows.Controls.MenuItem {
            Header = "Start with Windows",
            IsChecked = StartupRegistryService.IsEnabled(),
        };
        startupItem.Click += (_, _) => {
            if (StartupRegistryService.IsEnabled()) {
                StartupRegistryService.Disable();
                startupItem.IsChecked = false;
            } else {
                StartupRegistryService.Enable();
                startupItem.IsChecked = true;
            }
        };
        contextMenu.Items.Add(startupItem);

        contextMenu.Items.Add(new System.Windows.Controls.Separator());

        // Quit
        var quitItem = new System.Windows.Controls.MenuItem { Header = "Quit" };
        quitItem.Click += (_, _) => {
            _coordinator?.DeactivateOverlay();
            _hotKeyService?.Dispose();
            _trayIcon?.Dispose();
            _loggerFactory?.Dispose();
            Shutdown();
        };
        contextMenu.Items.Add(quitItem);

        _trayIcon.ContextMenu = contextMenu;

        // Show violations as tray notification
        if (violations.Count > 0) {
            var message = string.Join("\n", violations);
            LogStartupViolations(logger, message);
            _trayIcon.ShowNotification("Klikety — Configuration Issues", message);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to register global hotkey {Modifiers}+{Key}")]
    private static partial void LogHotkeyRegistrationFailed(ILogger logger, HotKeyModifiers modifiers, Input.VKey key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Startup violations: {Message}")]
    private static partial void LogStartupViolations(ILogger logger, string message);
}

