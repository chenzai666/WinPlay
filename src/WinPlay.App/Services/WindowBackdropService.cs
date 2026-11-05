// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using Windows.UI;

namespace WinPlay.App.Services;

/// <summary>
/// Frosted glass for the tray card and the volume HUD.
/// On this OS the WinUI acrylic controller paints a solid plate. The island is
/// kept clear and the visible frost is an accent blur whose tint follows
/// <see cref="CardMaterialSettings.Transparency"/>.
/// </summary>
public static class WindowBackdropService
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmsbtNone = 1;
    private const int DwmsbtTransient = 3;
    private const int AccentDisabled = 0;
    private const int AccentBlur = 3;
    private const int AccentAcrylic = 4;

    public static void ApplyAcrylic(Window window)
    {
        Prepare(window, transparent: true);
        // Rewriting the backdrop type while the window is open replaces the blur
        // with a solid plate. The slider only needs a new veil.
        if (window.SystemBackdrop is not ClearIslandBackdrop)
            Present(window);
        else
            Refresh(window);
    }

    public static void ApplySolid(Window window)
    {
        window.SystemBackdrop = null;
        Prepare(window, transparent: false);
        ApplyShell(window, glass: false);
    }

    /// <summary>
    /// Win11 transient acrylic, the same blur Windows Terminal uses.
    /// The slider only changes a veil on top of that blur. It does not clear the backdrop.
    /// </summary>
    public static void Present(Window window)
    {
        if (!CardMaterialSettings.UseGlass) return;
        if (window.SystemBackdrop is not ClearIslandBackdrop)
            window.SystemBackdrop = new ClearIslandBackdrop();
        if (window.SystemBackdrop is ClearIslandBackdrop island)
            island.EnsureClear();
        ExtendFrame(window, DwmsbtTransient);
        Refresh(window);
    }

    /// <summary>Change the veil only. Touching the backdrop type while open paints a solid frame.</summary>
    public static void Refresh(Window window)
    {
        if (!CardMaterialSettings.UseGlass) return;
        Color veil = Veil(CardMaterialSettings.Transparency);
        Paint(window, veil);
        if (window.SystemBackdrop is ClearIslandBackdrop island)
            island.Match(veil);
    }

    /// <summary>A theme-brush edit must not replace the blur with a solid plate.</summary>
    public static void Restore(Window window) => Refresh(window);

    /// <summary>
    /// 0 is the heaviest frost, 100 is the clearest. The clear end still keeps a veil,
    /// so a white page cannot blow the card out to pure white.
    /// </summary>
    internal static Color Veil(int transparency)
    {
        transparency = Math.Clamp(transparency, 0, 100);
        const int heaviest = 0xD8;
        const int clearest = 0x58;
        int alpha = heaviest - transparency * (heaviest - clearest) / 100;
        return Color.FromArgb((byte)alpha, 0x1C, 0x1C, 0x1E);
    }

    /// <summary>What the veil looks like over a white page. The fallback plate uses this, so a failed blur matches.</summary>
    internal static Color FrostOverWhite(Color veil)
    {
        int keep = 255 - veil.A;
        byte Mix(byte channel) => (byte)((channel * veil.A + 255 * keep) / 255);
        return Color.FromArgb(255, Mix(veil.R), Mix(veil.G), Mix(veil.B));
    }

    private static void Paint(Window window, Color color)
    {
        if (window.Content is not Panel panel) return;
        if (panel.Background is SolidColorBrush brush && brush.Color == color) return;
        panel.Background = new SolidColorBrush(color);
    }

    private static void ExtendFrame(Window window, int backdrop)
    {
        IntPtr hwnd = WindowNative.GetWindowHandle(window);
        int dark = 1;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        // Once, before the window is shown. Repeating NONE while it is open paints a solid frame.
        DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    private static void ApplyShell(Window window, bool glass)
    {
        IntPtr hwnd = WindowNative.GetWindowHandle(window);
        int dark = 1;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        int backdrop = DwmsbtNone;
        DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        ApplyAccent(hwnd, glass ? TintAlpha(CardMaterialSettings.Transparency) : 0, glass);
    }

    /// <summary>
    /// 0 is the heaviest frost, 100 is the clearest. Even the clearest keeps most of
    /// the card's own dark color, so a light or dark wallpaper cannot flip the card.
    /// </summary>
    private static int TintAlpha(int transparency)
    {
        transparency = Math.Clamp(transparency, 0, 100);
        const int heaviest = 0xF0;
        const int clearest = 0xA8;
        return heaviest - transparency * (heaviest - clearest) / 100;
    }

    private static void ApplyAccent(IntPtr hwnd, int alpha, bool glass)
    {
        int state = !glass ? AccentDisabled : alpha <= 0 ? AccentBlur : AccentAcrylic;
        // ABGR #1C1C1E. The card's own frost, not a tint taken from the wallpaper.
        uint color = (uint)((alpha & 0xFF) << 24) | 0x001E1C1C;
        var policy = new AccentPolicy { State = state, Flags = 0, Color = color, Animation = 0 };
        IntPtr mem = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>());
        try
        {
            Marshal.StructureToPtr(policy, mem, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = 19,
                Data = mem,
                Size = Marshal.SizeOf<AccentPolicy>(),
            };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(mem);
        }
    }

    private static void Prepare(Window window, bool transparent)
    {
        if (window.Content is not FrameworkElement element) return;
        // The frost stays dark on any wallpaper, so text uses the dark theme.
        // Skip a repeat assign: setting the theme again flashes the solid theme fill.
        var theme = ElementTheme.Dark;
        if (element.RequestedTheme != theme)
            element.RequestedTheme = theme;
        if (element is Panel panel)
        {
            var color = transparent
                ? Veil(CardMaterialSettings.Transparency)
                : Color.FromArgb(255, 28, 28, 30);
            if (panel.Background is not SolidColorBrush brush || brush.Color != color)
                panel.Background = new SolidColorBrush(color);
        }
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int State;
        public int Flags;
        public uint Color;
        public int Animation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int Size;
    }
}

/// <summary>
/// Clears the XAML island without drawing a tint. The visible material is DWM.
/// </summary>
sealed class ClearIslandBackdrop : SystemBackdrop
{
    private static readonly Color Clear = Color.FromArgb(0, 0, 0, 0);
    private DesktopAcrylicController? _controller;

    /// <summary>
    /// Values set before <see cref="DesktopAcrylicController.AddSystemBackdropTarget"/>
    /// never reach the brush. A later theme change builds a new brush from the
    /// light-theme fallback, which is the solid plate. Set the clear tint after
    /// every connect and every configuration change.
    /// </summary>
    public void EnsureClear() => ApplyClear(_controller);

    /// <summary>Keep a failed blur the same shade as the veil over a white page.</summary>
    public void Match(Color veil)
    {
        if (_controller is null) return;
        _controller.TintOpacity = 0;
        _controller.LuminosityOpacity = 0;
        _controller.TintColor = Clear;
        _controller.FallbackColor = WindowBackdropService.FrostOverWhite(veil);
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        _controller = new DesktopAcrylicController();
        _controller.AddSystemBackdropTarget(connectedTarget);
        Push(connectedTarget, xamlRoot);
        var queue = xamlRoot.Content?.DispatcherQueue;
        queue?.TryEnqueue(EnsureClear);
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);
        Push(target, xamlRoot);
        // WinUI builds the light fallback brush after this returns. Clear it on the next ticks.
        var queue = xamlRoot.Content?.DispatcherQueue;
        queue?.TryEnqueue(() =>
        {
            EnsureClear();
            queue.TryEnqueue(EnsureClear);
        });
    }

    private void Push(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        if (_controller is null) return;
        SystemBackdropConfiguration config = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
        config.Theme = SystemBackdropTheme.Dark;
        config.IsInputActive = true;
        _controller.SetSystemBackdropConfiguration(config);
        ApplyClear(_controller);
    }

    private static void ApplyClear(DesktopAcrylicController? controller)
    {
        if (controller is null) return;
        controller.TintOpacity = 0;
        controller.LuminosityOpacity = 0;
        controller.TintColor = Clear;
        controller.FallbackColor = WindowBackdropService.FrostOverWhite(
            WindowBackdropService.Veil(CardMaterialSettings.Transparency));
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        if (_controller is null) return;
        _controller.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller.Dispose();
        _controller = null;
    }
}
