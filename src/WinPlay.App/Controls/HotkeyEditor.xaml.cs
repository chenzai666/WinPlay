// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;
using WinPlay.App.Services;
using Windows.System;

namespace WinPlay.App.Controls;

public sealed partial class HotkeyEditor : UserControl
{
    public event EventHandler? TextChanged;

    public HotkeyEditor()
    {
        InitializeComponent();
        UpdatePreview();
    }

    public string Text
    {
        get => Input.Text;
        set => Input.Text = value ?? "";
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        PreviewButton.Visibility = Visibility.Collapsed;
        Input.Visibility = Visibility.Visible;
        Input.Focus(FocusState.Programmatic);
        Input.SelectAll();
    }

    private void OnInputLostFocus(object sender, RoutedEventArgs e)
    {
        Input.Visibility = Visibility.Collapsed;
        PreviewButton.Visibility = Visibility.Visible;
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePreview();
        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdatePreview()
    {
        if (Keycaps is null || Input is null) return;
        string[] parts = HotkeyKeycapFormatter.Split(Input.Text);
        Keycaps.ItemsSource = parts.Length == 0 ? new[] { "点击设置" } : parts;
        AutomationProperties.SetName(PreviewButton, parts.Length == 0
            ? "设置快捷键" : $"编辑快捷键 {string.Join(" 加 ", parts)}");
        AutomationProperties.SetName(Input, "输入快捷键");
    }

    private void OnChordKey(object sender, KeyRoutedEventArgs e)
    {
        if (IsModifier(e.Key)) { e.Handled = true; return; }
        if (e.Key == VirtualKey.Escape) { PreviewButton.Visibility = Visibility.Visible; Input.Visibility = Visibility.Collapsed; PreviewButton.Focus(FocusState.Programmatic); e.Handled = true; return; }
        if (e.Key == VirtualKey.Tab) return;
        bool ctrl = Down(VirtualKey.Control, VirtualKey.LeftControl, VirtualKey.RightControl);
        bool alt = Down(VirtualKey.Menu, VirtualKey.LeftMenu, VirtualKey.RightMenu);
        bool shift = Down(VirtualKey.Shift, VirtualKey.LeftShift, VirtualKey.RightShift);
        bool win = Down(VirtualKey.LeftWindows, VirtualKey.RightWindows);
        if (!ctrl && !alt && !win && (IsEditingKey(e.Key) || IsTypingKey(e.Key))) return;
        int mods = 0;
        if (ctrl) mods |= HotkeyChord.ModControl;
        if (alt) mods |= HotkeyChord.ModAlt;
        if (shift) mods |= HotkeyChord.ModShift;
        if (win) mods |= HotkeyChord.ModWin;
        Input.Text = HotkeyChord.Format(mods, (int)e.Key);
        e.Handled = true;
    }

    private static bool Down(params VirtualKey[] keys) => keys.Any(key =>
        ((int)InputKeyboardSource.GetKeyStateForCurrentThread(key) & 1) != 0);
    private static bool IsEditingKey(VirtualKey key) => key is VirtualKey.Back or VirtualKey.Delete or
        VirtualKey.Left or VirtualKey.Right or VirtualKey.Home or VirtualKey.End;
    private static bool IsTypingKey(VirtualKey key)
    {
        int vk = (int)key;
        return vk is >= 'A' and <= 'Z' or >= '0' and <= '9' or 0xBB or 0xBD ||
            key is VirtualKey.Add or VirtualKey.Subtract or VirtualKey.Space;
    }
    private static bool IsModifier(VirtualKey key) => key is VirtualKey.Control or VirtualKey.LeftControl or
        VirtualKey.RightControl or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift or
        VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu or VirtualKey.LeftWindows or VirtualKey.RightWindows;
}
