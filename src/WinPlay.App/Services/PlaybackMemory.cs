// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;

namespace WinPlay.App.Services;

/// <summary>
/// Remembers the last HomePod and the volume set before it was disconnected.
/// </summary>
public static class PlaybackMemory
{
    private sealed class State
    {
        public string? LastKey { get; set; }
        public string? LastName { get; set; }
        public Dictionary<string, double> VolumeByKey { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> VolumeByName { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WinPlay", "playback.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly object Gate = new();
    private static State _state = Load();

    public static double GetVolume(string key, string name)
    {
        lock (Gate)
        {
            if (_state.VolumeByKey.TryGetValue(key, out double byKey)) return Clamp(byKey);
            if (!string.IsNullOrWhiteSpace(name) && _state.VolumeByName.TryGetValue(name, out double byName))
                return Clamp(byName);
            return 20;
        }
    }

    public static void RememberVolume(string key, string name, double percent)
    {
        lock (Gate)
        {
            _state.VolumeByKey[key] = Clamp(percent);
            if (!string.IsNullOrWhiteSpace(name))
                _state.VolumeByName[name] = Clamp(percent);
            Save();
        }
    }

    public static void RememberDevice(string key, string name)
    {
        lock (Gate)
        {
            _state.LastKey = key;
            _state.LastName = name;
            Save();
        }
    }

    public static bool IsPreferred(string key, string name)
    {
        lock (Gate)
        {
            if (!string.IsNullOrEmpty(_state.LastKey))
                return string.Equals(_state.LastKey, key, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(_state.LastName, name, StringComparison.OrdinalIgnoreCase);
            return string.Equals(name, "游戏室", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static double Clamp(double percent) => Math.Clamp(percent, 0, 100);

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

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_state, Json));
        }
        catch (Exception) { }
    }
}
