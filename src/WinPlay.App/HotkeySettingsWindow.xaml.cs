// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinPlay.App.Services;
using WinRT.Interop;
using Windows.System;

namespace WinPlay.App;

/// <summary>Edits the three volume chords. Press a key in a box, or type Ctrl+PgUp / VolumeUp.</summary>
public sealed partial class HotkeySettingsWindow : Window
{
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
            presenter.IsAlwaysOnTop = true;
        }
        app.Resize(new Windows.Graphics.SizeInt32(460, 340));
        var area = DisplayArea.GetFromWindowId(app.Id, DisplayAreaFallback.Primary).WorkArea;
        app.Move(new Windows.Graphics.PointInt32(
            area.X + (area.Width - 460) / 2,
            area.Y + (area.Height - 340) / 2));
        LoadBoxes(HotkeySettings.VolumeUp.Text, HotkeySettings.VolumeDown.Text, HotkeySettings.Mute.Text);
    }

    private void LoadBoxes(string up, string down, string mute)
    {
        UpBox.Text = up;
        DownBox.Text = down;
        MuteBox.Text = mute;
        ErrorText.Text = "";
    }

    private void OnChordKey(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (IsModifier(e.Key))
        {
            e.Handled = true;
            return;
        }
        if (e.Key == VirtualKey.Escape || e.Key == VirtualKey.Tab)
            return;

        bool ctrl = Down(VirtualKey.Control, VirtualKey.LeftControl, VirtualKey.RightControl);
        bool alt = Down(VirtualKey.Menu, VirtualKey.LeftMenu, VirtualKey.RightMenu);
        bool shift = Down(VirtualKey.Shift, VirtualKey.LeftShift, VirtualKey.RightShift);
        bool win = Down(VirtualKey.LeftWindows, VirtualKey.RightWindows);

        // Plain letters and + - stay editable, so Ctrl+PgUp can be typed as text.
        // A held Ctrl, Alt, or Win, or a non-character key, replaces the whole chord.
        if (!ctrl && !alt && !win && (IsEditingKey(e.Key) || IsTypingKey(e.Key)))
            return;

        int mods = 0;
        if (ctrl) mods |= HotkeyChord.ModControl;
        if (alt) mods |= HotkeyChord.ModAlt;
        if (shift) mods |= HotkeyChord.ModShift;
        if (win) mods |= HotkeyChord.ModWin;

        box.Text = HotkeyChord.Format(mods, (int)e.Key);
        ErrorText.Text = "";
        e.Handled = true;
    }

    private void OnChordFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
            box.SelectAll();
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

    private static bool Down(params VirtualKey[] keys)
    {
        foreach (var key in keys)
        {
            if (((int)InputKeyboardSource.GetKeyStateForCurrentThread(key) & 1) != 0)
                return true;
        }
        return false;
    }

    private static bool IsEditingKey(VirtualKey key) => key is
        VirtualKey.Back or VirtualKey.Delete or
        VirtualKey.Left or VirtualKey.Right or VirtualKey.Home or VirtualKey.End;

    private static bool IsTypingKey(VirtualKey key)
    {
        int vk = (int)key;
        if (vk is >= 'A' and <= 'Z' or >= '0' and <= '9') return true;
        // OEM plus/minus (0xBB/0xBD) plus the numpad add/subtract keys.
        return key is VirtualKey.Add or VirtualKey.Subtract or VirtualKey.Space || vk is 0xBB or 0xBD;
    }

    private static bool IsModifier(VirtualKey key) => key is
        VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl or
        VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift or
        VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu or
        VirtualKey.LeftWindows or VirtualKey.RightWindows;
}
