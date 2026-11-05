// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace WinPlay.App.Services;

/// <summary>Pushes the chosen text color into the theme brushes the card and settings already use.</summary>
public static class TextColorSettings
{
    public static void Apply()
    {
        if (!TryParse(CardMaterialSettings.TextHex, out Color primary)) return;
        Set("PrimaryTextBrush", primary);
        Set("SecondaryTextBrush", Secondary(primary));
    }

    /// <summary>The color chosen in settings. Volume bar and percent use this; mute stays red.</summary>
    public static Color Primary =>
        TryParse(CardMaterialSettings.TextHex, out Color color)
            ? color
            : Color.FromArgb(255, 0xF4, 0xA5, 0xB0);

    private static void Set(string key, Color color)
    {
        if (Application.Current?.Resources is ResourceDictionary root)
            Walk(root, key, color);
    }

    private static void Walk(ResourceDictionary dict, string key, Color color)
    {
        foreach (var entry in dict.ThemeDictionaries)
        {
            if (entry.Value is ResourceDictionary theme
                && theme.ContainsKey(key)
                && theme[key] is SolidColorBrush brush)
                brush.Color = color;
        }
        foreach (ResourceDictionary merged in dict.MergedDictionaries)
            Walk(merged, key, color);
    }

    private static Color Secondary(Color color)
    {
        double y = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
        double toward = y < 0.55 ? 80 : 210;
        byte Mix(byte channel) => (byte)Math.Clamp(channel + (toward - channel) * 0.55, 0, 255);
        return Color.FromArgb(255, Mix(color.R), Mix(color.G), Mix(color.B));
    }

    private static bool TryParse(string hex, out Color color)
    {
        color = default;
        if (hex.Length != 6) return false;
        try
        {
            color = Color.FromArgb(255,
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
