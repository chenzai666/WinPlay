// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace WinPlay.App;

/// <summary>
/// TuneBlade-style OSD: fixed dark card, green percent, bar that snaps (no animation).
/// </summary>
public sealed partial class VolumeOsdWindow : Window
{
    private const int Width = 340;
    private const int Height = 118;
    private const int BarWidth = 300;

    private readonly AppWindow _appWindow;
    private readonly DispatcherTimer _hide;
    private bool _placed;
    private bool _visible;

    public VolumeOsdWindow()
    {
        InitializeComponent();
        IntPtr hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd));
        _appWindow.IsShownInSwitchers = false;

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        int corner = 1; // DWMWCP_DONOTROUND — flat card, like the TuneBlade popup
        DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));

        _appWindow.Resize(new Windows.Graphics.SizeInt32(Width, Height));
        PlaceOnce();

        _hide = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _hide.Tick += (_, _) =>
        {
            _hide.Stop();
            if (_visible)
            {
                _appWindow.Hide();
                _visible = false;
            }
        };
    }

    public void ShowLevel(string name, double percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        bool muted = percent <= 0.5;
        NameText.Text = string.IsNullOrWhiteSpace(name) ? "WinPlay" : name;
        PercentText.Text = muted ? "静音" : $"{percent:0}%";
        PercentText.Foreground = new SolidColorBrush(muted
            ? Windows.UI.Color.FromArgb(255, 0xF8, 0x71, 0x71)
            : Windows.UI.Color.FromArgb(255, 0x4A, 0xDE, 0x80));
        var fill = muted
            ? Windows.UI.Color.FromArgb(255, 0xF8, 0x71, 0x71)
            : Windows.UI.Color.FromArgb(255, 0x4A, 0xDE, 0x80);
        BarFill.Background = new SolidColorBrush(fill);
        BarFill.Width = muted ? 0 : BarWidth * (percent / 100.0);
        TipText.Text = muted ? "已静音" : "WinPlay 音量";

        if (!_placed) PlaceOnce();
        if (!_visible)
        {
            _appWindow.Show(false);
            _visible = true;
        }

        _hide.Stop();
        _hide.Start();
    }

    private void PlaceOnce()
    {
        var area = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var work = area.WorkArea;
        _appWindow.Move(new Windows.Graphics.PointInt32(
            work.X + (work.Width - Width) / 2,
            work.Y + work.Height - Height - 100));
        _placed = true;
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
