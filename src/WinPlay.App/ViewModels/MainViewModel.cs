// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using WinPlay.App.Services;
using WinPlay.Core.Discovery;

namespace WinPlay.App.ViewModels;

/// <summary>
/// Owns discovery + streaming. Projects browser snapshots into picker rows on the UI
/// thread (updated in place, keyed by group/device ID) and turns row actions into
/// <see cref="StreamController"/> sessions.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly AirPlayBrowser _browser;
    private readonly StreamController _streams = new();
    private readonly NowPlayingService _nowPlaying;
    private readonly MediaVolumeKeys _mediaKeys = new();
    private readonly Dictionary<string, double> _muteRestore = new();
    private readonly HashSet<string> _manualStop = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherQueue _dispatcher;
    private DateTime _nextAutoConnectUtc = DateTime.MinValue;
    private string _status = "Looking for AirPlay devices…";
    private int _deviceCount;

    public ObservableCollection<PickerRowViewModel> Rows { get; } = [];

    /// <summary>
    /// Set by the view: shows a PIN-entry dialog for the named receiver and returns
    /// the PIN, or null on cancel. Enables first-time Apple TV pairing from the flyout.
    /// </summary>
    public Func<string, Task<string?>>? RequestPin
    {
        get => _streams.PinPrompt;
        set => _streams.PinPrompt = value;
    }

    public MainViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _streams.SessionStage += (key, stage) => _dispatcher.TryEnqueue(() =>
        {
            var row = Rows.FirstOrDefault(r => r.Key == key);
            row?.OnStage(stage);
        });
        _streams.SessionFailed += (key, ex) => _dispatcher.TryEnqueue(() =>
        {
            var row = Rows.FirstOrDefault(r => r.Key == key);
            row?.OnFailed(FriendlyError(ex));
        });

        _browser = new AirPlayBrowser();
        _browser.DevicesChanged += OnDevicesChanged;
        _browser.Start();

        _nowPlaying = new NowPlayingService(_streams);
        _nowPlaying.Start();
        _mediaKeys.VolumeUp += () => NudgeStreamingVolume(5);
        _mediaKeys.VolumeDown += () => NudgeStreamingVolume(-5);
        _mediaKeys.Mute += ToggleStreamingMute;
    }

    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    public bool HasNoDevices => _deviceCount == 0;

    /// <summary>Shows the on-screen volume bar. Set by the app window.</summary>
    public Action<string, double>? ShowVolumeHud { get; set; }

    // ------------------------------------------------------------ row actions

    private async Task OnAudioToggleAsync(PickerRowViewModel row, bool on)
    {
        row.IsBusy = true;
        try
        {
            if (on)
            {
                row.SetStatus("Connecting…");
                PlaybackMemory.RememberDevice(row.Key, row.DisplayName);
                await _streams.StartAudioAsync(row.Entry, PercentToDb(row.VolumePercent), CancellationToken.None);
                _dispatcher.TryEnqueue(() => { row.SetStreamingStatus(); RefreshStatus(); });
            }
            else
            {
                _manualStop.Add(row.Key);
                PlaybackMemory.RememberVolume(row.Key, row.DisplayName, row.VolumePercent);
                await _streams.StopAudioAsync(row.Key);
                _dispatcher.TryEnqueue(() => { row.SetStatus(null); RefreshStatus(); });
            }
        }
        catch (Exception ex)
        {
            _dispatcher.TryEnqueue(() =>
            {
                row.SetAudioCheckedSilently(false);
                row.SetStatus(FriendlyError(ex));
            });
        }
        finally
        {
            _dispatcher.TryEnqueue(() =>
            {
                row.IsBusy = false;
                SyncMediaKeys();
            });
        }
    }

    /// <summary>Keyboard volume keys: step every live HomePod stream. No-op when nothing is streaming.</summary>
    public void NudgeStreamingVolume(int deltaPercent)
    {
        _dispatcher.TryEnqueue(() =>
        {
            foreach (var row in Rows)
            {
                if (!row.IsAudioChecked) continue;
                row.VolumePercent = Math.Clamp(row.VolumePercent + deltaPercent, 0, 100);
            }
            RefreshStatus();
        });
    }

    public void ToggleStreamingMute()
    {
        _dispatcher.TryEnqueue(() =>
        {
            var live = Rows.Where(r => r.IsAudioChecked).ToList();
            if (live.Count == 0) return;
            bool anyAudible = live.Any(r => r.VolumePercent > 0.5);
            foreach (var row in live)
            {
                if (anyAudible)
                {
                    if (row.VolumePercent > 0.5) _muteRestore[row.Key] = row.VolumePercent;
                    row.VolumePercent = 0;
                }
                else
                {
                    row.VolumePercent = _muteRestore.TryGetValue(row.Key, out double saved)
                        ? saved
                        : PlaybackMemory.GetVolume(row.Key, row.DisplayName);
                }
            }
            RefreshStatus();
        });
    }

    private void SyncMediaKeys() =>
        _mediaKeys.SetArmed(Rows.Any(r => r.IsAudioChecked));

    private async Task OnMirrorToggleAsync(PickerRowViewModel row, bool on)
    {
        row.IsBusy = true;
        try
        {
            if (on)
            {
                row.SetStatus("Starting mirroring…");
                await _streams.StartMirrorAsync(row.Entry, CancellationToken.None);
                _dispatcher.TryEnqueue(() => { row.SetStatus("Mirroring your screen"); RefreshStatus(); });
            }
            else
            {
                await _streams.StopMirrorAsync(row.Key);
                _dispatcher.TryEnqueue(() => { row.SetStatus(null); RefreshStatus(); });
            }
        }
        catch (Exception ex)
        {
            _dispatcher.TryEnqueue(() =>
            {
                row.SetMirrorCheckedSilently(false);
                row.SetStatus(FriendlyError(ex));
            });
        }
        finally
        {
            _dispatcher.TryEnqueue(() => row.IsBusy = false);
        }
    }

    private Task OnVolumeAsync(PickerRowViewModel row, double percent)
    {
        PlaybackMemory.RememberVolume(row.Key, row.DisplayName, percent);
        if (row.IsAudioChecked)
            ShowVolumeHud?.Invoke(row.DisplayName, percent);
        return _streams.SetVolumeAsync(row.Key, PercentToDb(percent));
    }

    /// <summary>0 % = AirPlay mute sentinel −144; otherwise linear −30…0 dBFS.</summary>
    private static double PercentToDb(double percent) =>
        percent <= 0.5 ? -144.0 : -30.0 + (percent / 100.0) * 30.0;

    private static string FriendlyError(Exception ex) => ex switch
    {
        OperationCanceledException => "Cancelled",
        TimeoutException => "Couldn't reach the device",
        _ when ex.Message.Contains("no member", StringComparison.OrdinalIgnoreCase) => "Device not ready yet — try again",
        _ when ex.Message.Contains("mirroring", StringComparison.OrdinalIgnoreCase) => ex.Message,
        _ => "Couldn't connect — try again",
    };

    // ------------------------------------------------------------ discovery projection

    private void OnDevicesChanged(IReadOnlyList<AirPlayDevice> devices)
    {
        var entries = DevicePicker.Collapse(devices);
        _dispatcher.TryEnqueue(() => Apply(entries, devices.Count));
    }

    private void Apply(List<PickerEntry> entries, int deviceCount)
    {
        var byKey = Rows.ToDictionary(r => r.Key);
        var seen = new HashSet<string>();

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            seen.Add(entry.Key);
            if (byKey.TryGetValue(entry.Key, out var row))
            {
                row.Update(entry);
                int currentIndex = Rows.IndexOf(row);
                if (currentIndex != i) Rows.Move(currentIndex, i);
            }
            else
            {
                var created = new PickerRowViewModel(entry)
                {
                    AudioToggleRequested = OnAudioToggleAsync,
                    MirrorToggleRequested = OnMirrorToggleAsync,
                    VolumeChanged = OnVolumeAsync,
                };
                created.SetVolumeSilently(PlaybackMemory.GetVolume(entry.Key, entry.DisplayName));
                Rows.Insert(Math.Min(i, Rows.Count), created);
            }
        }
        // Only drop rows that are gone AND not actively streaming (don't yank a live session
        // because discovery briefly missed a device).
        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            if (!seen.Contains(Rows[i].Key)
                && !_streams.IsAudioActive(Rows[i].Key) && !_streams.IsMirroring(Rows[i].Key))
            {
                Rows.RemoveAt(i);
            }
        }

        _deviceCount = deviceCount;
        RefreshStatus();
        OnPropertyChanged(nameof(HasNoDevices));
        TryAutoConnect();
    }

    private void TryAutoConnect()
    {
        if (DateTime.UtcNow < _nextAutoConnectUtc) return;
        if (Rows.Any(r => r.IsAudioChecked || r.IsBusy)) return;

        var candidates = Rows.Where(r => r.IsAudioCapable && !_manualStop.Contains(r.Key)).ToList();
        PickerRowViewModel? target = candidates.FirstOrDefault(r => PlaybackMemory.IsPreferred(r.Key, r.DisplayName));
        if (target is null && candidates.Count == 1)
            target = candidates[0];
        if (target is null) return;

        _nextAutoConnectUtc = DateTime.UtcNow.AddSeconds(20);
        target.SetVolumeSilently(PlaybackMemory.GetVolume(target.Key, target.DisplayName));
        target.IsAudioChecked = true;
    }

    private void RefreshStatus()
    {
        int active = _streams.ActiveCount;
        var streaming = Rows.Where(r => r.IsAudioChecked).ToList();
        Status = streaming.Count > 0
            ? $"正在推流 · 音量 {streaming.Average(r => r.VolumePercent):0}%"
            : active > 0
                ? $"Streaming to {active} destination{(active == 1 ? "" : "s")}"
                : $"{Rows.Count} destination{(Rows.Count == 1 ? "" : "s")} available";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public async ValueTask DisposeAsync()
    {
        _mediaKeys.Dispose();
        _browser.DevicesChanged -= OnDevicesChanged;
        _browser.Dispose();
        await _nowPlaying.DisposeAsync();
        await _streams.DisposeAsync();
    }
}
