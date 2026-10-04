using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private readonly List<ILoggerFactory> _retainedLoggerFactories = [];
    private readonly List<string> _bootstrapActivationErrors = [];
    private KeyboardHookService? _pendingBootstrapHook;
    private OverlayWindow? _pendingBootstrapOverlay;
    private bool _hasBlockingViolations;
    private SettingsWindow? _settingsWindow;
    private string? _demoConfigPath;
    private string? _runtimeFixtureRoot;
    private AppPaths _paths = AppPaths.User;
    private static string UserConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Klikety", "config.json");

    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        if (e.Args.Contains("--settings-runtime-fixture")) {
            try {
                var index = Array.IndexOf(e.Args, "--settings-runtime-fixture");
                if (index + 1 >= e.Args.Length) {
                    throw new InvalidDataException("--settings-runtime-fixture requires an absolute fixture directory.");
                }
                var root = e.Args[index + 1];
                if (!Path.IsPathFullyQualified(root)) {
                    throw new InvalidDataException("Runtime fixture directory must be absolute.");
                }
                _runtimeFixtureRoot = Path.GetFullPath(root);
                _paths = new AppPaths(_runtimeFixtureRoot);
                var userFolder = Path.GetDirectoryName(UserConfigPath)!;
                var fullUserFolder = Path.GetFullPath(userFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (_paths.Root.StartsWith(fullUserFolder, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(_paths.Root, fullUserFolder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) {
                    throw new InvalidDataException("Runtime fixture refuses the real Klikety AppData directory.");
                }
                EnsureFixturePathHasNoReparsePoints(_paths.Root);
                Directory.CreateDirectory(_paths.Root);
                EnsureFixturePathHasNoReparsePoints(_paths.Root);
                CreateDemoFixture(_paths.ConfigPath);
                ConfigureRuntimeFixture(_paths.ConfigPath);
                _hotKeyService = new HotKeyService();
                var runtimeViolations = BootstrapCoordinatorSafely();
                SetupTrayIcon(runtimeViolations, _loggerFactory!.CreateLogger<App>());
                _trayIcon!.ToolTipText = "Klikety - ISOLATED RUNTIME FIXTURE";
                return;
            } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException) {
                MessageBox.Show("Cannot start isolated runtime fixture: " + ex.Message,
                    "Klikety", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }
        }

        if (e.Args.Contains("--settings-demo")) {
            try {
                var index = Array.IndexOf(e.Args, "--settings-demo");
                if (index + 1 >= e.Args.Length) {
                    throw new InvalidDataException("--settings-demo requires an absolute fixture config path.");
                }
                var path = e.Args[index + 1];
                if (!Path.IsPathFullyQualified(path)) {
                    throw new InvalidDataException("Demo config path must be absolute.");
                }
                _demoConfigPath = Path.GetFullPath(path);
                _paths = new AppPaths(Path.GetDirectoryName(_demoConfigPath)!);
                var userFolder = Path.GetDirectoryName(UserConfigPath)!;
                if (_demoConfigPath.StartsWith(userFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
                    throw new InvalidDataException("Demo mode refuses the real Klikety AppData directory.");
                }
                CreateDemoFixture(_demoConfigPath);
                _loggerFactory = LoggingSetup.CreateLoggerFactory("Warning", false, 7, _paths.LogsFolder);
                SetupTrayIcon([], _loggerFactory.CreateLogger<App>());
                _trayIcon!.ToolTipText = "Klikety Settings - ISOLATED DEMO";
                ShowSettings();
                return;
            } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException) {
                MessageBox.Show("Cannot start isolated settings demo: " + ex.Message, "Klikety", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }
        }

        // First-run: extract default config, schemas, and themes
        _paths = AppPaths.User;
        FirstRunExtractor.EnsureDefaults(_paths.Root);

        // Create hotkey service (reused across resets)
        _hotKeyService = new HotKeyService();

        // Bootstrap coordinator from config
        var violations = BootstrapCoordinatorSafely();

        // Setup tray icon
        SetupTrayIcon(violations, _loggerFactory!.CreateLogger<App>());
    }

    /// <summary>
    /// Creates (or re-creates) the coordinator from current config on disk.
    /// Returns the list of violations for tray notification.
    /// </summary>
    private List<string> BootstrapCoordinator(ConfigModel? capturedConfig = null) {
        _bootstrapActivationErrors.Clear();
        // Load config
        var configResult = capturedConfig is null
            ? ConfigLoader.Load(_paths.ConfigPath)
            : new ConfigLoadResult { Config = capturedConfig };
        var config = configResult.Config;
        _config = config;

        // Keep the previous factory alive until the replacement runtime is known to work.
        _loggerFactory = LoggingSetup.CreateLoggerFactory(
            config.LogLevel, config.FileLoggingEnabled, config.RetainedLogFileCount, _paths.LogsFolder);
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
            v.Contains("not a JSON object") ||
            (v.StartsWith("macros.playbackIndicator.", StringComparison.Ordinal) &&
             !v.Contains("must be at least 1 DIP", StringComparison.Ordinal) &&
             !v.Contains("must be at least 100 ms", StringComparison.Ordinal)));

        if (_hasBlockingViolations) {
            _bootstrapActivationErrors.AddRange(violations);
            return violations;
        }

        // Load theme
        var (theme, themeWarning) = ThemeLoader.Load(config.Theme, _paths.Root);
        if (themeWarning is not null) {
            violations.Add(themeWarning);
        }

        // Create services
        var hookService = new KeyboardHookService();
        _pendingBootstrapHook = hookService;
        var mouseService = new MouseActionService(logger);

        // Create overlay window
        var overlayWindow = new OverlayWindow();
        _pendingBootstrapOverlay = overlayWindow;
        overlayWindow.SetTheme(theme);
        overlayWindow.SetLogger(logger);

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
        _macroStore = new MacroStore(_paths.MacrosPath);
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
            _macrosFile,
            () => new SatelliteWindow(theme, logger),
            new DisplayTopologyStore(_paths.DisplayTopologyPath));
        _pendingBootstrapHook = null;
        _pendingBootstrapOverlay = null;

        // Register hotkey
        _hotKeyService!.Unregister();
        if (!_hotKeyService.Register(config.HotKey)) {
            LogHotkeyRegistrationFailed(logger, config.HotKey.Modifiers, config.HotKey.Key);
            var failure = $"Failed to register global hotkey {config.HotKey.Modifiers}+{config.HotKey.Key}.";
            violations.Add(failure);
            _bootstrapActivationErrors.Add(failure);
        }

        // Scroll hotkeys
        _scrollHotKeyService?.Dispose();
        _scrollHotKeyService = new ScrollHotKeyService(config.ScrollHotKeys, mouseService, logger);
        _scrollHotKeysConfigEnabled = config.ScrollHotKeys.Enabled;
        if (_scrollHotKeysConfigEnabled) {
            var scrollFailures = _scrollHotKeyService.Register();
            violations.AddRange(scrollFailures);
            _bootstrapActivationErrors.AddRange(scrollFailures);
        }

        // Macro hotkey + picker
        _macroHotKeyService?.Dispose();
        _macroPickerOverlay ??= new MacroPickerOverlay();
        if (config.Macros.Enabled && config.Macros.GlobalHotKey is { } macroHotKey) {
            _macroHotKeyService = new MacroHotKeyService(macroHotKey, logger);
            var macroFailure = _macroHotKeyService.Register();
            if (macroFailure is not null) {
                violations.Add(macroFailure);
                _bootstrapActivationErrors.Add(macroFailure);
            }
        }

        _coordinator.MacroHotKeyService = _macroHotKeyService;
        _coordinator.MacroPickerWindow = _macroPickerOverlay;
        _coordinator.MacroPlaybackWindow = new MacroPlaybackOverlay();
        _coordinator.ClickIndicator = new ClickIndicatorAdapter(
            new ClickIndicatorWindow(config.Macros.PlaybackIndicator));

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

        var settingsItem = new System.Windows.Controls.MenuItem { Header = "Settings..." };
        settingsItem.Click += (_, _) => ShowSettings();
        contextMenu.Items.Add(settingsItem);

        if (_demoConfigPath is not null) {
            var demoInfo = new System.Windows.Controls.MenuItem {
                Header = "ISOLATED DEMO - no navigation, hooks or registry changes", IsEnabled = false,
            };
            contextMenu.Items.Add(demoInfo);
            var demoQuit = new System.Windows.Controls.MenuItem { Header = "Quit demo" };
            demoQuit.Click += (_, _) => {
                _settingsWindow?.Close();
                if (_settingsWindow is null) {
                    _trayIcon?.Dispose();
                    DisposeLoggerFactories();
                    Shutdown();
                }
            };
            contextMenu.Items.Add(demoQuit);
            _trayIcon!.ContextMenu = contextMenu;
            return;
        }

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
            Process.Start("explorer.exe", _paths.Root);
        };
        contextMenu.Items.Add(configFolderItem);

        // Reload Configuration
        var reloadItem = new System.Windows.Controls.MenuItem { Header = "Reload Configuration" };
        reloadItem.Click += (_, _) => {
            List<string> newViolations;
            try {
                newViolations = ReloadConfiguration();
            } catch (InvalidOperationException ex) {
                _trayIcon?.ShowNotification("Klikety", ex.Message);
                return;
            }

            if (newViolations.Count > 0) {
                var msg = string.Join("\n", newViolations);
                _trayIcon?.ShowNotification("Klikety — Configuration Issues", msg);
            } else {
                _trayIcon?.ShowNotification("Klikety", "Configuration reloaded.");
            }
        };
        contextMenu.Items.Add(reloadItem);

        // Reset Configuration (visible only when config has blocking violations)
        if (_hasBlockingViolations) {
            var resetItem = new System.Windows.Controls.MenuItem { Header = "Reset Configuration" };
            resetItem.Click += (_, _) => {
                if (_coordinator is { IsIdle: false }) {
                    _trayIcon?.ShowNotification("Klikety", "Reset is only available while navigation and macro activity are idle.");
                    return;
                }
                var error = ConfigResetter.ResetToDefaults(_paths.ConfigPath);
                if (error is not null) {
                    _trayIcon?.ShowNotification("Klikety — Reset Failed", error);
                    return;
                }

                // Re-bootstrap: dispose old coordinator, re-create
                _coordinator?.Dispose();
                _coordinator = null;
                _hotKeyService?.Unregister();
                _scrollHotKeyService?.Dispose();
                _scrollHotKeyService = null;
                _macroHotKeyService?.Dispose();
                _macroHotKeyService = null;

                var newViolations = BootstrapCoordinatorSafely();

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

        if (_runtimeFixtureRoot is not null) {
            contextMenu.Items.Add(new System.Windows.Controls.MenuItem {
                Header = "ISOLATED RUNTIME FIXTURE - config, logs, macros, themes and topology use this folder",
                IsEnabled = false,
            });
        } else {
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
        }

        // Show Key Presses (runtime toggle, always off on startup)
        var keyPressItem = new System.Windows.Controls.MenuItem {
            Header = "Show Key Presses",
            IsChecked = _keyPressHook is not null,
        };
        keyPressItem.Click += (_, _) => {
            if (keyPressItem.IsChecked) {
                DisableKeyPressVisualization();
                keyPressItem.IsChecked = false;
                _trayIcon!.ToolTipText = _runtimeFixtureRoot is null ? "Klikety" : "Klikety - ISOLATED RUNTIME FIXTURE";
            } else {
                if (EnableKeyPressVisualization()) {
                    keyPressItem.IsChecked = true;
                    _trayIcon!.ToolTipText = _runtimeFixtureRoot is null
                        ? "Klikety (Key Display Active)"
                        : "Klikety - ISOLATED RUNTIME FIXTURE (Key Display Active)";
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
            _settingsWindow?.Close();
            if (_settingsWindow is not null) {
                return;
            }
            DisableKeyPressVisualization();
            _coordinator?.Dispose();
            _hotKeyService?.Dispose();
            _scrollHotKeyService?.Dispose();
            _macroHotKeyService?.Dispose();
            _trayIcon?.Dispose();
            DisposeLoggerFactories();
            Shutdown();
        };
        contextMenu.Items.Add(quitItem);

        _trayIcon!.ContextMenu = contextMenu;
    }

    private void DisposeLoggerFactories() {
        _loggerFactory?.Dispose();
        _loggerFactory = null;
        foreach (var loggerFactory in _retainedLoggerFactories) {
            loggerFactory.Dispose();
        }
        _retainedLoggerFactories.Clear();
    }

    private List<string> BootstrapCoordinatorSafely(ConfigModel? capturedConfig = null) {
        try {
            return BootstrapCoordinator(capturedConfig);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or
                                    Win32Exception or ArgumentException or NotSupportedException or JsonException) {
            _coordinator?.Dispose();
            _coordinator = null;
            _macroHotKeyService?.Dispose();
            _macroHotKeyService = null;
            _scrollHotKeyService?.Dispose();
            _scrollHotKeyService = null;
            _hotKeyService?.Unregister();
            _pendingBootstrapOverlay?.Close();
            _pendingBootstrapOverlay = null;
            _pendingBootstrapHook?.Dispose();
            _pendingBootstrapHook = null;
            _macroPickerOverlay?.Close();
            _macroPickerOverlay = null;
            _hasBlockingViolations = true;
            _bootstrapActivationErrors.Add(ex.Message);
            return [ex.Message];
        }
    }

    private void ShowSettings() {
        if (_settingsWindow is not null) {
            if (_settingsWindow.WindowState == WindowState.Minimized) {
                _settingsWindow.WindowState = WindowState.Normal;
            }
            _settingsWindow.Activate();
            return;
        }
        try {
            _settingsWindow = new SettingsWindow(_demoConfigPath ?? UserConfigPath, _demoConfigPath is not null,
                ValidateSettingsApply, CaptureSettingsRuntime, ApplySettings, RestoreSettingsRuntime);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException) {
            MessageBox.Show("Cannot open Settings. The config has not been changed.\n" + ex.Message,
                "Klikety Settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ValidateSettingsApply(ConfigModel config) {
        if (_demoConfigPath is null && _coordinator is { IsIdle: false }) {
            throw new InvalidOperationException("Settings can only be applied while navigation and macro activity are idle.");
        }
        var (_, warning) = ThemeLoader.Load(config.Theme, _paths.Root);
        if (warning is not null && !string.Equals(_config?.Theme, config.Theme, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidDataException(warning);
        }
        if (_demoConfigPath is null &&
            (_config is null || _config.HotKey.Key != config.HotKey.Key || _config.HotKey.Modifiers != config.HotKey.Modifiers)) {
            var failure = StartupValidator.ProbeHotKey(config.HotKey);
            if (failure is not null) {
                throw new InvalidDataException(failure);
            }
        }
    }

    private SettingsRuntimeSnapshot CaptureSettingsRuntime() {
        if (_demoConfigPath is not null) {
            var config = ConfigLoader.ReadSettings(File.ReadAllText(_paths.ConfigPath)).Config;
            return new SettingsRuntimeSnapshot(config, false, false);
        }
        return new SettingsRuntimeSnapshot(
            _config ?? throw new InvalidOperationException("The active runtime config is unavailable."),
            _keyPressHook is not null,
            _scrollHotKeysConfigEnabled && _scrollHotKeyService is { IsRegistered: false });
    }

    private SettingsApplyOutcome ApplySettings(ConfigModel candidate) {
        if (_demoConfigPath is not null) {
            _config = candidate;
            return SettingsApplyOutcome.Success;
        }
        try {
            var issues = ReloadConfiguration(candidate);
            return _bootstrapActivationErrors.Count == 0
                ? new SettingsApplyOutcome(true, issues)
                : new SettingsApplyOutcome(false, _bootstrapActivationErrors.ToArray());
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or
                                    Win32Exception or ArgumentException or NotSupportedException or JsonException) {
            return new SettingsApplyOutcome(false, [ex.Message]);
        }
    }

    private SettingsApplyOutcome RestoreSettingsRuntime(SettingsRuntimeSnapshot snapshot) {
        if (_demoConfigPath is not null) {
            _config = snapshot.Config;
            return SettingsApplyOutcome.Success;
        }
        try {
            var issues = ReloadConfiguration(snapshot.Config, allowBusy: true);
            if (_bootstrapActivationErrors.Count > 0) {
                return new SettingsApplyOutcome(false, _bootstrapActivationErrors.ToArray());
            }
            if (snapshot.HudEnabled && _keyPressHook is null && !EnableKeyPressVisualization()) {
                return new SettingsApplyOutcome(false, ["Could not restore the previously enabled key-press HUD."]);
            }
            if (!snapshot.HudEnabled && _keyPressHook is not null) {
                DisableKeyPressVisualization();
            }
            if (snapshot.ScrollPaused && _scrollHotKeysConfigEnabled && _scrollHotKeyService?.IsRegistered == true) {
                _scrollHotKeyService.Unregister();
            }
            SetupTrayContextMenu(issues, _loggerFactory!.CreateLogger<App>());
            return new SettingsApplyOutcome(true, issues);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or
                                    Win32Exception or ArgumentException or NotSupportedException or JsonException) {
            return new SettingsApplyOutcome(false, [ex.Message]);
        }
    }

    private List<string> ReloadConfiguration(ConfigModel? capturedConfig = null, bool allowBusy = false) {
        if (!allowBusy && _demoConfigPath is null && _coordinator is { IsIdle: false }) {
            throw new InvalidOperationException("Configuration reload is only available while navigation and macro activity are idle.");
        }
        var hudWasEnabled = _keyPressHook is not null;
        var scrollWasPaused = _scrollHotKeysConfigEnabled && _scrollHotKeyService is { IsRegistered: false };
        var previousLogger = _loggerFactory;
        DisableKeyPressVisualization();
        _coordinator?.Dispose();
        _coordinator = null;
        _hotKeyService?.Unregister();
        _scrollHotKeyService?.Dispose();
        _scrollHotKeyService = null;
        _macroHotKeyService?.Dispose();
        _macroHotKeyService = null;
        List<string> violations;
        violations = BootstrapCoordinatorSafely(capturedConfig);
        if (hudWasEnabled && !_hasBlockingViolations && !EnableKeyPressVisualization()) {
            var failure = "Could not re-enable key-press display after reload.";
            violations.Add(failure);
            _bootstrapActivationErrors.Add(failure);
        }
        if (scrollWasPaused && _scrollHotKeysConfigEnabled && _scrollHotKeyService?.IsRegistered == true) {
            _scrollHotKeyService.Unregister();
        }
        if (_bootstrapActivationErrors.Count == 0) {
            if (previousLogger is not null && !ReferenceEquals(previousLogger, _loggerFactory)) {
                previousLogger.Dispose();
            }
            foreach (var loggerFactory in _retainedLoggerFactories) {
                if (!ReferenceEquals(loggerFactory, _loggerFactory)) {
                    loggerFactory.Dispose();
                }
            }
            _retainedLoggerFactories.Clear();
        } else if (previousLogger is not null && !ReferenceEquals(previousLogger, _loggerFactory)) {
            _retainedLoggerFactories.Add(previousLogger);
        }
        SetupTrayContextMenu(violations, _loggerFactory!.CreateLogger<App>());
        return violations;
    }

    private static void CreateDemoFixture(string path) {
        var folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(folder, "themes"));
        foreach (var (resource, target) in new[] {
            ("config.json", path),
            ("dark.theme.json", Path.Combine(folder, "themes", "dark.theme.json")),
            ("light.theme.json", Path.Combine(folder, "themes", "light.theme.json")),
        }) {
            if (File.Exists(target)) {
                continue;
            }
            using var source = typeof(App).Assembly.GetManifestResourceStream("Klikety.Resources." + resource)
                ?? throw new InvalidDataException($"Missing demo resource: {resource}");
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(output);
        }
    }

    internal static void ConfigureRuntimeFixture(string path) {
        var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) as JsonObject
            ?? throw new InvalidDataException("Runtime fixture config must be a JSON object.");
        root["hotKey"] = new JsonObject { ["modifiers"] = "Control, Alt, Shift", ["key"] = "F12" };
        var macros = root["macros"] as JsonObject ?? new JsonObject();
        root["macros"] = macros;
        macros["globalHotKey"] = new JsonObject { ["modifiers"] = "Control, Alt, Shift", ["key"] = "F11" };
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void EnsureFixturePathHasNoReparsePoints(string path) {
        var directory = new DirectoryInfo(path);
        while (directory is not null) {
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) {
                throw new InvalidDataException("Runtime fixture paths cannot traverse symbolic links or junctions.");
            }
            directory = directory.Parent;
        }
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
        var processor = new KeyPressProcessor(
            PlatformServices.Instance.KeyState,
            TimeProvider.System,
            PlatformServices.Instance.KeyboardLayout,
            hkl => new Win32KeyLabelResolver(hkl));
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
