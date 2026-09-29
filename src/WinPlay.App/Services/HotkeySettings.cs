// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;

namespace WinPlay.App.Services;

/// <summary>One chord: modifiers plus a virtual key, shown as "Ctrl+PgUp".</summary>
public readonly record struct HotkeyChord(int Modifiers, int VirtualKey, string Text)
{
    public const int ModAlt = 0x0001;
    public const int ModControl = 0x0002;
    public const int ModShift = 0x0004;
    public const int ModWin = 0x0008;

    // Literals on purpose. Parse() needs Names, and these initializers run first.
    public static HotkeyChord DefaultUp { get; } = new(ModControl, 0x21, "Ctrl+PgUp");
    public static HotkeyChord DefaultDown { get; } = new(ModControl, 0x22, "Ctrl+PgDn");
    public static HotkeyChord DefaultMute { get; } = new(ModControl, 0x4D, "Ctrl+M");

    public bool SameKey(HotkeyChord other) =>
        Modifiers == other.Modifiers && VirtualKey == other.VirtualKey;

    public static HotkeyChord? Parse(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        int mods = 0;
        string? key = null;
        foreach (string raw in spec.Replace('-', '+').Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string part = raw.ToLowerInvariant();
            switch (part)
            {
                case "ctrl":
                case "control":
                case "ctl":
                    mods |= ModControl;
                    break;
                case "alt":
                case "menu":
                    mods |= ModAlt;
                    break;
                case "shift":
                    mods |= ModShift;
                    break;
                case "win":
                case "windows":
                case "cmd":
                case "super":
                case "meta":
                    mods |= ModWin;
                    break;
                default:
                    key = part;
                    break;
            }
        }
        if (key is null || !TryVk(key, out int vk)) return null;
        return new HotkeyChord(mods, vk, Format(mods, vk));
    }

    public static string Format(int mods, int vk)
    {
        var parts = new List<string>(5);
        if ((mods & ModControl) != 0) parts.Add("Ctrl");
        if ((mods & ModAlt) != 0) parts.Add("Alt");
        if ((mods & ModShift) != 0) parts.Add("Shift");
        if ((mods & ModWin) != 0) parts.Add("Win");
        parts.Add(VkName(vk));
        return string.Join('+', parts);
    }

    private static bool TryVk(string key, out int vk)
    {
        if (Names.TryGetValue(key, out vk)) return true;
        if (key.StartsWith("key", StringComparison.Ordinal) &&
            int.TryParse(key.AsSpan(3), System.Globalization.NumberStyles.HexNumber, null, out vk))
            return true;
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                vk = c;
                return true;
            }
        }
        vk = 0;
        return false;
    }

    private static string VkName(int vk) => vk switch
    {
        0x21 => "PgUp",
        0x22 => "PgDn",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x20 => "Space",
        0x09 => "Tab",
        0x1B => "Esc",
        0x0D => "Enter",
        0x24 => "Home",
        0x23 => "End",
        0x2D => "Insert",
        0x2E => "Delete",
        0x08 => "Backspace",
        0xAF => "VolumeUp",
        0xAE => "VolumeDown",
        0xAD => "VolumeMute",
        >= 0x70 and <= 0x7B => "F" + (vk - 0x6F),
        >= 'A' and <= 'Z' => ((char)vk).ToString(),
        >= '0' and <= '9' => ((char)vk).ToString(),
        _ => "Key" + vk.ToString("X"),
    };

    private static Dictionary<string, int>? _names;
    private static Dictionary<string, int> Names => _names ??= BuildNames();

    private static Dictionary<string, int> BuildNames()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["pgup"] = 0x21, ["pageup"] = 0x21, ["prior"] = 0x21,
            ["pgdn"] = 0x22, ["pagedown"] = 0x22, ["pagedn"] = 0x22, ["next"] = 0x22,
            ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
            ["space"] = 0x20, ["tab"] = 0x09, ["esc"] = 0x1B, ["escape"] = 0x1B,
            ["enter"] = 0x0D, ["return"] = 0x0D, ["home"] = 0x24, ["end"] = 0x23,
            ["insert"] = 0x2D, ["ins"] = 0x2D, ["delete"] = 0x2E, ["del"] = 0x2E,
            ["backspace"] = 0x08, ["bksp"] = 0x08,
            ["volumeup"] = 0xAF, ["volup"] = 0xAF, ["volume_up"] = 0xAF, ["media_volume_up"] = 0xAF,
            ["volumedown"] = 0xAE, ["voldown"] = 0xAE, ["volume_down"] = 0xAE, ["media_volume_down"] = 0xAE,
            ["volumemute"] = 0xAD, ["volmute"] = 0xAD, ["volume_mute"] = 0xAD, ["media_mute"] = 0xAD,
        };
        for (int i = 1; i <= 12; i++) map["f" + i] = 0x6F + i;
        for (int i = 0; i <= 9; i++) map[i.ToString()] = 0x30 + i;
        for (char c = 'a'; c <= 'z'; c++) map[c.ToString()] = char.ToUpperInvariant(c);
        return map;
    }
}

/// <summary>The three volume chords. Stored in %AppData%\WinPlay\hotkeys.json.</summary>
public static class HotkeySettings
{
    private sealed class State
    {
        public string VolumeUp { get; set; } = "Ctrl+PgUp";
        public string VolumeDown { get; set; } = "Ctrl+PgDn";
        public string Mute { get; set; } = "Ctrl+M";
    }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WinPlay", "hotkeys.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly object Gate = new();
    private static State _state = Load();

    public static HotkeyChord VolumeUp => Get(_state.VolumeUp, HotkeyChord.DefaultUp);
    public static HotkeyChord VolumeDown => Get(_state.VolumeDown, HotkeyChord.DefaultDown);
    public static HotkeyChord Mute => Get(_state.Mute, HotkeyChord.DefaultMute);

    public static void Save(string up, string down, string mute)
    {
        lock (Gate)
        {
            _state.VolumeUp = up;
            _state.VolumeDown = down;
            _state.Mute = mute;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_state, Json));
        }
    }

    private static HotkeyChord Get(string text, HotkeyChord fallback) =>
        HotkeyChord.Parse(text) ?? fallback;

    private static State Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath), Json) ?? new State();
        }
        catch (Exception) { }
        return new State();
    }
}
