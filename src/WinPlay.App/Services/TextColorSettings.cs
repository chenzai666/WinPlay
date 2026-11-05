// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace WinPlay.App.Services;

/// <summary>文字与强调色分别保存、分别应用；自动模式恢复主题原始颜色。</summary>
public static class TextColorSettings
{
    private static readonly Dictionary<SolidColorBrush, Color> Defaults = [];

    public static void Apply()
    {
        if (Application.Current?.Resources is not ResourceDictionary root) return;
        ApplyToDictionary(root, Parse(CardMaterialSettings.TextHex), Parse(CardMaterialSettings.AccentHex));
    }

    private static void ApplyToDictionary(ResourceDictionary dictionary, Color? text, Color? accent)
    {
        foreach (var entry in dictionary.ThemeDictionaries)
        {
            if (entry.Key is not string themeName ||
                (themeName != "Light" && themeName != "Dark") ||
                entry.Value is not ResourceDictionary theme) continue;

            Color? effectiveText = text;
            if (text is Color chosen && chosen.R == 0xF4 && chosen.G == 0xA5 && chosen.B == 0xB0)
            {
                // Keep the saved pink intact; use a darker rose only for light-theme text.
                effectiveText = themeName == "Light"
                    ? Color.FromArgb(255, 0xA3, 0x34, 0x50)
                    : chosen;
            }
            Update(theme, "PrimaryTextBrush", effectiveText);
            Update(theme, "SecondaryTextBrush", effectiveText);
            Update(theme, "NavigationSelectedTextBrush", effectiveText);
            Update(theme, "AccentBrush", accent);
            Update(theme, "CheckedCardBrush", accent, preserveAlpha: true);
            Update(theme, "NavigationSelectedBrush", accent, preserveAlpha: true);
            Update(theme, "AccentFillColorDefaultBrush", accent);
            Update(theme, "AccentFillColorSecondaryBrush", accent, preserveAlpha: true);
            Update(theme, "AccentFillColorTertiaryBrush", accent, preserveAlpha: true);
            Update(theme, "AccentTextFillColorPrimaryBrush", accent);
        }
        foreach (ResourceDictionary merged in dictionary.MergedDictionaries)
            ApplyToDictionary(merged, text, accent);
    }

    private static void Update(ResourceDictionary theme, string key, Color? color, bool preserveAlpha = false)
    {
        if (!theme.ContainsKey(key) || theme[key] is not SolidColorBrush brush) return;
        if (!Defaults.ContainsKey(brush)) Defaults.Add(brush, brush.Color);
        Color original = Defaults[brush];
        brush.Color = color is Color chosen
            ? Color.FromArgb(preserveAlpha ? original.A : chosen.A, chosen.R, chosen.G, chosen.B)
            : original;
    }

    private static Color? Parse(string? hex)
    {
        if (hex is null || hex.Length != 6 || !uint.TryParse(hex,
            System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture,
            out uint value)) return null;
        return Color.FromArgb(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

}
