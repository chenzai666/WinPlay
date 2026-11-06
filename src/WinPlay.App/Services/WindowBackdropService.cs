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
        if (window.SystemBackdrop is not CardAcrylicBackdrop { UsesAccentTint: false })
            window.SystemBackdrop = new CardAcrylicBackdrop();
        Refresh(window);
    }

    public static void ApplySettings(Window window)
    {
        WindowShapeService.Rounded(window);
        if (!CardMaterialSettings.UseGlass || !DesktopAcrylicController.IsSupported())
        {
            ApplySolid(window);
            return;
        }
        ThemeService.Register(window);
        if (window.SystemBackdrop is not CardAcrylicBackdrop { UsesAccentTint: true })
            window.SystemBackdrop = new CardAcrylicBackdrop(usesAccentTint: true);
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
        WindowShapeService.Refresh(window);
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
    internal bool UsesAccentTint { get; }

    internal CardAcrylicBackdrop(bool usesAccentTint = false) => UsesAccentTint = usesAccentTint;

    private DesktopAcrylicController? _controller;
    private XamlRoot? _root;
    private ICompositionSupportsSystemBackdrop? _target;
    private SystemBackdropConfiguration? _configuration;
    private Windows.UI.Composition.CompositionMaskBrush? _opacityMask;
    private Windows.UI.Composition.CompositionColorBrush? _opacityBrush;
    private Microsoft.UI.Dispatching.DispatcherQueue? _queue;
    private (bool Dark, bool HighContrast, int Transparency, Color Accent)? _lastAppearance;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        _target = target;
        _root = xamlRoot;
        _configuration = new SystemBackdropConfiguration();
        _queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin };
        _controller.StateChanged += OnControllerStateChanged;
        _controller.AddSystemBackdropTarget(target);
        UpdateConfiguration(target, xamlRoot);
        _controller.SetSystemBackdropConfiguration(_configuration);
        UpdatePopupOpacity();
        LogState("connected");
    }

    private void OnControllerStateChanged(DesktopAcrylicController sender, object args)
    {
        _queue?.TryEnqueue(() =>
        {
            if (_controller is null) return;
            UpdatePopupOpacity();
            LogState("state-changed");
        });
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        // The base virtual notification does not need forwarding. On WinAppSDK 1.7
        // forwarding can reject a hidden popup's target during a live theme change.
        if (target is null || xamlRoot is null || _controller is null || _configuration is null ||
            !ReferenceEquals(_root, xamlRoot) || !ReferenceEquals(_target, target)) return;
        UpdateConfiguration(target, xamlRoot);
    }

    private void UpdateConfiguration(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        if (_controller is null || _configuration is null) return;
        var defaults = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
        if (defaults is null) return;
        // WinUI owns defaults and rewrites its activation state when a popup hides.
        // Keep our transient-window policy separate, as required by SystemBackdrop.
        _configuration.Theme = defaults.Theme;
        _configuration.IsHighContrast = defaults.IsHighContrast;
        _configuration.IsInputActive = true;
        Refresh();
        LogState("configuration-changed");
    }

    internal void Refresh()
    {
        if (_controller is null) return;
        bool dark = (_root?.Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark;
        bool highContrast = _configuration?.IsHighContrast == true;
        Color accent = UsesAccentTint ? TextColorSettings.Accent : default;
        var appearance = (dark, highContrast, CardMaterialSettings.Transparency, accent);
        if (_lastAppearance == appearance)
        {
            UpdatePopupOpacity();
            return;
        }
        _lastAppearance = appearance;
        var material = UsesAccentTint
            ? AcrylicMaterial.ForSettings(CardMaterialSettings.Transparency, dark)
            : AcrylicMaterial.FromTransparency(CardMaterialSettings.Transparency);
        Color neutral = dark ? Color.FromArgb(255, 31, 32, 37) : Color.FromArgb(255, 248, 249, 252);
        double accentWeight = dark ? 0.25 : 0.65;
        byte Mix(byte background, byte foreground) => (byte)Math.Round(background * (1 - accentWeight) + foreground * accentWeight);
        Color tint = UsesAccentTint
            ? Color.FromArgb(255, Mix(neutral.R, accent.R), Mix(neutral.G, accent.G), Mix(neutral.B, accent.B))
            : neutral;
        _controller.TintOpacity = material.TintOpacity;
        _controller.LuminosityOpacity = material.LuminosityOpacity;
        _controller.FallbackColor = highContrast
            ? new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Background)
            : neutral;
        // WinAppSDK 1.7 can leave opacity-only changes unapplied (#10717).
        // A one-channel nudge commits the material, without replacing its controller.
        // https://github.com/microsoft/microsoft-ui-xaml/issues/10717
        Color nudge = tint;
        nudge.R = (byte)(tint.R == 255 ? 254 : tint.R + 1);
        _controller.TintColor = nudge;
        _controller.TintColor = tint;
        UpdatePopupOpacity();
        LogState("appearance-changed");
    }

    private void UpdatePopupOpacity()
    {
        if (UsesAccentTint || _controller is null || _target?.SystemBackdrop is not { } current) return;
        // A controller may replace its output when the system material changes.
        // Reuse one mask and never wrap the mask itself. Use the target's compositor,
        // which belongs to Windows.UI.Composition in the App SDK 1.7 projection.
        if (!ReferenceEquals(current, _opacityMask))
        {
            if (_opacityMask is null)
            {
                _opacityBrush = current.Compositor.CreateColorBrush();
                _opacityMask = current.Compositor.CreateMaskBrush();
                _opacityMask.Mask = _opacityBrush;
            }
            _opacityMask.Source = current;
            _target.SystemBackdrop = _opacityMask;
        }
        bool fallback = _configuration?.IsHighContrast == true ||
            _controller.State != SystemBackdropState.Active;
        float opacity = fallback ? 1f : AcrylicMaterial.PopupBackdropOpacity(CardMaterialSettings.Transparency);
        _opacityBrush!.Color = Color.FromArgb((byte)Math.Round(opacity * 255), 255, 255, 255);
    }

    private void LogState(string reason)
    {
        string? path = Environment.GetEnvironmentVariable("WINPLAY_UI_LOG_PATH");
        if (string.IsNullOrWhiteSpace(path) || _controller is null) return;
        try
        {
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {reason} state={_controller.State} theme={_configuration?.Theme} active={_configuration?.IsInputActive} highContrast={_configuration?.IsHighContrast} tint={_controller.TintOpacity:F3} luminosity={_controller.LuminosityOpacity:F3} transparency={CardMaterialSettings.Transparency} backgroundAlpha={_opacityBrush?.Color.A} source={_opacityMask?.Source?.GetType().Name}\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        if (_controller is not null)
        {
            _controller.StateChanged -= OnControllerStateChanged;
            _controller.RemoveSystemBackdropTarget(target);
            _controller.Dispose();
            _controller = null;
        }
        _opacityMask?.Dispose();
        _opacityBrush?.Dispose();
        _opacityMask = null;
        _opacityBrush = null;
        _queue = null;
        _root = null;
        _target = null;
        _configuration = null;
        _lastAppearance = null;
        base.OnTargetDisconnected(target);
    }
}
