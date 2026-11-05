// SPDX-License-Identifier: GPL-3.0-or-later
namespace WinPlay.App.Services;

/// <summary>
/// System dark acrylic for the device card and the volume popup.
/// On by default so the card matches the project screenshots. Off follows the system theme.
/// </summary>
public static class CardMaterialSettings
{
    private static readonly string DirectoryPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinPlay");

    private static readonly string Path = System.IO.Path.Combine(DirectoryPath, "card-glass.txt");
    private static readonly string OpacityPath = System.IO.Path.Combine(DirectoryPath, "card-opacity.txt");
    private static readonly string TextPath = System.IO.Path.Combine(DirectoryPath, "card-text.txt");

    public static bool UseGlass { get; private set; } = true;

    /// <summary>RRGGBB. Black by default so the text stays readable on clear glass.</summary>
    public static string TextHex { get; private set; } = "000000";

    /// <summary>0 is the heaviest frost, 100 is blur with almost no tint. 75 is a step clearer than the shell default.</summary>
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
            // The first pink preset was FF2D55, which reads as red.
            bool migrated = hex.Equals("FF2D55", StringComparison.OrdinalIgnoreCase);
            if (migrated)
                hex = "F4A5B0";
            if (hex.Length == 6 && IsHex(hex))
                TextHex = hex.ToUpperInvariant();
            if (migrated)
                SetTextHex(TextHex);
        }
        catch (Exception) { }
    }

    public static void SetTextHex(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6 || !IsHex(hex)) return;
        TextHex = hex.ToUpperInvariant();
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(TextPath, TextHex);
        }
        catch (Exception) { }
    }

    private static bool IsHex(string hex)
    {
        foreach (char c in hex)
        {
            bool digit = c is >= '0' and <= '9';
            bool upper = c is >= 'A' and <= 'F';
            bool lower = c is >= 'a' and <= 'f';
            if (!digit && !upper && !lower) return false;
        }
        return true;
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
