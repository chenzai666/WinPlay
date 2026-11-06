// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinPlay.App.ViewModels;
using WinRT.Interop;

namespace WinPlay.App;

/// <summary>
/// The quick-settings–style flyout: borderless, Acrylic ("liquid glass"), rounded,
/// always-on-top, anchored above the taskbar tray corner, hidden on focus loss.
/// </summary>
public sealed partial class FlyoutWindow : Window
{
    private const int BaseWidth = 384;
    private const int BaseMaxHeight = 560;

    public MainViewModel ViewModel { get; }

    private readonly AppWindow _appWindow;
    private bool _suppressDeactivateHide;
    // A show that never receives foreground (launcher is not the foreground
    // process) still raises Deactivated. Hide only after the user has actually
    // focused the card, otherwise the window vanishes before they can see it.
    private bool _allowDismiss;
    public FlyoutWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        IntPtr hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        _appWindow.IsShownInSwitchers = false;

        var presenter = (OverlappedPresenter)_appWindow.Presenter;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(true, false);

        // Win11 rounded corners for a borderless popup.
        int cornerPreference = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33 /*DWMWA_WINDOW_CORNER_PREFERENCE*/,
            ref cornerPreference, sizeof(int));
        int borderColor = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE: one system outline, no extra stroke.
        DwmSetWindowAttribute(hwnd, 34 /*DWMWA_BORDER_COLOR*/, ref borderColor, sizeof(int));
        Services.WindowShapeService.Rounded(this);
        ApplyMaterial();

        Activated += OnActivated;
        if (Content is FrameworkElement sized)
            sized.SizeChanged += (_, _) =>
            {
                if (!_placing) PositionNearTray();
            };
        if (Content is UIElement root)
        {
            // Arm click-away dismiss only after a real click on the card.
            // Show/Activate from startup would otherwise count as focus and
            // the next deactivate would hide the window before it is seen.
            root.AddHandler(UIElement.PointerPressedEvent,
                new PointerEventHandler((_, _) => _allowDismiss = true), true);
        }
        ViewModel.RequestPin = ShowPinDialogAsync;
    }

    public Action? OpenSettings { get; set; }

    private void OnOpenSettings(object sender, RoutedEventArgs e) => OpenSettings?.Invoke();

    /// <summary>Thin acrylic, or a solid card when frosted glass is turned off.</summary>
    public void ApplyMaterial()
    {
        if (Services.CardMaterialSettings.UseGlass)
            Services.WindowBackdropService.ApplyAcrylic(this);
        else
            Services.WindowBackdropService.ApplySolid(this);
    }

    /// <summary>
    /// Modal PIN entry for first-time pairing with a PIN-protected receiver. Runs on
    /// the UI thread regardless of the caller; keeps the flyout open while shown.
    /// </summary>
    private Task<string?> ShowPinDialogAsync(string deviceName)
    {
        var completion = new TaskCompletionSource<string?>();
        bool queued = DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                _suppressDeactivateHide = true;
                if (!_appWindow.IsVisible) Toggle();

                var pinBox = new TextBox
                {
                    PlaceholderText = "PIN",
                    MaxLength = 4,
                    Width = 160,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                var dialog = new ContentDialog
                {
                    Title = $"Pair with “{deviceName}”",
                    Content = new StackPanel
                    {
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = $"Enter the PIN shown on {deviceName}. You only need to do this once.",
                                TextWrapping = TextWrapping.Wrap,
                            },
                            pinBox,
                        },
                    },
                    PrimaryButtonText = "Pair",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = Content.XamlRoot,
                };
                var result = await dialog.ShowAsync();
                completion.TrySetResult(result == ContentDialogResult.Primary ? pinBox.Text?.Trim() : null);
            }
            catch (Exception)
            {
                completion.TrySetResult(null);
            }
            finally
            {
                _suppressDeactivateHide = false;
            }
        });
        if (!queued) completion.TrySetResult(null);
        return completion.Task;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        Services.WindowBackdropService.Refresh(this);
        if (args.WindowActivationState == WindowActivationState.Deactivated
            && _allowDismiss
            && !_suppressDeactivateHide && _appWindow.IsVisible)
        {
            AnimateAndHide();
        }
    }

    public bool IsVisible => _appWindow.IsVisible;

    public void Toggle()
    {
        if (_appWindow.IsVisible) AnimateAndHide();
        else ShowNearTray();
    }

    /// <summary>Shows the flyout anchored above the tray with an iOS-style fade + slide-up.</summary>
    public void ShowNearTray()
    {
        _closing = false;
        PositionNearTray();
        _suppressDeactivateHide = true;
        _appWindow.Show();
        Activate();
        Services.WindowBackdropService.Refresh(this);
        _suppressDeactivateHide = false;
        AnimateIn();
    }

    public void HideFlyout() => AnimateAndHide();

    private bool _closing;
    private int _animationVersion;

    private Visual RootVisual => ElementCompositionPreview.GetElementVisual(CardMotion);

    /// <summary>
    /// Smooth, continuous fade + slide-up + subtle grow of the whole card, one ease-out
    /// curve over the full motion (no spring bounce). WinUI's system acrylic backdrop is
    /// painted by the window and can't be animated as content, so the card content carries
    /// the motion; keeping the fade opacity-led from 0 keeps the reveal continuous.
    /// </summary>
    private void AnimateIn()
    {
        ++_animationVersion;
        var root = CardMotion;
        var visual = RootVisual;
        if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            visual.StopAnimation("Opacity");
            visual.StopAnimation("Offset");
            visual.StopAnimation("Scale");
            visual.Opacity = 1;
            visual.Offset = Vector3.Zero;
            visual.Scale = Vector3.One;
            return;
        }
        var c = visual.Compositor;
        var size = root.ActualSize;
        visual.CenterPoint = new Vector3(size.X / 2f, size.Y, 0f); // grow from the bottom (tray) edge

        // Windows-flyout-like ease-out: fast start, gentle deceleration, no overshoot.
        var ease = c.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));
        var dur = TimeSpan.FromMilliseconds(180);

        visual.Opacity = 0f;
        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, ease);
        fade.Duration = dur;
        visual.StartAnimation("Opacity", fade);

        visual.Offset = new Vector3(0f, 12f, 0f);
        var slide = c.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0f, new Vector3(0f, 12f, 0f));
        slide.InsertKeyFrame(1f, Vector3.Zero, ease);
        slide.Duration = dur;
        visual.StartAnimation("Offset", slide);

        visual.Scale = new Vector3(0.98f, 0.98f, 1f);
        var scale = c.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0f, new Vector3(0.98f, 0.98f, 1f));
        scale.InsertKeyFrame(1f, Vector3.One, ease);
        scale.Duration = dur;
        visual.StartAnimation("Scale", scale);
    }

    /// <summary>Fade out + slide down, then hide the window when the animation completes (~140 ms).</summary>
    private void AnimateAndHide()
    {
        if (_closing) return;
        _closing = true;
        int version = ++_animationVersion;
        if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            _appWindow.Hide();
            return;
        }
        var visual = RootVisual;
        var c = visual.Compositor;
        var ease = c.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(1f, 1f)); // ease-in
        var dur = TimeSpan.FromMilliseconds(150);

        var batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);

        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 0f, ease); fade.Duration = dur;
        visual.StartAnimation("Opacity", fade);

        var slide = c.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(1f, new Vector3(0f, 16f, 0f), ease); slide.Duration = dur;
        visual.StartAnimation("Offset", slide);

        batch.Completed += (_, _) =>
        {
            batch.Dispose();
            if (_closing && version == _animationVersion) _appWindow.Hide();
        };
        batch.End();
    }

    private bool _placing;

    private void PositionNearTray()
    {
        if (_placing) return;
        _placing = true;
        try
        {
            PlaceNearTray();
        }
        finally
        {
            _placing = false;
        }
    }

    private void PlaceNearTray()
    {
        var display = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var work = display.WorkArea;

        double scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
        int width = (int)(BaseWidth * scale);
        int maxHeight = (int)(BaseMaxHeight * scale);
        int margin = (int)(12 * scale);

        // Size to the content. ActualHeight follows the window, so a star row
        // never shrinks and leaves a tall gray slab with a seam across it.
        int height = maxHeight;
        if (Content is FrameworkElement root)
        {
            root.Measure(new Windows.Foundation.Size(BaseWidth, BaseMaxHeight));
            if (root.DesiredSize.Height > 0)
                height = Math.Min(maxHeight, (int)Math.Ceiling(root.DesiredSize.Height * scale));
        }

        // Keep the requested client size while allowing DWM its non-client border.
        Services.PopupWindowService.ResizeClient(_appWindow, WindowNative.GetWindowHandle(this), width, height);
        var outer = _appWindow.Size;
        int x = work.X + work.Width - outer.Width - margin;
        int y = work.Y + work.Height - outer.Height - margin;
        _appWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute,
        ref int value, int size);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
