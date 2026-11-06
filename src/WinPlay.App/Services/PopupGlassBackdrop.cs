// SPDX-License-Identifier: GPL-3.0-or-later
// DWM initialization informed by WinUIEx (MIT), Copyright (c) 2021 Morten Nielsen.
// See THIRD-PARTY-NOTICES.md for the retained license.
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Wuc = Windows.UI.Composition;

namespace WinPlay.App.Services;

/// <summary>Popup-only glass whose complete surface, including blur, can fade to transparency.</summary>
internal sealed class PopupGlassBackdrop : SystemBackdrop
{
    private ICompositionSupportsSystemBackdrop? _target;
    private XamlRoot? _root;
    private Wuc.Compositor? _compositor;
    private Wuc.CompositionEffectFactory? _factory;
    private Wuc.CompositionEffectBrush? _effect;
    private Wuc.CompositionBackdropBrush? _host;
    private Wuc.CompositionColorBrush? _fallback;
    private readonly Windows.UI.ViewManagement.UISettings _settings = new();
    private readonly Windows.UI.ViewManagement.AccessibilitySettings _accessibility = new();
    private Microsoft.UI.Dispatching.DispatcherQueue? _queue;
    private bool _hostAvailable;
    private int _generation;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        _target = target;
        _root = xamlRoot;
        _queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        ++_generation;
        _settings.ColorValuesChanged += OnSettingsChanged;
        _settings.AdvancedEffectsEnabledChanged += OnSettingsChanged;

        _queue?.EnsureSystemDispatcherQueue();
        _compositor = new Wuc.Compositor();
        _fallback = _compositor.CreateColorBrush();
        // AppWindowId for a Win32-backed window is mapped via the documented Win32 interop API.
        var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(xamlRoot.ContentIslandEnvironment.AppWindowId);
        _hostAvailable = ConfigureHost(hwnd);
        if (_hostAvailable)
        {
            try
            {
                var blur = new GaussianBlurEffect
                {
                    Name = "Blur", BlurAmount = 20f, BorderMode = EffectBorderMode.Hard,
                    Source = new Wuc.CompositionEffectSourceParameter("desktop")
                };
                var tint = new ColorSourceEffect { Name = "Tint", Color = Color.FromArgb(0, 0, 0, 0) };
                var composite = new CompositeEffect { Mode = CanvasComposite.SourceOver };
                composite.Sources.Add(blur);
                composite.Sources.Add(tint);
                var opacity = new OpacityEffect { Name = "Surface", Opacity = 1f, Source = composite };
                _factory = _compositor.CreateEffectFactory(opacity, new[] { "Blur.BlurAmount", "Tint.Color", "Surface.Opacity" });
                _effect = _factory.CreateBrush();
                _host = _compositor.CreateHostBackdropBrush();
                _effect.SetSourceParameter("desktop", _host);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
            {
                _hostAvailable = false;
                Log($"effect-unavailable type={ex.GetType().Name} hr=0x{ex.HResult:X8}");
            }
        }
        Refresh();
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        // The base notification can reject an obsolete target during theme changes on WinAppSDK 1.7.
        if (target is null || xamlRoot is null || !ReferenceEquals(_target, target) || !ReferenceEquals(_root, xamlRoot)) return;
        Refresh();
    }

    private void OnSettingsChanged(Windows.UI.ViewManagement.UISettings sender, object args)
    {
        int generation = _generation;
        _queue?.TryEnqueue(() => { if (generation == _generation && _target is not null) Refresh(); });
    }

    internal void Refresh()
    {
        if (_target is null || _fallback is null) return;
        bool dark = (_root?.Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark;
        bool highContrast = _accessibility.HighContrast;
        Color neutral = dark ? Color.FromArgb(255, 31, 32, 37) : Color.FromArgb(255, 248, 249, 252);
        _fallback.Color = highContrast
            ? _settings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background) : neutral;
        bool useEffect = _hostAvailable && _effect is not null && _settings.AdvancedEffectsEnabled && !highContrast;
        if (!useEffect)
        {
            _target.SystemBackdrop = _fallback;
            Log($"popup-glass state=fallback highContrast={highContrast} advanced={_settings.AdvancedEffectsEnabled} host={_hostAvailable}");
            return;
        }

        int clarity = CardMaterialSettings.Transparency;
        var material = AcrylicMaterial.FromTransparency(clarity);
        float opacity = AcrylicMaterial.PopupBackdropOpacity(clarity);
        // Above 70 the host blur also reduces, so the background becomes progressively clear.
        float blurAmount = 20f * opacity;
        Color tint = Color.FromArgb((byte)Math.Round(material.TintOpacity * 255), neutral.R, neutral.G, neutral.B);
        _effect!.Properties.InsertScalar("Blur.BlurAmount", blurAmount);
        _effect.Properties.InsertColor("Tint.Color", tint);
        _effect.Properties.InsertScalar("Surface.Opacity", opacity);
        _target.SystemBackdrop = _effect;
        Log($"popup-glass state=active transparency={clarity} opacity={opacity:F3} blur={blurAmount:F2} tintAlpha={tint.A} theme={(dark ? "Dark" : "Light")}");
    }

    private static bool ConfigureHost(IntPtr hwnd)
    {
        // Public DWM host-backdrop support begins with Windows 11 (build 22000).
        if (hwnd == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return false;
        int enable = 1;
        if (DwmSetWindowAttribute(hwnd, 17, ref enable, sizeof(int)) != 0) return false;
        var margins = new Margins();
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        IntPtr region = CreateRectRgn(-2, -2, -1, -1);
        if (region == IntPtr.Zero) return false;
        try
        {
            var blur = new BlurBehind { Flags = 3, Enable = true, Region = region };
            return DwmEnableBlurBehindWindow(hwnd, ref blur) == 0;
        }
        finally { DeleteObject(region); }
    }

    private static void Log(string message)
    {
        string? path = Environment.GetEnvironmentVariable("WINPLAY_UI_LOG_PATH");
        if (string.IsNullOrWhiteSpace(path)) return;
        try { File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}\n"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        ++_generation;
        _settings.ColorValuesChanged -= OnSettingsChanged;
        _settings.AdvancedEffectsEnabledChanged -= OnSettingsChanged;
        target.SystemBackdrop = null;
        _host?.Dispose();
        _effect?.Dispose();
        _factory?.Dispose();
        _fallback?.Dispose();
        _compositor?.Dispose();
        _host = null;
        _effect = null;
        _factory = null;
        _fallback = null;
        _compositor = null;
        _target = null;
        _root = null;
        _queue = null;
        base.OnTargetDisconnected(target);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlurBehind
    {
        public uint Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool Enable;
        public IntPtr Region;
        [MarshalAs(UnmanagedType.Bool)] public bool TransitionOnMaximized;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref BlurBehind blur);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);
}
