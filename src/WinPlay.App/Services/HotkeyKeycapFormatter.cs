// SPDX-License-Identifier: GPL-3.0-or-later
namespace WinPlay.App.Services;

/// <summary>将快捷键文字拆为展示用键帽，不改变原始输入。</summary>
public static class HotkeyKeycapFormatter
{
    public static string[] Split(string? text) => string.IsNullOrWhiteSpace(text)
        ? []
        : text.Replace('-', '+').Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
