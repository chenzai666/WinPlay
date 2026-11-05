// SPDX-License-Identifier: GPL-3.0-or-later
namespace WinPlay.App.Services;

/// <summary>
/// Acrylic preference for the device card and volume HUD; all surfaces follow the selected theme.
/// </summary>
public static class CardMaterialSettings
{
    private static readonly string DirectoryPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinPlay");

    private static readonly string Path = System.IO.Path.Combine(DirectoryPath, "card-glass.txt");
    private static readonly string OpacityPath = System.IO.Path.Combine(DirectoryPath, "card-opacity.txt");
    private static readonly string TextPath = System.IO.Path.Combine(DirectoryPath, "card-text.txt");
    private static readonly string AccentPath = System.IO.Path.Combine(DirectoryPath, "card-accent.txt");

    public static bool UseGlass { get; private set; } = true;

    /// <summary>字体颜色的 RRGGBB 值；null 表示跟随主题。</summary>
    public static string? TextHex { get; private set; }

    /// <summary>强调颜色的 RRGGBB 值；null 表示跟随主题。</summary>
    public static string? AccentHex { get; private set; }

    /// <summary>0 is the strongest tint, 100 is the clearest acrylic. System policy controls the blur.</summary>
    public static int Transparency { get; private set; } = 75;

    static CardMaterialSettings()
    {
        try
        {
            string text = File.ReadAllText(Path).Trim();
            if (text == "0") UseGlass = false;
            else if (text == "1") UseGlass = true;
        }
        catch (Exception) { }

        try
        {
            if (int.TryParse(File.ReadAllText(OpacityPath).Trim(), out int value))
                Transparency = Math.Clamp(value, 0, 100);
        }
        catch (Exception) { }

        try
        {
            string hex = File.ReadAllText(TextPath).Trim().TrimStart('#');
            if (IsHex(hex)) TextHex = hex.ToUpperInvariant();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        try
        {
            string hex = File.ReadAllText(AccentPath).Trim().TrimStart('#');
            if (IsHex(hex)) AccentHex = hex.ToUpperInvariant();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

    }

    public static void SetTextHex(string? hex)
    {
        hex = hex?.Trim().TrimStart('#');
        if (!string.IsNullOrEmpty(hex) && !IsHex(hex)) return;
        TextHex = string.IsNullOrEmpty(hex) ? null : hex.ToUpperInvariant();
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(TextPath, TextHex ?? "auto");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool IsHex(string hex) => hex.Length == 6 &&
        hex.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    public static void SetAccentHex(string? hex)
    {
        hex = hex?.Trim().TrimStart('#');
        if (!string.IsNullOrEmpty(hex) && !hex.Equals("auto", StringComparison.OrdinalIgnoreCase) && !IsHex(hex)) return;
        AccentHex = string.IsNullOrEmpty(hex) || hex.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? null : hex.ToUpperInvariant();
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(AccentPath, AccentHex ?? "auto");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void Set(bool useGlass)
    {
        UseGlass = useGlass;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(Path, useGlass ? "1" : "0");
        }
        catch (Exception) { }
    }

    public static void SetTransparency(int transparency)
    {
        Transparency = Math.Clamp(transparency, 0, 100);
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(OpacityPath, Transparency.ToString());
        }
        catch (Exception) { }
    }
}
