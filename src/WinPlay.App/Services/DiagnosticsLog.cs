// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Concurrent;

namespace WinPlay.App.Services;

/// <summary>
/// Bounded, thread-safe rolling log of recent session events for a diagnostics view.
/// Keeps the last <see cref="Capacity"/> entries with timestamps; oldest drop off.
/// </summary>
public sealed class DiagnosticsLog
{
    public const int Capacity = 200;

    public readonly record struct Entry(DateTime TimestampUtc, string Destination, string Message);

    private readonly ConcurrentQueue<Entry> _entries = new();
    private readonly object _fileGate = new();
    private readonly bool _writeStartupLog = Environment.GetEnvironmentVariable("WINPLAY_STARTUP_LOG") == "1";

    public event Action<Entry>? EntryAdded;

    public void Add(string destination, string message)
    {
        var entry = new Entry(DateTime.UtcNow, destination, message);
        _entries.Enqueue(entry);
        while (_entries.Count > Capacity && _entries.TryDequeue(out _)) { }
        if (_writeStartupLog) WriteStartupLog(entry);
        EntryAdded?.Invoke(entry);
    }

    private void WriteStartupLog(Entry entry)
    {
        // Opt-in local diagnostics for a test run; never store media payloads.
        if (entry.Message.Contains("event:") || entry.Message.Contains("feedback")) return;
        try
        {
            lock (_fileGate)
            {
                string directory = Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData), "WinPlay");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "startup-timing.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1_048_576)
                    File.Move(path, path + ".previous", overwrite: true);
                File.AppendAllText(path, $"{entry.TimestampUtc:O} [{Environment.ProcessId}] "
                    + $"[{entry.Destination}] {entry.Message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public IReadOnlyList<Entry> Snapshot() => _entries.ToArray();

    public string Export() =>
        string.Join(Environment.NewLine, Snapshot()
            .Select(e => $"{e.TimestampUtc:HH:mm:ss.fff}  [{e.Destination}]  {e.Message}"));
}
