// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Reflection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinPlay.App.Services;
using WinPlay.App.Tray;
using WinPlay.App.ViewModels;

namespace WinPlay.App;

/// <summary>
/// Tray-first app: no main window. A notification-area icon toggles the Acrylic flyout;
/// right-click exits. The flyout window is created eagerly (hidden) so discovery is warm
/// by the first click.
/// </summary>
public partial class App : Application
{
    private TrayIcon? _tray;
    private FlyoutWindow? _flyout;
    private VolumeOsdWindow? _osd;
    private HotkeySettingsWindow? _hotkeys;
    private MainViewModel? _viewModel;
    private DispatcherTimer? _materialProbeTimer;
    private Window? _designProbeWindow;
    private TransparencyProbe? _transparencyProbe;
    private static Mutex? _instanceMutex;

    public App()
    {
        InitializeComponent();

        // Crash diagnostics: log unhandled exceptions to %TEMP%\winplay-crash.log so a
        // failure that would otherwise vanish (WinUI swallows some) is recoverable.
        UnhandledException += (_, e) => LogCrash("UI", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash("Domain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => LogCrash("Task", e.Exception);
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            string path = Path.Combine(Path.GetTempPath(), "winplay-crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss}] {source}: {ex}\n\n");
        }
        catch (Exception) { }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (Environment.GetCommandLineArgs().Skip(1).Contains("--diagnose-transparency-ui", StringComparer.Ordinal))
        {
            TextColorSettings.Apply();
            _transparencyProbe = new TransparencyProbe();
            _transparencyProbe.Start();
            return;
        }

        if (Environment.GetCommandLineArgs().Skip(1).Contains("--diagnose-design-ui", StringComparer.Ordinal))
        {
            TextColorSettings.Apply();
            var card = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton
            {
                Content = "测试设备 — 点击切换选中状态",
                Style = (Style)Resources["DeviceCardStyle"],
                IsChecked = false,
            };
            var editor = new Controls.HotkeyEditor { Text = "Ctrl+PgUp" };
            var layout = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 16, Padding = new Thickness(24) };
            layout.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock { Text = "设备卡片和快捷键编辑测试" });
            layout.Children.Add(card);
            layout.Children.Add(editor);
            _designProbeWindow = new Window { Title = "WinPlay 界面设计测试", Content = layout };
            _designProbeWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(620, 320));
            _materialProbeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _materialProbeTimer.Tick += (_, _) => _designProbeWindow?.Close();
            _designProbeWindow.Closed += (_, _) => { _materialProbeTimer.Stop(); _designProbeWindow = null; Exit(); };
            _designProbeWindow.Activate();
            _materialProbeTimer.Start();
            return;
        }

        bool settingsProbe = Environment.GetCommandLineArgs().Skip(1).Contains("--diagnose-settings-ui", StringComparer.Ordinal);
        if (settingsProbe || Environment.GetCommandLineArgs().Skip(1).Contains("--diagnose-theme-ui", StringComparer.Ordinal))
        {
            TextColorSettings.Apply();
            _osd = new VolumeOsdWindow();
            _hotkeys = new HotkeySettingsWindow();
            int step = 0;
            _materialProbeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(settingsProbe ? 1000 : 700) };
            _materialProbeTimer.Tick += (_, _) =>
            {
                if (step >= (settingsProbe ? 12 : 9))
                {
                    _materialProbeTimer.Stop();
                    _hotkeys.Close();
                    _osd.Close();
                    Exit();
                    return;
                }
                var choice = (ThemeService.ThemeChoice)(settingsProbe ? step / 4 : step % 3);
                ThemeService.Preview(choice);
                if (settingsProbe)
                {
                    _hotkeys.PreviewPage(step % 4);
                    _hotkeys.Activate();
                }
                else if (step % 2 == 0) _hotkeys.Activate();
                else _hotkeys.AppWindow.Hide();
                _osd.ShowLevel("WinPlay", 65);
                string? log = Environment.GetEnvironmentVariable("WINPLAY_UI_LOG_PATH");
                if (!string.IsNullOrWhiteSpace(log))
                {
                    var systemAccent = new Windows.UI.ViewManagement.UISettings()
                        .GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
                    var root = (FrameworkElement)_osd.Content;
                    var resources = Application.Current.Resources.MergedDictionaries
                        .First(dictionary => dictionary.ThemeDictionaries.TryGetValue("Light", out object? light)
                            && light is ResourceDictionary palette && palette.ContainsKey("AccentBrush"));
                    string key = root.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
                    var palette = (ResourceDictionary)resources.ThemeDictionaries[key];
                    var accent = ((Microsoft.UI.Xaml.Media.SolidColorBrush)palette["AccentBrush"]).Color;
                    File.AppendAllText(log, $"theme-probe step={step} choice={choice} page={(settingsProbe ? step % 4 : -1)} systemAccent={systemAccent} appliedAccent={accent}\n");
                }
                ++step;
            };
            _materialProbeTimer.Start();
            return;
        }

        // Finite UI-only probe: exercise the real HUD lifecycle without creating
        // a streaming view model, tray icon, hotkeys, network sockets or mutex.
        if (args.Arguments.Trim() == "--diagnose-volume-ui" ||
            Environment.GetCommandLineArgs().Skip(1).Contains("--diagnose-volume-ui", StringComparer.Ordinal))
        {
            TextColorSettings.Apply();
            _osd = new VolumeOsdWindow();
            int remaining = 3;
            _materialProbeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _materialProbeTimer.Tick += (_, _) =>
            {
                if (--remaining > 0) _osd.ShowLevel("WinPlay", 65);
                else
                {
                    _materialProbeTimer.Stop();
                    _osd.Close();
                    Exit();
                }
            };
            DispatcherQueue.GetForCurrentThread().TryEnqueue(() => _osd.ShowLevel("WinPlay", 65));
            _materialProbeTimer.Start();
            return;
        }

        // Single instance: a second WinPlay would fail to bind the PTP grandmaster ports
        // (319/320) and break streaming. If one is already running, exit quietly.
        _instanceMutex = new Mutex(true, @"Local\WinPlay.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            Process.GetCurrentProcess().Kill();
            return;
        }

        TextColorSettings.Apply();
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        _dispatcher = dispatcher;
        _viewModel = new MainViewModel(dispatcher);
        _osd = new VolumeOsdWindow();
        _viewModel.ShowVolumeHud = (name, percent) =>
            dispatcher.TryEnqueue(() => _osd?.ShowLevel(name, percent));
        _flyout = new FlyoutWindow(_viewModel);
        _flyout.OpenSettings = OpenHotkeySettings;

        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "winplay.ico");
        _tray = new TrayIcon("WinPlay — AirPlay to speakers and TVs", iconPath);
        _tray.LeftClicked += () => dispatcher.TryEnqueue(() => _flyout?.Toggle());
        _tray.MenuBuilder = BuildTrayMenu;

        // Sign-in already passed --startup. Stay in the tray; a manual open still shows the card.
        StartupManager.RefreshCommand();
        if (StartupManager.IsStartupLaunch(args.Arguments)) return;

        dispatcher.TryEnqueue(() =>
        {
            try
            {
                _flyout?.ShowNearTray();
            }
            catch (Exception ex)
            {
                LogCrash("Show", ex);
            }
        });
    }

    private const string RepositoryUrl = "https://github.com/chenzai666/WinPlay";

    private IReadOnlyList<TrayMenuItem> BuildTrayMenu()
    {
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.3";
        return
        [
            new TrayMenuItem { Text = "Open WinPlay", IsDefault = true, Clicked = () => _dispatcher?.TryEnqueue(() => _flyout?.ShowNearTray()) },
            TrayMenuItem.Separator,
            BufferItem(AirPlayStreamingSettings.Mode.RealTime, "AirPlay 实时（约 0.3 秒）"),
            BufferItem(AirPlayStreamingSettings.Mode.Low, "AirPlay 低延迟（0.7 秒）"),
            BufferItem(AirPlayStreamingSettings.Mode.Normal, "AirPlay 普通（2 秒）"),
            BufferItem(AirPlayStreamingSettings.Mode.Buffered, "AirPlay 缓冲（3 秒）"),
            new TrayMenuItem { Text = "设置…", Clicked = OpenHotkeySettings },
            new TrayMenuItem
            {
                Text = $"快捷键 {HotkeySettings.VolumeUp.Text} / {HotkeySettings.VolumeDown.Text} / {HotkeySettings.Mute.Text}",
                IsEnabled = false,
            },
            TrayMenuItem.Separator,
            new TrayMenuItem
            {
                Text = "Start with Windows",
                IsChecked = StartupManager.IsEnabled(),
                Clicked = () => StartupManager.SetEnabled(!StartupManager.IsEnabled()),
            },
            TrayMenuItem.Separator,
            new TrayMenuItem { Text = "Support on GitHub", Clicked = () => OpenUrl(RepositoryUrl) },
            new TrayMenuItem { Text = "Report an issue", Clicked = () => OpenUrl($"{RepositoryUrl}/issues/new/choose") },
            new TrayMenuItem { Text = $"WinPlay {version}", IsEnabled = false },
            TrayMenuItem.Separator,
            new TrayMenuItem { Text = "Quit WinPlay", Clicked = () => _dispatcher?.TryEnqueue(ExitApp) },
        ];
    }

    private static TrayMenuItem BufferItem(AirPlayStreamingSettings.Mode mode, string label) => new()
    {
        Text = label,
        IsChecked = AirPlayStreamingSettings.Current == mode,
        Clicked = () => AirPlayStreamingSettings.Set(mode),
    };

    private DispatcherQueue? _dispatcher;

    private void OpenHotkeySettings()
    {
        _dispatcher?.TryEnqueue(() =>
        {
            if (_hotkeys is null)
            {
                try
                {
                    _hotkeys = new HotkeySettingsWindow();
                }
                catch (Exception ex)
                {
                    LogCrash("Settings", ex);
                    _hotkeys = null;
                    return;
                }
                _hotkeys.MaterialChanged = () =>
                {
                    _flyout?.ApplyMaterial();
                    _osd?.ApplyMaterial();
                };
                _hotkeys.Closed += (_, _) =>
                {
                    _viewModel?.SuspendHotkeys(false);
                    _hotkeys = null;
                };
            }
            _viewModel?.SuspendHotkeys(true);
            _hotkeys.Activate();
        });
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { }
    }

    private async void ExitApp()
    {
        _tray?.Dispose();
        _tray = null;
        _flyout?.HideFlyout();
        if (_viewModel is not null)
            await _viewModel.DisposeAsync();
        Exit();
    }
}
