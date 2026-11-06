// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;

namespace WinPlay.App.Services;

/// <summary>Applies a brief press response without replacing native control templates.</summary>
public static class InteractionAnimationService
{
    public static readonly DependencyProperty PressEnabledProperty = DependencyProperty.RegisterAttached(
        "PressEnabled", typeof(bool), typeof(InteractionAnimationService),
        new PropertyMetadata(false, OnPressEnabledChanged));

    private static readonly DependencyProperty RegistrationProperty = DependencyProperty.RegisterAttached(
        "Registration", typeof(object), typeof(InteractionAnimationService), new PropertyMetadata(null));

    public static readonly DependencyProperty CardEnabledProperty = DependencyProperty.RegisterAttached(
        "CardEnabled", typeof(bool), typeof(InteractionAnimationService), new PropertyMetadata(false, OnCardEnabledChanged));
    private static readonly DependencyProperty CardRegistrationProperty = DependencyProperty.RegisterAttached(
        "CardRegistration", typeof(object), typeof(InteractionAnimationService), new PropertyMetadata(null));

    private static void Log(string message)
    {
        string? path = Environment.GetEnvironmentVariable("WINPLAY_UI_LOG_PATH");
        if (string.IsNullOrWhiteSpace(path)) return;
        try { File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}\n"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void SetCardEnabled(DependencyObject element, bool value) => element.SetValue(CardEnabledProperty, value);
    public static bool GetCardEnabled(DependencyObject element) => (bool)element.GetValue(CardEnabledProperty);

    private static void OnCardEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not ToggleButton button) return;
        if ((bool)args.NewValue)
        {
            var registration = new CardRegistration(button);
            button.SetValue(CardRegistrationProperty, registration);
            registration.Attach();
        }
        else if (button.GetValue(CardRegistrationProperty) is CardRegistration registration)
        {
            registration.Detach();
            button.ClearValue(CardRegistrationProperty);
        }
    }

    private sealed class CardRegistration(ToggleButton button)
    {
        private readonly SolidColorBrush _background = new();
        private readonly SolidColorBrush _border = new();
        private readonly List<(SolidColorBrush Brush, long Token)> _sources = [];
        private readonly Windows.UI.ViewManagement.UISettings _settings = new();
        private readonly Windows.UI.ViewManagement.AccessibilitySettings _accessibility = new();
        private object? _oldBackground;
        private object? _oldBorder;
        private Storyboard? _backgroundAnimation;
        private Storyboard? _borderAnimation;
        private long _checkedToken;
        private long _enabledToken;
        private bool _active;
        private bool _hover;
        private int _generation;
        private Microsoft.UI.Dispatching.DispatcherQueue? _queue;

        internal void Attach()
        {
            button.Loaded += Loaded;
            button.Unloaded += Unloaded;
            if (button.IsLoaded) Start();
        }

        internal void Detach()
        {
            button.Loaded -= Loaded;
            button.Unloaded -= Unloaded;
            Stop();
        }

        private void Loaded(object sender, RoutedEventArgs e) => Start();
        private void Unloaded(object sender, RoutedEventArgs e) => Stop();

        private void Start()
        {
            if (_active) return;
            _active = true;
            ++_generation;
            _queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            _settings.AnimationsEnabledChanged += AnimationsChanged;
            _settings.ColorValuesChanged += ColorsChanged;
            _oldBackground = button.ReadLocalValue(Control.BackgroundProperty);
            _oldBorder = button.ReadLocalValue(Control.BorderBrushProperty);
            button.Background = _background;
            button.BorderBrush = _border;
            button.PointerEntered += Entered;
            button.PointerExited += Exited;
            button.ActualThemeChanged += ThemeChanged;
            _checkedToken = button.RegisterPropertyChangedCallback(ToggleButton.IsCheckedProperty, CheckedChanged);
            _enabledToken = button.RegisterPropertyChangedCallback(Control.IsEnabledProperty, EnabledChanged);
            SubscribePalette();
            Apply(0);
        }

        private void Stop()
        {
            if (!_active) return;
            _active = false;
            ++_generation;
            _settings.AnimationsEnabledChanged -= AnimationsChanged;
            _settings.ColorValuesChanged -= ColorsChanged;
            _queue = null;
            button.PointerEntered -= Entered;
            button.PointerExited -= Exited;
            button.ActualThemeChanged -= ThemeChanged;
            button.UnregisterPropertyChangedCallback(ToggleButton.IsCheckedProperty, _checkedToken);
            button.UnregisterPropertyChangedCallback(Control.IsEnabledProperty, _enabledToken);
            UnsubscribePalette();
            StopAnimation(ref _backgroundAnimation, _background);
            StopAnimation(ref _borderAnimation, _border);
            Restore(Control.BackgroundProperty, _oldBackground);
            Restore(Control.BorderBrushProperty, _oldBorder);
            _hover = false;
        }

        private void Restore(DependencyProperty property, object? value)
        {
            if (value == DependencyProperty.UnsetValue) button.ClearValue(property);
            else button.SetValue(property, value);
        }

        private void Entered(object sender, PointerRoutedEventArgs args) { _hover = true; Apply(150); }
        private void Exited(object sender, PointerRoutedEventArgs args) { _hover = false; Apply(150); }
        private void CheckedChanged(DependencyObject sender, DependencyProperty property) => Apply(180);
        private void EnabledChanged(DependencyObject sender, DependencyProperty property) => Apply(0);
        private void ThemeChanged(FrameworkElement sender, object args) { SubscribePalette(); Apply(0); }
        private void SourceChanged(DependencyObject sender, DependencyProperty property) => Apply(0);

        private void AnimationsChanged(Windows.UI.ViewManagement.UISettings sender, object args) => QueueSettingsChange(false);
        private void ColorsChanged(Windows.UI.ViewManagement.UISettings sender, object args) => QueueSettingsChange(true);

        private void QueueSettingsChange(bool paletteChanged)
        {
            int generation = _generation;
            _queue?.TryEnqueue(() =>
            {
                if (!_active || generation != _generation) return;
                if (paletteChanged) SubscribePalette();
                Apply(0);
            });
        }

        private ResourceDictionary? Palette()
        {
            if (Application.Current?.Resources is not ResourceDictionary resources) return null;
            string key = _accessibility.HighContrast ? "HighContrast" :
                button.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
            foreach (var dictionary in resources.MergedDictionaries)
                if (dictionary.ThemeDictionaries.TryGetValue(key, out object? value) &&
                    value is ResourceDictionary palette && palette.ContainsKey("CardBackgroundBrush") &&
                    palette.ContainsKey("CheckedCardBrush")) return palette;
            return null;
        }

        private void SubscribePalette()
        {
            UnsubscribePalette();
            var palette = Palette();
            if (palette is null) return;
            foreach (string key in new[] { "CardBackgroundBrush", "CardHoverBrush", "CheckedCardBrush", "CardBorderBrush", "AccentBrush" })
                if (palette.TryGetValue(key, out object? value) && value is SolidColorBrush brush)
                    _sources.Add((brush, brush.RegisterPropertyChangedCallback(SolidColorBrush.ColorProperty, SourceChanged)));
        }

        private void UnsubscribePalette()
        {
            foreach (var source in _sources)
                source.Brush.UnregisterPropertyChangedCallback(SolidColorBrush.ColorProperty, source.Token);
            _sources.Clear();
        }

        private void Apply(int milliseconds)
        {
            if (!_active) return;
            var palette = Palette();
            if (palette is null) return;
            bool selected = button.IsChecked == true;
            string backgroundKey = selected ? "CheckedCardBrush" : _hover && button.IsEnabled ? "CardHoverBrush" : "CardBackgroundBrush";
            string borderKey = selected || (_hover && button.IsEnabled) ? "AccentBrush" : "CardBorderBrush";
            if (palette[backgroundKey] is not SolidColorBrush background || palette[borderKey] is not SolidColorBrush border) return;
            if (!button.IsEnabled || !_settings.AnimationsEnabled || _accessibility.HighContrast) milliseconds = 0;
            Log($"card transition duration={milliseconds} checked={selected} hover={_hover} backgroundFrom={_background.Color} backgroundTo={background.Color} borderFrom={_border.Color} borderTo={border.Color}");
            SetColor(_background, background.Color, milliseconds, ref _backgroundAnimation);
            SetColor(_border, border.Color, milliseconds, ref _borderAnimation);
        }

        private static void StopAnimation(ref Storyboard? animation, SolidColorBrush brush)
        {
            if (animation is null) return;
            Color current = brush.Color;
            animation.Stop();
            animation = null;
            brush.Color = current;
        }

        private static void SetColor(SolidColorBrush brush, Color target, int milliseconds, ref Storyboard? animation)
        {
            StopAnimation(ref animation, brush);
            if (milliseconds == 0 || brush.Color == target) { brush.Color = target; return; }
            var colorAnimation = new ColorAnimation { From = brush.Color, To = target, Duration = TimeSpan.FromMilliseconds(milliseconds) };
            Storyboard.SetTarget(colorAnimation, brush);
            Storyboard.SetTargetProperty(colorAnimation, "Color");
            animation = new Storyboard();
            animation.Children.Add(colorAnimation);
            animation.Begin();
        }
    }

    public static void SetPressEnabled(DependencyObject element, bool value) => element.SetValue(PressEnabledProperty, value);
    public static bool GetPressEnabled(DependencyObject element) => (bool)element.GetValue(PressEnabledProperty);

    private static void OnPressEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not ButtonBase button) return;
        if ((bool)args.NewValue)
        {
            var registration = new Registration(button);
            button.SetValue(RegistrationProperty, registration);
            registration.Attach();
        }
        else if (button.GetValue(RegistrationProperty) is Registration registration)
        {
            registration.Detach();
            button.ClearValue(RegistrationProperty);
        }
    }

    private sealed class Registration(ButtonBase button)
    {
        private long _pressedToken;
        private long _enabledToken;
        private bool _observing;
        private readonly Windows.UI.ViewManagement.UISettings _settings = new();
        private readonly Windows.UI.ViewManagement.AccessibilitySettings _accessibility = new();
        private Microsoft.UI.Dispatching.DispatcherQueue? _queue;
        private int _generation;

        internal void Attach()
        {
            button.Loaded += OnLoaded;
            button.Unloaded += OnUnloaded;
            if (button.IsLoaded) Observe();
        }

        internal void Detach()
        {
            button.Loaded -= OnLoaded;
            button.Unloaded -= OnUnloaded;
            StopObserving();
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => Observe();
        private void OnUnloaded(object sender, RoutedEventArgs e) => StopObserving();

        private void Observe()
        {
            if (_observing) return;
            _observing = true;
            ++_generation;
            _queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            _settings.AnimationsEnabledChanged += OnSettingsChanged;
            _settings.ColorValuesChanged += OnColorsChanged;
            _pressedToken = button.RegisterPropertyChangedCallback(ButtonBase.IsPressedProperty, OnStateChanged);
            _enabledToken = button.RegisterPropertyChangedCallback(Control.IsEnabledProperty, OnStateChanged);
            Update();
        }

        private void StopObserving()
        {
            if (!_observing) return;
            ++_generation;
            _settings.AnimationsEnabledChanged -= OnSettingsChanged;
            _settings.ColorValuesChanged -= OnColorsChanged;
            _queue = null;
            button.UnregisterPropertyChangedCallback(ButtonBase.IsPressedProperty, _pressedToken);
            button.UnregisterPropertyChangedCallback(Control.IsEnabledProperty, _enabledToken);
            _observing = false;
            var visual = ElementCompositionPreview.GetElementVisual(button);
            visual.StopAnimation("Scale");
            visual.Scale = Vector3.One;
        }

        private void OnStateChanged(DependencyObject sender, DependencyProperty property) => Update();

        private void OnSettingsChanged(Windows.UI.ViewManagement.UISettings sender, object args) => QueueSettingsChange();
        private void OnColorsChanged(Windows.UI.ViewManagement.UISettings sender, object args) => QueueSettingsChange();

        private void QueueSettingsChange()
        {
            int generation = _generation;
            _queue?.TryEnqueue(() =>
            {
                if (_observing && generation == _generation) Update();
            });
        }

        private void Update()
        {
            var visual = ElementCompositionPreview.GetElementVisual(button);
            visual.StopAnimation("Scale");
            bool animate = _settings.AnimationsEnabled && !_accessibility.HighContrast;
            Vector3 target = button.IsEnabled && button.IsPressed && animate
                ? new Vector3(0.98f, 0.98f, 1f) : Vector3.One;
            visual.CenterPoint = new Vector3((float)button.ActualWidth / 2f, (float)button.ActualHeight / 2f, 0f);
            if (!animate || !button.IsEnabled)
            {
                Log($"button press duration=0 target=1 enabled={button.IsEnabled} pressed={button.IsPressed}");
                visual.Scale = Vector3.One;
                return;
            }
            var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
            animation.InsertKeyFrame(1f, target);
            animation.Duration = TimeSpan.FromMilliseconds(100);
            Log($"button press duration=100 target={target.X:F2} enabled={button.IsEnabled} pressed={button.IsPressed}");
            visual.StartAnimation("Scale", animation);
        }
    }
}
