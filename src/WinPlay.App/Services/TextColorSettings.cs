// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace WinPlay.App.Services;

/// <summary>保留主题原始文字色，手动选择时仅修改浅色和深色画刷。</summary>
public static class TextColorSettings
{
    private static readonly Dictionary<SolidColorBrush, Color> Defaults = [];

    public static void Apply()
    {
        if (Application.Current?.Resources is not ResourceDictionary root) return;
        Color? selected = Parse(CardMaterialSettings.TextHex);
        ApplyToDictionary(root, selected);
    }

    private static void ApplyToDictionary(ResourceDictionary dictionary, Color? selected)
    {
        foreach (var entry in dictionary.ThemeDictionaries)
        {
            if (entry.Key is not string themeName ||
                (themeName != "Light" && themeName != "Dark") ||
                entry.Value is not ResourceDictionary theme) continue;

            Update(theme, "PrimaryTextBrush", selected);
            Update(theme, "SecondaryTextBrush", selected is Color color ? Secondary(color) : null);
        }
        foreach (ResourceDictionary merged in dictionary.MergedDictionaries)
            ApplyToDictionary(merged, selected);
    }

    private static void Update(ResourceDictionary theme, string key, Color? color)
    {
        if (!theme.ContainsKey(key) || theme[key] is not SolidColorBrush brush) return;
        if (!Defaults.ContainsKey(brush)) Defaults.Add(brush, brush.Color);
        brush.Color = color ?? Defaults[brush];
    }

    private static Color? Parse(string? hex)
    {
        if (hex is null || hex.Length != 6 || !uint.TryParse(hex,
            System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture,
            out uint value)) return null;
        return Color.FromArgb(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    private static Color Secondary(Color color)
    {
        double luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
        double toward = luminance < 0.55 ? 80 : 210;
        byte Mix(byte channel) => (byte)Math.Clamp(channel + (toward - channel) * 0.55, 0, 255);
        return Color.FromArgb(255, Mix(color.R), Mix(color.G), Mix(color.B));
    }
}
