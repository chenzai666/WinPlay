// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace WinPlay.App.Services;

/// <summary>有限时界面诊断，不创建媒体服务或写入用户配置。</summary>
internal sealed class TransparencyProbe
{
    private static readonly int[] Values = [70, 85, 90, 95, 100];
    private readonly Window _card;
    private readonly VolumeOsdWindow _hud;
    private readonly DispatcherTimer _timer;
    private int _step;
    private bool _refreshHud;
    private bool _stopped;

    internal TransparencyProbe()
    {
        var content = new StackPanel { Padding = new Thickness(16), Spacing = 12 };
        content.Children.Add(new TextBlock { Text = "音频输出", FontSize = 16 });
        content.Children.Add(new ToggleButton
        {
            Content = "测试设备    已连接",
            Style = (Style)Application.Current.Resources["DeviceCardStyle"],
            IsChecked = true,
        });
        var root = new Grid { Width = 384, Height = 220, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        root.Children.Add(content);
        _card = new Window { Title = "WinPlay 透明度设备面板测试", Content = root };
        if (_card.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(true, false);
        }
        IntPtr hwnd = WindowNative.GetWindowHandle(_card);
        int borderColor = unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(hwnd, 34, ref borderColor, sizeof(int));
        ResizeCard();
        root.Loaded += OnCardLoaded;
        WindowShapeService.Rounded(_card);
        WindowBackdropService.ApplyAcrylic(_card);
        _hud = new VolumeOsdWindow();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        _card.Closed += OnClosed;
    }

    internal void Start()
    {
        _card.Activate();
        _card.DispatcherQueue.TryEnqueue(() =>
        {
            if (_stopped) return;
            _card.AppWindow.Show();
            ResizeCard();
            Step();
            _timer.Start();
        });
    }

    private void OnCardLoaded(object sender, RoutedEventArgs e)
    {
        if (!_stopped) ResizeCard();
    }

    private void ResizeCard()
    {
        IntPtr hwnd = WindowNative.GetWindowHandle(_card);
        double scale = GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0) scale = 1;
        PopupWindowService.ResizeClient(_card.AppWindow, hwnd, (int)Math.Ceiling(384 * scale), (int)Math.Ceiling(220 * scale));
    }

    private void OnTick(object? sender, object args)
    {
        if (!_refreshHud)
        {
            _hud.ShowLevel("测试设备", 65);
            _refreshHud = true;
        }
        else if (_step >= Values.Length * 2) Stop();
        else { _refreshHud = false; Step(); }
    }

    private void Step()
    {
        int value = Values[_step % Values.Length];
        var theme = _step < Values.Length ? ThemeService.ThemeChoice.Light : ThemeService.ThemeChoice.Dark;
        CardMaterialSettings.PreviewTransparency(value);
        ThemeService.Preview(theme);
        WindowBackdropService.Refresh(_card);
        WindowBackdropService.Refresh(_hud);
        _hud.ShowLevel("测试设备", 65);
        string? path = Environment.GetEnvironmentVariable("WINPLAY_UI_LOG_PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            try { File.AppendAllText(path, $"transparency-probe step={_step} transparency={value} theme={theme}\n"); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        ++_step;
    }

    private void OnClosed(object sender, WindowEventArgs e) => Stop(cardAlreadyClosed: true);

    private void Stop(bool cardAlreadyClosed = false)
    {
        if (_stopped) return;
        _stopped = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _card.Closed -= OnClosed;
        if (_card.Content is FrameworkElement root) root.Loaded -= OnCardLoaded;
        _hud.Close();
        if (!cardAlreadyClosed) _card.Close();
        Application.Current.Exit();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
