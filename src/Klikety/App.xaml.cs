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
    private SettingsRuntimeResources? _hudResources;

#if DEBUG
    private HotKeyService? _debugHotKeyService;
    private OverlayWindow? _debugOverlay;
#endif
    private ILoggerFactory? _loggerFactory;
    private readonly SettingsLoggerLifetime _loggerLifetime = new();
    private readonly List<string> _bootstrapActivationErrors = [];
    private SettingsRuntimeResources? _runtimeResources;
    private SettingsFixtureFaults? _fixtureFaults;
    private readonly SettingsOperationGate _settingsGate;
    private bool _hasBlockingViolations;
    private SettingsWindow? _settingsWindow;
    private string? _demoConfigPath;
    private string? _runtimeFixtureRoot;
    private AppPaths _paths = AppPaths.User;
    private static string UserConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Klikety", "config.json");

    public App() {
        _settingsGate = new SettingsOperationGate(() => {
            if (_runtimeFixtureRoot is not null || _demoConfigPath is not null) { _paths.ValidateFixture(); }
            return _coordinator?.IsIdle != false;
        });
    }

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
                _paths = AppPaths.ForFixture(_runtimeFixtureRoot);
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
                _fixtureFaults = new SettingsFixtureFaults(_paths);
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
                _paths = AppPaths.ForFixture(Path.GetDirectoryName(_demoConfigPath)!);
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
    private List<string> BootstrapCoordinator(ConfigModel? capturedConfig = null, ConfigLoadResult? prepared = null) {
        _bootstrapActivationErrors.Clear();
        var configResult = prepared ?? (capturedConfig is null
            ? ConfigLoader.Load(_paths.ConfigPath)
            : new ConfigLoadResult { Config = capturedConfig });
        var config = configResult.Config;
        var violations = new List<string>(configResult.Violations);
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
            _loggerFactory ??= LoggingSetup.CreateLoggerFactory("Warning", false, 7, _paths.LogsFolder);
            return violations;
        }

        var (theme, themeWarning) = ThemeLoader.Load(config.Theme, _paths.Root);
        if (themeWarning is not null) {
            violations.Add(themeWarning);
        }
        ILoggerFactory? candidateLogger = null;
        NavigatorCoordinator? coordinator = null;
        ScrollHotKeyService? scroll = null;
        MacroHotKeyService? macro = null;
        MacroPickerOverlay? picker = null;
        MacroStore? macroStore = null;
        MacrosFile? macrosFile = null;
        Win32KeyLabelResolver? resolver = null;
        SettingsRuntimeResources resources;
        try {
            candidateLogger = LoggingSetup.CreateLoggerFactory(
                config.LogLevel, config.FileLoggingEnabled, config.RetainedLogFileCount, _paths.LogsFolder);
            var logger = candidateLogger.CreateLogger<App>();
            resources = SettingsRuntimeResources.Create(owner => {
                owner.Checkpoint("logger");
                var hook = owner.Own("navigation hook", new KeyboardHookService());
                var mouse = new MouseActionService(logger);
                var overlay = new OverlayWindow();
                owner.Own("overlay", overlay.Close);
                owner.Checkpoint("overlay");
                overlay.SetTheme(theme);
                overlay.SetLogger(logger);
                resolver = new Win32KeyLabelResolver();
                var labels = new LabelGenerator(config.HorizontalKeys, config.VerticalKeys, resolver);
                var grid = new GridRenderer(overlay.Canvas, theme, labels, config.MinLabelFontSize);
                var horizontal = new AxisLabelGenerator(config.HorizontalKeys, resolver);
                var vertical = new AxisLabelGenerator(config.VerticalKeys, resolver);
                var crosshair = config.Modes.Crosshair.Enabled
                    ? new CrosshairRenderer(overlay.Canvas, theme, horizontal, vertical, config.MinLabelFontSize) : null;
                var logCrosshair = config.Modes.LogCrosshair.Enabled
                    ? new LogCrosshairRenderer(overlay.Canvas, theme, horizontal, vertical, config.MinLabelFontSize) : null;
                var logGrid = config.Modes.LogGrid.Enabled
                    ? new LogGridRenderer(overlay.Canvas, theme, horizontal, vertical, config.MinLabelFontSize) : null;
                var sessions = new ModeSessionFactory(config, new ActionMapper(config.ActionBindings),
                    grid, crosshair, logCrosshair, logGrid);
                if (sessions.LogGridKeyPolicyWarning is { } warning) { violations.Add(warning); }
                macroStore = new MacroStore(_paths.MacrosPath);
                var loadedMacros = macroStore.Load();
                macrosFile = loadedMacros.File;
                violations.AddRange(loadedMacros.Errors);
                coordinator = owner.Own("coordinator", new NavigatorCoordinator(_hotKeyService!, hook,
                    mouse, overlay, sessions, PlatformServices.Instance, new ModifierDetector(), config,
                    logger, macroStore, macrosFile, () => new SatelliteWindow(theme, logger),
                    new DisplayTopologyStore(_paths.DisplayTopologyPath)));
                owner.Transfer("navigation hook");
                owner.Checkpoint("coordinator");
                owner.Own("main hotkey", _hotKeyService!.Unregister);
                if (!_hotKeyService.Register(config.HotKey)) {
                    throw new InvalidOperationException("hotKey: failed to register the application shortcut.");
                }
                owner.Checkpoint("main");
                scroll = owner.Own("scroll hotkeys", new ScrollHotKeyService(config.ScrollHotKeys, mouse, logger,
                    owner.Checkpoint));
                if (config.ScrollHotKeys.Enabled) {
                    var failures = scroll.Register();
                    if (failures.Count > 0) { throw new InvalidOperationException(string.Join("\n", failures)); }
                }
                owner.Checkpoint("scroll");
                picker = new MacroPickerOverlay();
                owner.Own("macro picker", picker.Close);
                if (config.Macros.Enabled && config.Macros.GlobalHotKey is { } macroHotkey) {
                    macro = owner.Own("macro hotkey", new MacroHotKeyService(macroHotkey, logger));
                    if (macro.Register() is { } failure) { throw new InvalidOperationException(failure); }
                }
                owner.Checkpoint("macro");
                coordinator.MacroHotKeyService = macro;
                coordinator.MacroPickerWindow = picker;
                var playback = new MacroPlaybackOverlay();
                owner.Own("macro playback overlay", playback.Close);
                coordinator.MacroPlaybackWindow = playback;
                coordinator.ClickIndicator = new ClickIndicatorAdapter(new ClickIndicatorWindow(config.Macros.PlaybackIndicator));
                owner.ReleaseFirst("coordinator");
                owner.Checkpoint("indicator");
            }, stage => _fixtureFaults?.Check(stage), pending => _runtimeResources = pending);
        } catch {
            if (candidateLogger is not null) { _loggerLifetime.Retain(candidateLogger); }
            throw;
        }
        _runtimeResources = resources;
        _loggerFactory = candidateLogger;
        _config = config;
        _coordinator = coordinator;
        _scrollHotKeyService = scroll;
        _scrollHotKeysConfigEnabled = config.ScrollHotKeys.Enabled;
        _macroHotKeyService = macro;
        _macroPickerOverlay = picker;
        _macroStore = macroStore;
        _macrosFile = macrosFile;

#if DEBUG
        if (_runtimeFixtureRoot is null) { SetupDebugLogGridSession(config, theme, resolver!); }
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
                try {
                    using var operation = _settingsGate.Enter();
                    var error = ConfigResetter.ResetToDefaults(_paths.ConfigPath);
                    if (error is not null) {
                        _trayIcon?.ShowNotification("Klikety — Reset Failed", error);
                        return;
                    }
                    if (_runtimeFixtureRoot is not null) { ConfigureRuntimeFixture(_paths.ConfigPath); }
                    var newViolations = ReloadConfigurationCore();
                    CompleteRuntimeOperation();
                    _trayIcon?.ShowNotification("Klikety",
                        newViolations.Count > 0 ? string.Join("\n", newViolations) : "Configuration reset to defaults.");
                } catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException) {
                    _trayIcon?.ShowNotification("Klikety — Reset Failed", ex.Message);
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
            var faults = new System.Windows.Controls.MenuItem { Header = "Fixture: fail next operation" };
            foreach (var stage in SettingsFixtureFaults.Stages) {
                if (stage != "disk-restore") {
                    var fail = new System.Windows.Controls.MenuItem { Header = "Candidate " + stage };
                    fail.Click += (_, _) => ArmFixtureFault(stage, false);
                    faults.Items.Add(fail);
                }
                if (stage is not ("disk-save" or "external-edit")) {
                    var recover = new System.Windows.Controls.MenuItem { Header = "Recovery " + stage };
                    recover.Click += (_, _) => ArmFixtureFault(stage, true);
                    faults.Items.Add(recover);
                }
            }
            contextMenu.Items.Add(faults);
            var conflict = new System.Windows.Controls.MenuItem {
                Header = "Fixture: reserve Ctrl+Alt+Shift+Backspace conflict",
                IsCheckable = true,
                IsChecked = _fixtureFaults!.HasConflict,
            };
            conflict.Click += (_, _) => {
                try {
                    if (_fixtureFaults.HasConflict) {
                        var failures = _fixtureFaults.ReleaseConflict();
                        if (failures.Count > 0) { throw new InvalidOperationException(string.Join("\n", failures)); }
                    } else {
                        _fixtureFaults.ReserveConflict(() => new HotKeyService());
                    }
                    _trayIcon?.ShowNotification("Klikety fixture", _fixtureFaults.HasConflict
                        ? "Test shortcut reserved. Choose Ctrl+Alt+Shift+Backspace in Settings to exercise conflict refusal."
                        : "Test conflict shortcut released.");
                } catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException) {
                    _trayIcon?.ShowNotification("Klikety fixture conflict failed", ex.Message);
                }
                conflict.IsChecked = _fixtureFaults.HasConflict;
            };
            contextMenu.Items.Add(conflict);
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
            IsChecked = _keyPressHook is not null || _hudResources?.HasResources == true,
        };
        keyPressItem.Click += (_, _) => {
            try {
                if (keyPressItem.IsChecked) {
                    DisableKeyPressVisualization();
                    _trayIcon!.ToolTipText = _runtimeFixtureRoot is null ? "Klikety" : "Klikety - ISOLATED RUNTIME FIXTURE";
                } else {
                    if (EnableKeyPressVisualization()) {
                        _trayIcon!.ToolTipText = _runtimeFixtureRoot is null
                            ? "Klikety (Key Display Active)"
                            : "Klikety - ISOLATED RUNTIME FIXTURE (Key Display Active)";
                    }
                }
            } catch (InvalidOperationException ex) {
                _trayIcon?.ShowNotification("Klikety HUD cleanup failed", ex.Message);
                LogSettingsActivationFailed(logger, ex.Message);
            }
            keyPressItem.IsChecked = _keyPressHook is not null || _hudResources?.HasResources == true;
        };
        contextMenu.Items.Add(keyPressItem);

        // Scroll Keys toggle (visible when scroll hotkeys are enabled in config)
        if (_scrollHotKeyService is not null && _scrollHotKeysConfigEnabled) {
            var scrollItem = new System.Windows.Controls.MenuItem {
                Header = _scrollHotKeyService.IsRegistered ? "Pause Scroll Keys" : "Resume Scroll Keys",
            };
            scrollItem.Click += (_, _) => {
                try {
                    if (_scrollHotKeyService.IsRegistered) {
                        _scrollHotKeyService.Unregister();
                    } else {
                        var failures = _scrollHotKeyService.Register();
                        if (failures.Count > 0) {
                            _trayIcon?.ShowNotification("Klikety", string.Join("\n", failures));
                        }
                    }
                } catch (Exception ex) when (ex is InvalidOperationException or IOException) {
                    _trayIcon?.ShowNotification("Klikety scroll keys failed", ex.Message);
                    LogSettingsActivationFailed(logger, ex.Message);
                }
                scrollItem.Header = _scrollHotKeyService.IsRegistered ? "Pause Scroll Keys" : "Resume Scroll Keys";
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
            var cleanup = ReleaseSettingsRuntime().ToList();
            cleanup.AddRange(_fixtureFaults?.ReleaseConflict() ?? []);
            try { _hotKeyService?.Dispose(); } catch (InvalidOperationException ex) { cleanup.Add(ex.Message); }
            if (cleanup.Count > 0) {
                MessageBox.Show(string.Join("\n", cleanup), "Klikety cleanup", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            _trayIcon?.Dispose();
            DisposeLoggerFactories();
            Shutdown();
        };
        contextMenu.Items.Add(quitItem);

        _trayIcon!.ContextMenu = contextMenu;
    }

    private void ArmFixtureFault(string stage, bool recovery) {
        try {
            _fixtureFaults!.Arm(stage, recovery);
            _trayIcon?.ShowNotification("Klikety fixture", $"Armed {(recovery ? "recovery" : "candidate")} {stage}");
        } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) {
            _trayIcon?.ShowNotification("Klikety fixture fault could not be armed", ex.Message);
        }
    }

    private void DisposeLoggerFactories() {
        _loggerFactory?.Dispose();
        _loggerFactory = null;
        _loggerLifetime.Complete(null);
    }

    private List<string> BootstrapCoordinatorSafely(ConfigModel? capturedConfig = null, ConfigLoadResult? prepared = null) {
        try {
            return BootstrapCoordinator(capturedConfig, prepared);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or
                                    Win32Exception or ArgumentException or NotSupportedException or JsonException) {
            _loggerFactory ??= LoggingSetup.CreateLoggerFactory("Warning", false, 7, _paths.LogsFolder);
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
                ValidateSettingsApply, CaptureSettingsRuntime, ApplySettings, RestoreSettingsRuntime,
                _settingsGate, CompleteRuntimeOperation, stage => _fixtureFaults?.Check(stage));
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
        ThemeLoader.ValidateSettingsReference(config.Theme, _settingsWindow?.LoadedTheme ?? _config?.Theme, _paths.Root);
        if (_demoConfigPath is null) {
            var owned = _config is null ? [] : SettingsShortcutInventory.From(_config,
                _scrollHotKeysConfigEnabled && _scrollHotKeyService is { IsRegistered: false });
            SettingsShortcutInventory.Preflight(config, _hotKeyService?.IsRegistered == true ? owned : [],
                StartupValidator.ProbeHotKey);
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
            _fixtureFaults?.SetRecovery(false);
            _fixtureFaults?.Check("external-edit");
            var issues = ReloadConfigurationCore(candidate);
            var result = _bootstrapActivationErrors.Count == 0
                ? new SettingsApplyOutcome(true, issues)
                : new SettingsApplyOutcome(false, _bootstrapActivationErrors.ToArray());
            if (!result.Succeeded && _loggerFactory is { } factory) {
                LogSettingsActivationFailed(factory.CreateLogger<App>(), string.Join("; ", result.Issues));
            }
            return result;
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
            _fixtureFaults?.SetRecovery(true);
            var issues = ReloadConfigurationCore(snapshot.Config, snapshot);
            if (_bootstrapActivationErrors.Count > 0) {
                return new SettingsApplyOutcome(false, _bootstrapActivationErrors.ToArray());
            }
            return new SettingsApplyOutcome(true, issues);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or
                                    Win32Exception or ArgumentException or NotSupportedException or JsonException) {
            return new SettingsApplyOutcome(false, [ex.Message]);
        } finally {
            _fixtureFaults?.SetRecovery(false);
        }
    }

    private List<string> ReloadConfiguration() {
        using var operation = _settingsGate.Enter();
        var issues = ReloadConfigurationCore();
        CompleteRuntimeOperation();
        return issues;
    }

    private IReadOnlyList<string> ReleaseRuntime() {
        var failures = _runtimeResources?.Release() ?? [];
        if (failures.Count == 0) { _runtimeResources = null; }
        _coordinator = null;
        _scrollHotKeyService = null;
        _macroHotKeyService = null;
        _macroPickerOverlay = null;
        return failures;
    }

    private IReadOnlyList<string> ReleaseSettingsRuntime() {
        var failures = new List<string>();
        try { DisableKeyPressVisualization(); } catch (InvalidOperationException ex) { failures.Add(ex.Message); }
        failures.AddRange(ReleaseRuntime());
        return failures;
    }

    private List<string> ReloadConfigurationCore(ConfigModel? capturedConfig = null, SettingsRuntimeSnapshot? restoredState = null) {
        var previous = new SettingsRuntimeSnapshot(_config ?? new ConfigModel(),
            _keyPressHook is not null, _scrollHotKeysConfigEnabled && _scrollHotKeyService is { IsRegistered: false });
        var previousLogger = _loggerFactory;
        var prepared = capturedConfig is null ? ConfigLoader.Load(_paths.ConfigPath) : null;
        var candidate = capturedConfig ?? prepared!.Config;
        var violations = new List<string>();
        var result = SettingsRuntimeReplacement.Activate(restoredState ?? previous, candidate,
            ReleaseSettingsRuntime,
            model => {
                violations = BootstrapCoordinatorSafely(model, prepared);
                return new(_bootstrapActivationErrors.Count == 0, violations);
            },
            (hudEnabled, scrollPaused) => {
                if (hudEnabled && !EnableKeyPressVisualization()) {
                    return new(false, ["Could not re-enable key-press display after reload."]);
                }
                if (scrollPaused) { _scrollHotKeyService?.Unregister(); }
                return SettingsApplyOutcome.Success;
            });
        _bootstrapActivationErrors.Clear();
        if (!result.Succeeded) {
            _bootstrapActivationErrors.AddRange(result.Issues);
            _config = previous.Config;
            _hasBlockingViolations = true;
        }
        if (previousLogger is not null && !ReferenceEquals(previousLogger, _loggerFactory)) {
            _loggerLifetime.Retain(previousLogger);
        }
        SetupTrayContextMenu(result.Issues.ToList(), _loggerFactory!.CreateLogger<App>());
        return result.Issues.ToList();
    }

    private void CompleteRuntimeOperation() {
        if (_hasBlockingViolations && (_runtimeResources?.HasResources == true || _hudResources?.HasResources == true)) {
            return;
        }
        _loggerLifetime.Complete(_loggerFactory);
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
        root["hotKey"] = new JsonObject { ["modifiers"] = "Control, Alt, Shift", ["key"] = "F11" };
        var macros = root["macros"] as JsonObject ?? new JsonObject();
        root["macros"] = macros;
        macros["globalHotKey"] = new JsonObject { ["modifiers"] = "Control, Alt, Shift", ["key"] = "Pause" };
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void EnsureFixturePathHasNoReparsePoints(string path) => AppPaths.ForFixture(path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to register global hotkey {Modifiers}+{Key}")]
    private static partial void LogHotkeyRegistrationFailed(ILogger logger, HotKeyModifiers modifiers, Input.VKey key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Startup violations: {Message}")]
    private static partial void LogStartupViolations(ILogger logger, string message);

    /// <summary>
    /// Activation transaction: creates all key press visualization resources and enables the hook.
    /// Returns false (and cleans up) if the hook fails to install.
    /// </summary>
    private bool EnableKeyPressVisualization() {
        if (_keyPressHook is not null) { return true; }
        var vizConfig = _config!.KeyPressVisualization;
        try {
            if (_hudResources?.HasResources == true) {
                throw new InvalidOperationException("Previous HUD cleanup is incomplete; retry disabling it before enabling another hook.");
            }
            _hudResources = SettingsRuntimeResources.Create(owner => {
                var hook = owner.Own("HUD hook", new KeyboardHookService());
                var processor = new KeyPressProcessor(PlatformServices.Instance.KeyState,
                    TimeProvider.System, PlatformServices.Instance.KeyboardLayout, hkl => new Win32KeyLabelResolver(hkl));
                var window = new KeyPressWindow(vizConfig);
                owner.Own("HUD window", window.Close);
                var manager = owner.Own("HUD manager", new KeyPressDisplayManager(vizConfig, processor,
                    window, new MonitorService(window)));
                if (!hook.Enable()) { throw new InvalidOperationException("HUD hook could not be enabled."); }
                hook.KeyEvent += (_, e) => manager.HandleKeyEvent(e);
                owner.Checkpoint("hud");
                window.Show();
                _keyPressHook = hook;
                _keyPressProcessor = processor;
                _keyPressWindow = window;
                _keyPressDisplayManager = manager;
            }, stage => _fixtureFaults?.Check(stage), pending => _hudResources = pending);
            return true;
        } catch (InvalidOperationException ex) {
            _trayIcon?.ShowNotification("Klikety", "Failed to enable key press display: " + ex.Message);
            if (_loggerFactory is { } factory) { LogHudActivationFailed(factory.CreateLogger<App>(), ex); }
            return false;
        }
    }

    /// <summary>
    /// Disables and disposes all key press visualization resources. Safe to call when already disabled.
    /// </summary>
    private void DisableKeyPressVisualization() {
        if (_hudResources is null) {
            return;
        }

        _keyPressProcessor?.ResetModifierState();
        var failures = _hudResources?.Release() ?? [];
        if (failures.Count > 0) { throw new InvalidOperationException(string.Join("\n", failures)); }
        _hudResources = null;
        _keyPressHook = null;
        _keyPressProcessor = null;
        _keyPressWindow = null;
        _keyPressDisplayManager = null;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "HUD activation failed")]
    private static partial void LogHudActivationFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Settings activation failed: {Issue}")]
    private static partial void LogSettingsActivationFailed(ILogger logger, string issue);

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
