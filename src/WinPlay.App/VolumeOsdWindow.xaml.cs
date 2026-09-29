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
    private readonly IntPtr _hwnd;
    private bool _placed;
    private bool _visible;

    public VolumeOsdWindow()
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd));
        int ex = GetWindowLong(_hwnd, -20);
        SetWindowLong(_hwnd, -20, ex | 0x00000008 | 0x00000080 | 0x08000000); // TOPMOST | TOOLWINDOW | NOACTIVATE
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
        DwmSetWindowAttribute(_hwnd, 33, ref corner, sizeof(int));

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
        ShowWindow(_hwnd, 8); // SW_SHOWNA — visible, does not steal focus
        SetWindowPos(_hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0013); // TOPMOST | NOMOVE | NOSIZE | NOACTIVATE
        _visible = true;

        _hide.Stop();
        _hide.Start();
    }

    private void PlaceOnce()
    {
        int x, y;
        try
        {
            var area = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
            var work = area.WorkArea;
            x = work.X + (work.Width - Width) / 2;
            y = work.Y + work.Height - Height - 100;
        }
        catch (Exception)
        {
            x = Math.Max(0, (GetSystemMetrics(0) - Width) / 2);
            y = Math.Max(0, GetSystemMetrics(1) - Height - 100);
        }
        SetWindowPos(_hwnd, new IntPtr(-1), x, y, Width, Height, 0x0010); // NOACTIVATE
        _appWindow.Move(new Windows.Graphics.PointInt32(x, y));
        _placed = true;
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
