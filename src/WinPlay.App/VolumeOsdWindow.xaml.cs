// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using WinRT.Interop;

namespace WinPlay.App;

/// <summary>
/// Theme-aware acrylic HUD; volume updates immediately and the surface fades without taking focus.
/// </summary>
public sealed partial class VolumeOsdWindow : Window
{
    private const int Width = 420;
    private const int Height = 64;
    private const int BarWidth = 220;

    private readonly AppWindow _appWindow;
    private readonly DispatcherTimer _hide;
    private readonly IntPtr _hwnd;
    private bool _placed;
    private bool _visible;
    private bool _islandReady;
    private int _fadeVersion;

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
            presenter.SetBorderAndTitleBar(true, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        ApplyMaterial();

        _appWindow.ResizeClient(new Windows.Graphics.SizeInt32(PhysicalWidth, PhysicalHeight));
        // SW_SHOWNA draws nothing until this window has been Activate()'d once.
        // Opening the device card used to be that first activation. Do it off-screen
        // at launch so a sign-in start can show the volume card on the first key.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, PrimeHidden);

        _hide = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _hide.Tick += (_, _) =>
        {
            _hide.Stop();
            if (_visible) FadeOut();
        };
    }

    /// <summary>System acrylic, or a solid card when frosted glass is turned off.</summary>
    public void ApplyMaterial()
    {
        if (Services.CardMaterialSettings.UseGlass)
            Services.WindowBackdropService.ApplyAcrylic(this);
        else
            Services.WindowBackdropService.ApplySolid(this);
        int corner = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(_hwnd, 33, ref corner, sizeof(int));
        int borderColor = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(_hwnd, 34, ref borderColor, sizeof(int));
    }

    public void ShowLevel(string name, double percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        bool muted = percent <= 0.5;
        NameText.Text = string.IsNullOrWhiteSpace(name) ? "WinPlay" : name;
        PercentText.Text = $"{percent:0}%";
        PercentText.Visibility = muted ? Visibility.Collapsed : Visibility.Visible;
        MutedText.Visibility = muted ? Visibility.Visible : Visibility.Collapsed;
        VolumeIcon.Glyph = muted ? "\uE74F" : "\uE767";
        BarFill.Width = muted ? 0 : BarWidth * (percent / 100.0);
        TipText.Text = muted ? "已静音" : "WinPlay 音量";

        if (!_visible || !_placed) PlaceOnce();
        if (!_islandReady) AttachIsland();
        int cloak = 0; // Hide() recloaks; SW_SHOWNA does not clear that
        DwmSetWindowAttribute(_hwnd, 13, ref cloak, sizeof(int));
        _appWindow.Show();
        ShowWindow(_hwnd, 8); // SW_SHOWNA — visible, does not steal focus
        SetWindowPos(_hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0013); // TOPMOST | NOMOVE | NOSIZE | NOACTIVATE
        Services.WindowBackdropService.Refresh(this);
        bool firstShow = !_visible;
        _visible = true;
        ++_fadeVersion; // cancel a pending fade-out before updating the existing HUD
        var visual = ElementCompositionPreview.GetElementVisual(Root);
        visual.StopAnimation("Opacity");
        visual.Opacity = 1;
        if (firstShow && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0, 0);
            fade.InsertKeyFrame(1, 1);
            fade.Duration = TimeSpan.FromMilliseconds(150);
            visual.StartAnimation("Opacity", fade);
        }

        _hide.Stop();
        _hide.Start();
    }

    private void FadeOut()
    {
        if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            _appWindow.Hide();
            _visible = false;
            return;
        }
        int version = ++_fadeVersion;
        var visual = ElementCompositionPreview.GetElementVisual(Root);
        var batch = visual.Compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 0);
        fade.Duration = TimeSpan.FromMilliseconds(150);
        visual.StartAnimation("Opacity", fade);
        batch.Completed += (_, _) =>
        {
            batch.Dispose();
            if (version != _fadeVersion) return;
            _appWindow.Hide();
            _visible = false;
        };
        batch.End();
    }

    private double Scale => GetDpiForWindow(_hwnd) / 96.0;
    private int PhysicalWidth => (int)Math.Ceiling(Width * Scale);
    private int PhysicalHeight => (int)Math.Ceiling(Height * Scale);

    private int _primeAttempts;

    private void PrimeHidden()
    {
        if (_islandReady || _visible) return;
        _primeAttempts++;
        int ex = GetWindowLong(_hwnd, -20);
        SetWindowLong(_hwnd, -20, ex & ~0x08000000); // Activate cannot attach XAML while NOACTIVATE is set
        SetWindowPos(_hwnd, new IntPtr(-1), -32000, -32000, PhysicalWidth, PhysicalHeight, 0x0010);
        try
        {
            _appWindow.Move(new Windows.Graphics.PointInt32(-32000, -32000));
            Activate();
        }
        catch (Exception) { }
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => FinishPrime(ex));
    }

    private void FinishPrime(int previousExStyle)
    {
        _islandReady = HasContentBridge();
        SetWindowLong(_hwnd, -20, previousExStyle | 0x08000000);
        if (!_visible)
            _appWindow.Hide();
        _placed = false;
        PlaceOnce();
        if (_islandReady || _primeAttempts >= 2 || _visible) return;
        var retry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        retry.Tick += (_, _) =>
        {
            retry.Stop();
            PrimeHidden();
        };
        retry.Start();
    }

    /// <summary>First on-screen show, when sign-in priming did not attach the XAML island.</summary>
    private void AttachIsland()
    {
        int ex = GetWindowLong(_hwnd, -20);
        SetWindowLong(_hwnd, -20, ex & ~0x08000000);
        try { Activate(); }
        catch (Exception) { }
        SetWindowLong(_hwnd, -20, ex | 0x08000000);
        _islandReady = true;
    }

    private bool HasContentBridge()
    {
        bool found = false;
        EnumChildProc callback = (hwnd, _) =>
        {
            var name = new System.Text.StringBuilder(256);
            GetClassName(hwnd, name, name.Capacity);
            string cls = name.ToString();
            if (cls.Contains("DesktopWindowContentBridge", StringComparison.Ordinal)
                || cls.Contains("InputSite", StringComparison.Ordinal))
                found = true;
            return true;
        };
        EnumChildWindows(_hwnd, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return found;
    }

    private void PlaceOnce()
    {
        int x, y;
        _appWindow.ResizeClient(new Windows.Graphics.SizeInt32(PhysicalWidth, PhysicalHeight));
        int width = _appWindow.Size.Width, height = _appWindow.Size.Height;
        try
        {
            var area = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
            var work = area.WorkArea;
            if (work.Width < width || work.Height < height) return;
            x = work.X + (work.Width - width) / 2;
            y = work.Y + work.Height - height - (int)(100 * Scale);
        }
        catch (Exception)
        {
            int screenW = GetSystemMetrics(0);
            int screenH = GetSystemMetrics(1);
            if (screenW < width || screenH < height) return;
            x = Math.Max(0, (screenW - width) / 2);
            y = Math.Max(0, screenH - height - (int)(100 * Scale));
        }
        SetWindowPos(_hwnd, new IntPtr(-1), x, y, width, height, 0x0010); // NOACTIVATE
        _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
        _placed = true;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

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
    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hwnd, EnumChildProc callback, IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);
}
