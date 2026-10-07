// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Win32;

namespace WinPlay.App.Services;

/// <summary>
/// "Start with Windows" via the per-user Run key (no admin, no scheduled task). Points at
/// the current executable so a moved/updated install keeps working after the user re-toggles.
/// </summary>
public static class StartupManager
{
    public const string StartupArgument = "--startup";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WinPlay";

    /// <summary>Windows sign-in launches the Run key. That command includes <see cref="StartupArgument"/>.</summary>
    public static bool IsStartupLaunch(string? arguments = null)
    {
        if (ContainsStartup(arguments)) return true;
        foreach (string arg in Environment.GetCommandLineArgs())
            if (IsStartupToken(arg)) return true;
        return false;
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch (Exception) { return false; }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                string command = CommandLine();
                if (command.Length > 0) key.SetValue(ValueName, command);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception) { /* best effort — the menu reflects the real state on next open */ }
    }

    /// <summary>Keep an already-enabled Run entry pointed at this exe and marked as a sign-in start.</summary>
    public static void RefreshCommand()
    {
        if (!IsEnabled()) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return;
            string desired = CommandLine();
            if (desired.Length == 0) return;
            if (!string.Equals(key.GetValue(ValueName) as string, desired, StringComparison.Ordinal))
                key.SetValue(ValueName, desired);
        }
        catch (Exception) { }
    }

    private static string CommandLine()
    {
        string exe = Environment.ProcessPath ?? "";
        return exe.Length == 0 ? "" : $"\"{exe}\" {StartupArgument}";
    }

    private static bool ContainsStartup(string? arguments)
    {
        if (string.IsNullOrEmpty(arguments)) return false;
        foreach (string part in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (IsStartupToken(part)) return true;
        return false;
    }

    private static bool IsStartupToken(string arg) =>
        string.Equals(arg, StartupArgument, StringComparison.OrdinalIgnoreCase);
}
