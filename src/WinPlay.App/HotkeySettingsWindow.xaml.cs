// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Reflection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinPlay.App.Services;
using WinRT.Interop;

namespace WinPlay.App;

/// <summary>Edits the three volume chords. Press a key in a box, or type Ctrl+PgUp / VolumeUp.</summary>
public sealed partial class HotkeySettingsWindow : Window
{
    private const int WindowWidth = 760;
    private const int WindowHeight = 520;
    private bool _loading = true;

    public Action? MaterialChanged { get; set; }

    public HotkeySettingsWindow()
    {
        InitializeComponent();
        IntPtr hwnd = WindowNative.GetWindowHandle(this);
        var app = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd));
        if (app.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = false;
        }
        int corner = 2;
        DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(SettingsTitleBar);
        // DWM draws its frame outside SetWindowRgn; suppress that second outline
        // while retaining the native caption buttons and drag behavior.
        int borderColor = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(hwnd, 34 /*DWMWA_BORDER_COLOR*/, ref borderColor, sizeof(int));
        ApplyMaterial();
        double scale = GetDpiForWindow(hwnd) / 96.0;
        var area = DisplayArea.GetFromWindowId(app.Id, DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min(area.Width, (int)Math.Ceiling(WindowWidth * scale));
        int height = Math.Min(area.Height, (int)Math.Ceiling(WindowHeight * scale));
        app.MoveAndResize(new Windows.Graphics.RectInt32(
            area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2, width, height));
        LoadBoxes(HotkeySettings.VolumeUp.Text, HotkeySettings.VolumeDown.Text, HotkeySettings.Mute.Text);
        StartupSwitch.IsOn = StartupManager.IsEnabled();
        GlassSwitch.IsOn = CardMaterialSettings.UseGlass;
        OpacitySlider.Value = CardMaterialSettings.Transparency;
        OpacityText.Text = CardMaterialSettings.Transparency.ToString();
        OpacitySlider.IsEnabled = GlassSwitch.IsOn;
        foreach (ComboBoxItem item in ThemeSelector.Items)
            if (item.Tag as string == ThemeService.Current.ToString()) ThemeSelector.SelectedItem = item;
        string textChoice = CardMaterialSettings.TextHex ?? "auto";
        foreach (ComboBoxItem item in TextColorSelector.Items)
            if (item.Tag as string == textChoice) TextColorSelector.SelectedItem = item;
        if (TextColorSelector.SelectedItem is null)
        {
            var custom = new ComboBoxItem { Content = $"自定义 #{textChoice}", Tag = textChoice };
            TextColorSelector.Items.Add(custom);
            TextColorSelector.SelectedItem = custom;
        }
        string accentChoice = CardMaterialSettings.AccentHex ?? "auto";
        foreach (ComboBoxItem item in AccentColorSelector.Items)
            if (item.Tag as string == accentChoice) AccentColorSelector.SelectedItem = item;
        if (AccentColorSelector.SelectedItem is null)
        {
            var custom = new ComboBoxItem { Content = $"自定义 #{accentChoice}", Tag = accentChoice };
            AccentColorSelector.Items.Add(custom);
            AccentColorSelector.SelectedItem = custom;
        }
        switch (AirPlayStreamingSettings.Current)
        {
            case AirPlayStreamingSettings.Mode.Low: BufferLow.IsChecked = true; break;
            case AirPlayStreamingSettings.Mode.Normal: BufferNormal.IsChecked = true; break;
            case AirPlayStreamingSettings.Mode.Buffered: BufferBuffered.IsChecked = true; break;
            default: BufferRealTime.IsChecked = true; break;
        }
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.3";
        VersionText.Text = $"WinPlay {version}";
        _loading = false;
        NavGeneral.IsChecked = true;
    }

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || GeneralPage is null || sender is not RadioButton button || button.IsChecked != true) return;
        string page = button.Tag as string ?? "general";
        GeneralPage.Visibility = page == "general" ? Visibility.Visible : Visibility.Collapsed;
        AudioPage.Visibility = page == "audio" ? Visibility.Visible : Visibility.Collapsed;
        ShortcutsPage.Visibility = page == "shortcuts" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = page == "about" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        StartupManager.SetEnabled(StartupSwitch.IsOn);
    }

    private void OnGlassToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        CardMaterialSettings.Set(GlassSwitch.IsOn);
        OpacitySlider.IsEnabled = GlassSwitch.IsOn;
        ApplyMaterial();
        MaterialChanged?.Invoke();
    }

    private void OnOpacityChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading) return;
        int value = (int)Math.Round(OpacitySlider.Value);
        OpacityText.Text = value.ToString();
        CardMaterialSettings.SetTransparency(value);
        WindowBackdropService.Refresh(this);
        if (!GlassSwitch.IsOn) return;
        MaterialChanged?.Invoke();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeSelector.SelectedItem is not ComboBoxItem item) return;
        if (Enum.TryParse<ThemeService.ThemeChoice>(item.Tag as string, out var choice))
            ThemeService.Set(choice);
    }

    public void ApplyMaterial() => WindowBackdropService.ApplySettings(this);

    internal void PreviewPage(int page)
    {
        switch (page)
        {
            case 0: NavGeneral.IsChecked = true; break;
            case 1: NavAudio.IsChecked = true; break;
            case 2: NavShortcuts.IsChecked = true; break;
            case 3: NavAbout.IsChecked = true; break;
        }
    }

    private void OnTextColorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || TextColorSelector.SelectedItem is not ComboBoxItem item || item.Tag is not string choice) return;
        CardMaterialSettings.SetTextHex(choice == "auto" ? null : choice);
        TextColorSettings.Apply();
    }

    private void OnAccentColorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AccentColorSelector.SelectedItem is not ComboBoxItem item || item.Tag is not string choice) return;
        CardMaterialSettings.SetAccentHex(choice);
        TextColorSettings.Apply();
    }

    private void OnBufferChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton button || button.IsChecked != true) return;
        if (button.Tag is string name && Enum.TryParse(name, out AirPlayStreamingSettings.Mode mode))
            AirPlayStreamingSettings.Set(mode);
    }

    private void OnOpenGitHub(object sender, RoutedEventArgs e) => Open("https://github.com/chenzai666/WinPlay");

    private void OnOpenIssues(object sender, RoutedEventArgs e) => Open("https://github.com/chenzai666/WinPlay/issues/new/choose");

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void LoadBoxes(string up, string down, string mute)
    {
        UpBox.Text = up;
        DownBox.Text = down;
        MuteBox.Text = mute;
        ErrorText.Text = "";
    }

    private void OnChordChanged(object? sender, EventArgs e)
    {
        if (ErrorText is not null) ErrorText.Text = "";
    }

    private void OnReset(object sender, RoutedEventArgs e) =>
        LoadBoxes(HotkeyChord.DefaultUp.Text, HotkeyChord.DefaultDown.Text, HotkeyChord.DefaultMute.Text);

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var up = HotkeyChord.Parse(UpBox.Text);
        var down = HotkeyChord.Parse(DownBox.Text);
        var mute = HotkeyChord.Parse(MuteBox.Text);
        if (up is null || down is null || mute is null)
        {
            ErrorText.Text = "有无法识别的快捷键。用 Ctrl、Alt、Shift、Win 加上键名，例如 Ctrl+PgUp 或 VolumeUp。";
            return;
        }
        if (up.Value.SameKey(down.Value) || up.Value.SameKey(mute.Value) || down.Value.SameKey(mute.Value))
        {
            ErrorText.Text = "三个快捷键不能相同。";
            return;
        }
        HotkeySettings.Save(up.Value.Text, down.Value.Text, mute.Value.Text);
        Close();
    }

}
