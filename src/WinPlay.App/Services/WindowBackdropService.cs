// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace WinPlay.App.Services;

/// <summary>One system-managed material per window; never overlay an opaque tint on the backdrop.</summary>
public static class WindowBackdropService
{
    public static void ApplyAcrylic(Window window)
    {
        ThemeService.Register(window);
        if (!DesktopAcrylicController.IsSupported())
        {
            ApplySolid(window);
            return;
        }
        if (window.SystemBackdrop is not CardAcrylicBackdrop)
            window.SystemBackdrop = new CardAcrylicBackdrop();
        Refresh(window);
    }

    public static void ApplyMica(Window window)
    {
        ThemeService.Register(window);
        if (!MicaController.IsSupported())
        {
            ApplySolid(window);
            return;
        }
        if (window.SystemBackdrop is not MicaBackdrop)
            window.SystemBackdrop = new MicaBackdrop();
        Refresh(window);
    }

    public static void ApplySolid(Window window)
    {
        ThemeService.Register(window);
        window.SystemBackdrop = null;
        Refresh(window);
    }

    public static void Refresh(Window window)
    {
        if (window.Content is not Panel root) return;
        // The XAML root stays clear so it cannot cover the real desktop acrylic.
        bool dark = root.ActualTheme == ElementTheme.Dark;
        Color surface = window.SystemBackdrop is not null
            ? Microsoft.UI.Colors.Transparent
            : new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast
                ? new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Background)
                : dark ? Color.FromArgb(255, 31, 32, 37) : Color.FromArgb(255, 248, 249, 252);
        if (root.Background is not SolidColorBrush brush || brush.Color != surface)
            root.Background = new SolidColorBrush(surface);
        if (window.SystemBackdrop is CardAcrylicBackdrop acrylic)
            acrylic.Refresh();
        if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
        {
            var bar = window.AppWindow.TitleBar;
            bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.ButtonForegroundColor = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast
                ? new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground)
                : dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        }
    }
}

/// <summary>Uses the supported WinUI lifecycle and system policy fallback.</summary>
internal sealed class CardAcrylicBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;
    private XamlRoot? _root;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        _root = xamlRoot;
        _controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Base };
        _controller.AddSystemBackdropTarget(target);
        UpdateConfiguration(target, xamlRoot);
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);
        UpdateConfiguration(target, xamlRoot);
    }

    private void UpdateConfiguration(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        if (_controller is null) return;
        var configuration = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
        // The HUD intentionally shows without input focus. The controller still
        // respects transparency, high contrast, energy saver and remote-session policy.
        configuration.IsInputActive = true;
        _controller.SetSystemBackdropConfiguration(configuration);
        Refresh();
    }

    internal void Refresh()
    {
        if (_controller is null) return;
        bool dark = (_root?.Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark;
        float clarity = CardMaterialSettings.Transparency / 100.0f;
        _controller.TintColor = dark ? Color.FromArgb(255, 31, 32, 37) : Color.FromArgb(255, 248, 249, 252);
        _controller.TintOpacity = 0.8f - 0.65f * clarity;
        _controller.LuminosityOpacity = 0.95f - 0.3f * clarity;
        _controller.FallbackColor = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast
            ? new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Background)
            : dark ? Color.FromArgb(255, 31, 32, 37) : Color.FromArgb(255, 248, 249, 252);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        if (_controller is not null)
        {
            _controller.RemoveSystemBackdropTarget(target);
            _controller.Dispose();
            _controller = null;
        }
        _root = null;
        base.OnTargetDisconnected(target);
    }
}
