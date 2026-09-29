// SPDX-License-Identifier: GPL-3.0-or-later
using NAudio.CoreAudioApi;

namespace WinPlay.Core.Audio;

/// <summary>A keyboard volume gesture observed on the Windows endpoint while it is locked.</summary>
public enum VolumeGesture
{
    Up,
    Down,
    Mute,
}

/// <summary>
/// Moves the PC's audio to AirPlay the way a Mac does: while streaming, it mutes the local
/// output endpoint (so the speakers are silent and there is no ~2 s echo against the
/// receiver) and captures the system mix via <see cref="ProcessLoopbackCapture"/>, which
/// keeps working even though the endpoint is muted. On the last destination stopping, the
/// speakers are restored to their previous state.
///
/// Hardware volume keys usually change that endpoint directly and never arrive as a hotkey.
/// While streaming, the saved level is held: any nudge is snapped back and reported as
/// <see cref="VolumeGesture"/> so the AirPlay volume can move instead.
///
/// If process-loopback capture is unavailable (older Windows), it falls back to ordinary
/// endpoint loopback and does <em>not</em> mute — audio still streams, just without the
/// local‑mute behaviour.
/// </summary>
public sealed class SystemAudioMover : IDisposable
{
    private readonly object _lock = new();
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private bool _processLoopbackSupported = true;
    private bool _muting;
    private float _savedVol = 1f;
    private float _holdVol = 1f;
    private DateTime _cooldownUtc = DateTime.MinValue;
    private int _watchStarted;
    private bool _wantLock;
    private static readonly string MuteMarker = Path.Combine(Path.GetTempPath(), "winplay-endpoint-muted");

    /// <summary>Fired on a background thread when a locked endpoint was nudged by a volume key.</summary>
    public event Action<VolumeGesture>? VolumeGestureDetected;

    public SystemAudioMover()
    {
        // A previous run can be killed while the endpoint is still muted.
        if (!File.Exists(MuteMarker)) return;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.Mute = false;
            File.Delete(MuteMarker);
        }
        catch (Exception) { /* no endpoint yet */ }
    }

    /// <summary>Mutes the local output endpoint and freezes its level (idempotent).</summary>
    public void EnterStreaming()
    {
        lock (_lock)
        {
            if (!_processLoopbackSupported) return;
            _wantLock = true;
            TryArmLocked();
            if (Interlocked.Exchange(ref _watchStarted, 1) == 0)
            {
                var thread = new Thread(WatchLoop)
                {
                    IsBackground = true,
                    Name = "WinPlay volume lock",
                };
                thread.Start();
            }
        }
    }

    /// <summary>Caller holds <see cref="_lock"/>. Arms when the endpoint exists; false if audio is not up yet.</summary>
    private bool TryArmLocked()
    {
        if (_muting) return true;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var endpoint = device.AudioEndpointVolume;
            _savedVol = endpoint.MasterVolumeLevelScalar;
            _holdVol = _savedVol;
            endpoint.MasterVolumeLevelScalar = _holdVol;
            endpoint.Mute = true;
            try { File.WriteAllText(MuteMarker, "1"); } catch (Exception) { }
            _muting = true;
            _cooldownUtc = DateTime.UtcNow.AddMilliseconds(350);
            Trace("armed vol=" + _holdVol.ToString("0.00"));
            return true;
        }
        catch (Exception ex)
        {
            Trace("arm waiting: " + ex.Message);
            return false;
        }
    }

    /// <summary>Restores the local endpoint's prior volume and mute state.</summary>
    public void ExitStreaming()
    {
        lock (_lock)
        {
            _wantLock = false;
            if (!_muting) return;
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var endpoint = device.AudioEndpointVolume;
                endpoint.MasterVolumeLevelScalar = _savedVol;
                endpoint.Mute = false;
                try { File.Delete(MuteMarker); } catch (Exception) { }
            }
            catch (Exception) { /* endpoint gone; nothing to restore */ }
            _muting = false;
            Trace("disarmed");
        }
    }

    private static void Trace(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "winplay-volume.log"),
                DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
        }
        catch (Exception) { }
    }

    /// <summary>
    /// Creates an audio source for a streaming session. Prefers process-loopback capture
    /// (excludes WinPlay itself, survives the endpoint mute); falls back to endpoint
    /// loopback if process loopback can't start, disabling the local mute for this run.
    /// </summary>
    public IAudioSource CreateCaptureSource()
    {
        if (_processLoopbackSupported)
        {
            // At login the audio engine can still be starting. Keep trying so a
            // boot launch does not permanently fall back to unlocked speakers.
            Exception? last = null;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                try
                {
                    var source = new ProcessLoopbackAudioSource(_ownPid);
                    EnterStreaming();
                    return source;
                }
                catch (Exception ex) when (!IsPermanentCaptureFailure(ex))
                {
                    last = ex;
                    if (attempt == 15) break;
                    Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    last = ex;
                    break;
                }
            }
            Trace("process loopback gave up: " + last?.Message);
            lock (_lock) { _processLoopbackSupported = false; }
            ExitStreaming();
        }
        return new LoopbackAudioSource();
    }

    private static bool IsPermanentCaptureFailure(Exception ex) =>
        ex is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException
        || ex.InnerException is DllNotFoundException or EntryPointNotFoundException;

    public void Dispose() => ExitStreaming();

    private void WatchLoop()
    {
        while (true)
        {
            bool want;
            bool armed;
            lock (_lock)
            {
                want = _wantLock && _processLoopbackSupported;
                armed = _muting;
                if (want && !armed)
                    armed = TryArmLocked();
            }
            if (!want || !armed)
            {
                Thread.Sleep(500);
                continue;
            }

            Thread.Sleep(40);
            VolumeGesture? gesture = null;
            try { gesture = Poll(); }
            catch (Exception) { /* endpoint blip */ }
            if (gesture is { } found)
            {
                Trace("gesture " + found);
                try { VolumeGestureDetected?.Invoke(found); }
                catch (Exception ex) { Trace("gesture listener: " + ex); }
            }
        }
    }

    private VolumeGesture? Poll()
    {
        lock (_lock)
        {
            if (!_muting) return null;
            var now = DateTime.UtcNow;
            if (now < _cooldownUtc)
            {
                Hold();
                return null;
            }

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var endpoint = device.AudioEndpointVolume;
                float cur = endpoint.MasterVolumeLevelScalar;
                bool muted = endpoint.Mute;
                if (cur is < 0f or > 1f)
                {
                    _cooldownUtc = now.AddSeconds(1);
                    return null;
                }

                float delta = cur - _holdVol;
                // Still at the frozen mute point.
                if (muted && Math.Abs(delta) < 0.0015f) return null;
                // Adapter / VPN glitches jump the scalar. Snap back, do not treat as a key.
                if (Math.Abs(delta) >= 0.06f)
                {
                    Hold(endpoint);
                    _cooldownUtc = now.AddSeconds(1.2);
                    return null;
                }

                VolumeGesture gesture;
                if (!muted && Math.Abs(delta) < 0.02f)
                    gesture = VolumeGesture.Up; // mute-key or volume-up cleared our forced mute
                else if (delta > 0.0015f)
                    gesture = VolumeGesture.Up;
                else if (delta < -0.0015f)
                    gesture = VolumeGesture.Down;
                else
                    gesture = VolumeGesture.Mute;

                Hold(endpoint);
                _cooldownUtc = now.AddMilliseconds(250);
                return gesture;
            }
            catch (Exception)
            {
                _cooldownUtc = now.AddSeconds(1);
                return null;
            }
        }
    }

    private void Hold()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            Hold(device.AudioEndpointVolume);
        }
        catch (Exception) { /* next poll retries */ }
    }

    private void Hold(AudioEndpointVolume endpoint)
    {
        endpoint.MasterVolumeLevelScalar = _holdVol;
        endpoint.Mute = true;
    }
}
