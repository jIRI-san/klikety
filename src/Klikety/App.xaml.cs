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
    private ScrollHotKeyService? _scrollHotKeyService;
    private bool _scrollHotKeysConfigEnabled;
    private NavigatorCoordinator? _coordinator;
    private ConfigModel? _config;
    private MacroStore? _macroStore;
    private MacrosFile? _macrosFile;
    private MacroHotKeyService? _macroHotKeyService;
    private MacroPickerOverlay? _macroPickerOverlay;

    // Key press visualization state (runtime-only, never persisted)
    private KeyboardHookService? _keyPressHook;
    private KeyPressProcessor? _keyPressProcessor;
    private KeyPressWindow? _keyPressWindow;
    private KeyPressDisplayManager? _keyPressDisplayManager;

#if DEBUG
    private HotKeyService? _debugHotKeyService;
    private OverlayWindow? _debugOverlay;
#endif
    private ILoggerFactory? _loggerFactory;
    private bool _hasBlockingViolations;

    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        // First-run: extract default config, schemas, and themes
        FirstRunExtractor.EnsureDefaults();

        // Create hotkey service (reused across resets)
        _hotKeyService = new HotKeyService();

        // Bootstrap coordinator from config
        var violations = BootstrapCoordinator();

        // Setup tray icon
        SetupTrayIcon(violations, _loggerFactory!.CreateLogger<App>());
    }

    /// <summary>
    /// Creates (or re-creates) the coordinator from current config on disk.
    /// Returns the list of violations for tray notification.
    /// </summary>
    private List<string> BootstrapCoordinator() {
        // Load config
        var configResult = ConfigLoader.Load();
        var config = configResult.Config;
        _config = config;

        // Logging — dispose previous if re-bootstrapping
        _loggerFactory?.Dispose();
        _loggerFactory = LoggingSetup.CreateLoggerFactory(config.LogLevel, config.FileLoggingEnabled, config.RetainedLogFileCount);
        var logger = _loggerFactory.CreateLogger<App>();

        // Probe hotkey for conflicts
        var hotKeyViolation = StartupValidator.ProbeHotKey(config.HotKey);

        // Collect all violations
        var violations = new List<string>(configResult.Violations);
        if (hotKeyViolation is not null) {
            violations.Add(hotKeyViolation);
        }

        // Check for blocking violations (migration errors)
        _hasBlockingViolations = configResult.Violations.Any(v =>
            v.Contains("newer than supported") ||
            v.Contains("could not be parsed") ||
            v.Contains("Failed to write") ||
            v.Contains("Cannot read config") ||
            v.Contains("not a JSON object"));

        if (_hasBlockingViolations) {
            return violations;
        }

        // Load theme
        var (theme, themeWarning) = ThemeLoader.Load(config.Theme);
        if (themeWarning is not null) {
            violations.Add(themeWarning);
        }

        // Create services
        var hookService = new KeyboardHookService();
        var mouseService = new MouseActionService(logger);

        // Create overlay window
        var overlayWindow = new OverlayWindow();
        overlayWindow.SetTheme(theme);

        // Create label generator
        var resolver = new Win32KeyLabelResolver();
        var labelGenerator = new LabelGenerator(config.HorizontalKeys, config.VerticalKeys, resolver);
        var gridRenderer = new GridRenderer(overlayWindow.Canvas, theme, labelGenerator, config.MinLabelFontSize);

        // Create crosshair renderer (only if mode is enabled)
        CrosshairRenderer? crosshairRenderer = null;
        var crosshairMode = config.Modes.Crosshair;
        if (crosshairMode is { Enabled: true }) {
            var horizLabels = new AxisLabelGenerator(config.HorizontalKeys, resolver);
            var vertLabels = new AxisLabelGenerator(config.VerticalKeys, resolver);
            crosshairRenderer = new CrosshairRenderer(overlayWindow.Canvas, theme, horizLabels, vertLabels, config.MinLabelFontSize);
        }

        // Create log-crosshair renderer (only if mode is enabled)
        LogCrosshairRenderer? logCrosshairRenderer = null;
        var logCrosshairMode = config.Modes.LogCrosshair;
        if (logCrosshairMode is { Enabled: true }) {
            var horizLabels = new AxisLabelGenerator(config.HorizontalKeys, resolver);
            var vertLabels = new AxisLabelGenerator(config.VerticalKeys, resolver);
            logCrosshairRenderer = new LogCrosshairRenderer(overlayWindow.Canvas, theme, horizLabels, vertLabels, config.MinLabelFontSize);
        }

        // Create log-grid renderer (only if mode is enabled)
        LogGridRenderer? logGridRenderer = null;
        var logGridMode = config.Modes.LogGrid;
        if (logGridMode is { Enabled: true }) {
            var horizLabels = new AxisLabelGenerator(config.HorizontalKeys, resolver);
            var vertLabels = new AxisLabelGenerator(config.VerticalKeys, resolver);
            logGridRenderer = new LogGridRenderer(overlayWindow.Canvas, theme, horizLabels, vertLabels, config.MinLabelFontSize);
        }

        // Create action mapper and session factory
        var actionMapper = new ActionMapper(config.ActionBindings);
        var sessionFactory = new ModeSessionFactory(config, actionMapper, gridRenderer, crosshairRenderer, logCrosshairRenderer, logGridRenderer);

        // LogGrid key-policy warning (trim or unavailability)
        if (sessionFactory.LogGridKeyPolicyWarning is { } logGridWarning) {
            violations.Add(logGridWarning);
        }

        // Load macros
        _macroStore = new MacroStore();
        var macroResult = _macroStore.Load();
        _macrosFile = macroResult.File;
        foreach (var macroError in macroResult.Errors) {
            violations.Add(macroError);
        }

        // Create coordinator
        _coordinator = new NavigatorCoordinator(
            _hotKeyService!,
            hookService,
            mouseService,
            overlayWindow,
            sessionFactory,
            PlatformServices.Instance,
            new ModifierDetector(),
            config,
            logger,
            _macroStore,
            _macrosFile);

        // Register hotkey
        _hotKeyService!.Unregister();
        if (!_hotKeyService.Register(config.HotKey)) {
            LogHotkeyRegistrationFailed(logger, config.HotKey.Modifiers, config.HotKey.Key);
            violations.Add($"Failed to register global hotkey {config.HotKey.Modifiers}+{config.HotKey.Key}.");
        }

        // Scroll hotkeys
        _scrollHotKeyService?.Dispose();
        _scrollHotKeyService = new ScrollHotKeyService(config.ScrollHotKeys, mouseService, logger);
        _scrollHotKeysConfigEnabled = config.ScrollHotKeys.Enabled;
        if (_scrollHotKeysConfigEnabled) {
            var scrollFailures = _scrollHotKeyService.Register();
            violations.AddRange(scrollFailures);
        }

        // Macro hotkey + picker
        _macroHotKeyService?.Dispose();
        _macroPickerOverlay ??= new MacroPickerOverlay();
        if (config.Macros.Enabled && config.Macros.GlobalHotKey is { } macroHotKey) {
            _macroHotKeyService = new MacroHotKeyService(macroHotKey, logger);
            var macroFailure = _macroHotKeyService.Register();
            if (macroFailure is not null) {
                violations.Add(macroFailure);
            }
        }

        _coordinator.MacroHotKeyService = _macroHotKeyService;
        _coordinator.MacroPickerWindow = _macroPickerOverlay;
        _coordinator.MacroPlaybackWindow = new MacroPlaybackOverlay();

#if DEBUG
        SetupDebugLogGridSession(config, theme, resolver);
#endif

        return violations;
    }

    private void SetupTrayIcon(List<string> violations, ILogger logger) {
        var iconUri = new Uri("pack://application:,,,/Resources/klikety.ico", UriKind.Absolute);
        using var iconStream = Application.GetResourceStream(iconUri)?.Stream;

        _trayIcon = new TaskbarIcon {
            ToolTipText = "Klikety",
            Icon = iconStream is not null ? new System.Drawing.Icon(iconStream) : null,
        };
        _trayIcon.ForceCreate();

        SetupTrayContextMenu(violations, logger);

        // Show violations as tray notification
        if (violations.Count > 0) {
            var message = string.Join("\n", violations);
            LogStartupViolations(logger, message);
            _trayIcon.ShowNotification("Klikety — Configuration Issues", message);
        }
    }

    private void SetupTrayContextMenu(List<string> violations, ILogger logger) {
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

        // Reset Configuration (visible only when config has blocking violations)
        if (_hasBlockingViolations) {
            var resetItem = new System.Windows.Controls.MenuItem { Header = "Reset Configuration" };
            resetItem.Click += (_, _) => {
                var error = ConfigResetter.ResetToDefaults();
                if (error is not null) {
                    _trayIcon?.ShowNotification("Klikety — Reset Failed", error);
                    return;
                }

                // Re-bootstrap: dispose old coordinator, re-create
                _coordinator?.Dispose();
                _coordinator = null;

                var newViolations = BootstrapCoordinator();

                // Rebuild tray menu to reflect new state
                SetupTrayContextMenu(newViolations, logger);

                if (newViolations.Count > 0) {
                    var msg = string.Join("\n", newViolations);
                    _trayIcon?.ShowNotification("Klikety — Configuration Issues", msg);
                } else {
                    _trayIcon?.ShowNotification("Klikety", "Configuration reset to defaults.");
                }
            };
            contextMenu.Items.Add(resetItem);
        }

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

        // Show Key Presses (runtime toggle, always off on startup)
        var keyPressItem = new System.Windows.Controls.MenuItem {
            Header = "Show Key Presses",
            IsChecked = false,
        };
        keyPressItem.Click += (_, _) => {
            if (keyPressItem.IsChecked) {
                DisableKeyPressVisualization();
                keyPressItem.IsChecked = false;
                _trayIcon!.ToolTipText = "Klikety";
            } else {
                if (EnableKeyPressVisualization()) {
                    keyPressItem.IsChecked = true;
                    _trayIcon!.ToolTipText = "Klikety (Key Display Active)";
                }
            }
        };
        contextMenu.Items.Add(keyPressItem);

        // Scroll Keys toggle (visible when scroll hotkeys are enabled in config)
        if (_scrollHotKeyService is not null && _scrollHotKeysConfigEnabled) {
            var scrollItem = new System.Windows.Controls.MenuItem {
                Header = _scrollHotKeyService.IsRegistered ? "Pause Scroll Keys" : "Resume Scroll Keys",
            };
            scrollItem.Click += (_, _) => {
                if (_scrollHotKeyService.IsRegistered) {
                    _scrollHotKeyService.Unregister();
                    scrollItem.Header = "Resume Scroll Keys";
                } else {
                    var failures = _scrollHotKeyService.Register();
                    if (failures.Count > 0) {
                        _trayIcon?.ShowNotification("Klikety", string.Join("\n", failures));
                    }
                    scrollItem.Header = "Pause Scroll Keys";
                }
            };
            contextMenu.Items.Add(scrollItem);
        }

        // Macro status (info only)
        if (_config?.Macros.Enabled == true && _macrosFile is not null) {
            var defined = _macrosFile.Macros.Count(m => m is not null);
            var macroInfoItem = new System.Windows.Controls.MenuItem {
                Header = $"Macros: {defined}/10 defined",
                IsEnabled = false,
            };
            contextMenu.Items.Add(macroInfoItem);
        }

        contextMenu.Items.Add(new System.Windows.Controls.Separator());

        // Quit
        var quitItem = new System.Windows.Controls.MenuItem { Header = "Quit" };
        quitItem.Click += (_, _) => {
            DisableKeyPressVisualization();
            _coordinator?.Dispose();
            _hotKeyService?.Dispose();
            _scrollHotKeyService?.Dispose();
            _macroHotKeyService?.Dispose();
            _trayIcon?.Dispose();
            _loggerFactory?.Dispose();
            Shutdown();
        };
        contextMenu.Items.Add(quitItem);

        _trayIcon!.ContextMenu = contextMenu;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to register global hotkey {Modifiers}+{Key}")]
    private static partial void LogHotkeyRegistrationFailed(ILogger logger, HotKeyModifiers modifiers, Input.VKey key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Startup violations: {Message}")]
    private static partial void LogStartupViolations(ILogger logger, string message);

    /// <summary>
    /// Activation transaction: creates all key press visualization resources and enables the hook.
    /// Returns false (and cleans up) if the hook fails to install.
    /// </summary>
    private bool EnableKeyPressVisualization() {
        var vizConfig = _config!.KeyPressVisualization;

        var hook = new KeyboardHookService();
        var resolver = new Win32KeyLabelResolver();
        var processor = new KeyPressProcessor(resolver, PlatformServices.Instance.KeyState, TimeProvider.System);
        var window = new KeyPressWindow(vizConfig);
        var monitorService = new MonitorService(window);
        var displayManager = new KeyPressDisplayManager(vizConfig, processor, window, monitorService);

        if (!hook.Enable()) {
            displayManager.Dispose();
            window.Close();
            _trayIcon?.ShowNotification("Klikety", "Failed to enable key press display.");
            return false;
        }

        hook.KeyEvent += (_, e) => displayManager.HandleKeyEvent(e);
        window.Show();

        _keyPressHook = hook;
        _keyPressProcessor = processor;
        _keyPressWindow = window;
        _keyPressDisplayManager = displayManager;
        return true;
    }

    /// <summary>
    /// Disables and disposes all key press visualization resources. Safe to call when already disabled.
    /// </summary>
    private void DisableKeyPressVisualization() {
        if (_keyPressHook is null) {
            return;
        }

        _keyPressHook.Disable();
        _keyPressProcessor?.ResetModifierState();
        _keyPressDisplayManager?.Dispose();
        _keyPressWindow?.Close();

        _keyPressHook = null;
        _keyPressProcessor = null;
        _keyPressWindow = null;
        _keyPressDisplayManager = null;
    }

#if DEBUG
    private void SetupDebugLogGridSession(ConfigModel config, Config.ThemeModel theme, Win32KeyLabelResolver resolver) {
        _debugHotKeyService?.Dispose();
        _debugOverlay?.Close();

        _debugOverlay = new OverlayWindow();

        var colLabels = new AxisLabelGenerator(config.HorizontalKeys, resolver);
        var rowLabels = new AxisLabelGenerator(config.VerticalKeys, resolver);
        var logGridRenderer = new LogGridRenderer(
            _debugOverlay.Canvas, theme, colLabels, rowLabels, config.MinLabelFontSize);

        var session = new Navigation.DebugLogGridSession(
            logGridRenderer,
            PlatformServices.Instance.Cursor,
            PlatformServices.Instance.Screen);

        session.Cancelled += () => _debugOverlay?.Hide();

        _debugHotKeyService = new HotKeyService();
        _debugHotKeyService.Activated += (_, _) => {
            var screenBounds = PlatformServices.Instance.Screen.GetPrimaryScreenBounds();
            var cursor = PlatformServices.Instance.Cursor.GetCursorPosition();
            _debugOverlay!.Show();
            session.Activate(screenBounds, cursor);
        };

        // Ctrl+Shift+G — dedicated debug hotkey for log-grid visual test
        _debugHotKeyService.Register(new HotKeyConfig {
            Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Shift,
            Key = Input.VKey.G,
        });
    }
#endif
}

