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

    public static bool UseGlass { get; private set; } = true;

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
