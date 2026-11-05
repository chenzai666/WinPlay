// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.UI.Xaml;

namespace WinPlay.App.Services;

/// <summary>Shares the selected theme across windows and follows system theme changes.</summary>
public static class ThemeService
{
    public enum ThemeChoice { System, Light, Dark }

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinPlay", "appearance-theme.txt");
    private static readonly HashSet<Window> Windows = [];
    public static ThemeChoice Current { get; private set; } = ReadTheme();

    private static ThemeChoice ReadTheme()
    {
        try
        {
            if (Enum.TryParse<ThemeChoice>(File.ReadAllText(SettingsPath).Trim(), out var choice) && Enum.IsDefined(choice))
                return choice;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return ThemeChoice.System;
    }

    public static void Set(ThemeChoice choice)
    {
        if (!Enum.IsDefined(choice)) return;
        Current = choice;
        foreach (var window in Windows)
        {
            Apply(window);
            WindowBackdropService.Refresh(window);
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, choice.ToString());
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static void Register(Window window)
    {
        if (Windows.Add(window))
        {
            if (window.Content is FrameworkElement root)
                root.ActualThemeChanged += OnActualThemeChanged;
            window.Closed += OnClosed;
        }
        Apply(window);
    }

    private static void Apply(Window window)
    {
        if (window.Content is FrameworkElement root)
            root.RequestedTheme = Current switch
            {
                ThemeChoice.Light => ElementTheme.Light,
                ThemeChoice.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
    }

    private static void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        foreach (var window in Windows)
            if (ReferenceEquals(window.Content, sender)) WindowBackdropService.Refresh(window);
    }

    private static void OnClosed(object sender, WindowEventArgs args)
    {
        if (sender is not Window window) return;
        if (window.Content is FrameworkElement root)
            root.ActualThemeChanged -= OnActualThemeChanged;
        window.Closed -= OnClosed;
        Windows.Remove(window);
    }
}
