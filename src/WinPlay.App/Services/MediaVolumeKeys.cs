// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;

namespace WinPlay.App.Services;

/// <summary>
/// While armed, keyboard Volume Up/Down/Mute adjust WinPlay instead of Windows.
/// Unarmed, the keys are released back to the system.
/// </summary>
public sealed class MediaVolumeKeys : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int VkVolumeMute = 0xAD;
    private const int VkVolumeDown = 0xAE;
    private const int VkVolumeUp = 0xAF;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkShift = 0x10;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int ModNoRepeat = 0x4000;
    private const int IdUp = 1;
    private const int IdDown = 2;
    private const int IdMute = 3;
    private const int IdChordUp = 4;
    private const int IdChordDown = 5;
    private const int IdChordMute = 6;
    private const int WmAppArm = 0x8000 + 20;
    private const int WmAppSuspend = 0x8000 + 21;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly HookProc _hookProc;
    private IntPtr _hwnd;
    private IntPtr _hook;
    private bool _armed;
    private bool _wantArmed;
    private bool _suspended;
    private bool _disposed;
    private HotkeyChord _chordUp = HotkeyChord.DefaultUp;
    private HotkeyChord _chordDown = HotkeyChord.DefaultDown;
    private HotkeyChord _chordMute = HotkeyChord.DefaultMute;

    public event Action? VolumeUp;
    public event Action? VolumeDown;
    public event Action? Mute;

    public MediaVolumeKeys()
    {
        _hookProc = LowLevel;
        _thread = new Thread(Loop) { IsBackground = true, Name = "WinPlay volume keys" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(3));
    }

    public void SetArmed(bool armed)
    {
        if (_hwnd == IntPtr.Zero || _disposed) return;
        PostMessage(_hwnd, WmAppArm, armed ? 1 : 0, 0);
    }

    /// <summary>Drop the chords while the settings window is capturing a new one.</summary>
    public void Suspend(bool suspended)
    {
        if (_hwnd == IntPtr.Zero || _disposed) return;
        PostMessage(_hwnd, WmAppSuspend, suspended ? 1 : 0, 0);
    }

    public void Dispose()
    {
        _disposed = true;
        if (_hwnd != IntPtr.Zero)
            PostMessage(_hwnd, 0x0010 /*WM_DESTROY*/, 0, 0);
    }

    private void Loop()
    {
        var proc = new WndProc(WindowProc);
        var wc = new WndClassEx
        {
            cbSize = Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
            hInstance = GetModuleHandle(null),
            lpszClassName = "WinPlayVolumeKeys",
        };
        RegisterClassEx(ref wc);
        _hwnd = CreateWindowEx(0, wc.lpszClassName, "WinPlayVolumeKeys", 0,
            0, 0, 0, 0, new IntPtr(-3) /*HWND_MESSAGE*/, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        _ready.Set();

        while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        RemoveHook();
        Unregister();
        GC.KeepAlive(proc);
        GC.KeepAlive(_hookProc);
    }

    /// <summary>
    /// Hardware volume keys and Ctrl chords are often eaten before RegisterHotKey
    /// (Logitech, Razer, the Windows shell). A low-level hook sees them first.
    /// The body only posts a message; the thread keeps pumping so the keyboard cannot stall.
    /// </summary>
    private IntPtr LowLevel(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && _armed && lParam != IntPtr.Zero)
            {
                int vk = Marshal.ReadInt32(lParam);
                int msg = (int)wParam;
                bool down = msg is 0x0100 or 0x0104;
                bool up = msg is 0x0101 or 0x0105;
                bool ctrl = Down(VkControl);
                bool alt = Down(VkMenu);
                bool shift = Down(VkShift);
                bool win = Down(VkLWin) || Down(VkRWin);
                int cmd = 0;
                if (Matches(_chordUp, vk, ctrl, alt, shift, win)) cmd = IdChordUp;
                else if (Matches(_chordDown, vk, ctrl, alt, shift, win)) cmd = IdChordDown;
                else if (Matches(_chordMute, vk, ctrl, alt, shift, win)) cmd = IdChordMute;
                else if (vk == VkVolumeUp && !ctrl && !alt && !shift && !win) cmd = IdUp;
                else if (vk == VkVolumeDown && !ctrl && !alt && !shift && !win) cmd = IdDown;
                else if (vk == VkVolumeMute && !ctrl && !alt && !shift && !win) cmd = IdMute;
                if (cmd != 0 && (down || up))
                {
                    if (down) PostMessage(_hwnd, WmHotkey, cmd, 0);
                    return (IntPtr)1;
                }
            }
        }
        catch (Exception) { /* never break the hook chain */ }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void InstallHook()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = SetWindowsHookEx(13 /*WH_KEYBOARD_LL*/, _hookProc, GetModuleHandle(null), 0);
        Trace("hook " + (_hook != IntPtr.Zero));
    }

    private void RemoveHook()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
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

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmAppArm)
        {
            _wantArmed = wParam != IntPtr.Zero;
            Apply();
            return IntPtr.Zero;
        }
        if (msg == WmAppSuspend)
        {
            _suspended = wParam != IntPtr.Zero;
            Apply();
            return IntPtr.Zero;
        }
        if (msg == WmHotkey && _armed)
        {
            switch ((int)wParam)
            {
                case IdUp:
                case IdChordUp: VolumeUp?.Invoke(); break;
                case IdDown:
                case IdChordDown: VolumeDown?.Invoke(); break;
                case IdMute:
                case IdChordMute: Mute?.Invoke(); break;
            }
            Trace("key " + (int)wParam);
            return IntPtr.Zero;
        }
        if (msg == 0x0010 || msg == 0x0002)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void Apply()
    {
        if (_wantArmed && !_suspended)
        {
            Register();
            InstallHook();
        }
        else
        {
            RemoveHook();
            Unregister();
        }
    }

    private void Register()
    {
        Unregister();
        _chordUp = HotkeySettings.VolumeUp;
        _chordDown = HotkeySettings.VolumeDown;
        _chordMute = HotkeySettings.Mute;
        RegisterHotKey(_hwnd, IdUp, ModNoRepeat, VkVolumeUp);
        RegisterHotKey(_hwnd, IdDown, ModNoRepeat, VkVolumeDown);
        RegisterHotKey(_hwnd, IdMute, ModNoRepeat, VkVolumeMute);
        RegisterChord(IdChordUp, _chordUp);
        RegisterChord(IdChordDown, _chordDown);
        RegisterChord(IdChordMute, _chordMute);
        _armed = true;
        Trace($"hotkeys {_chordUp.Text} / {_chordDown.Text} / {_chordMute.Text}");
    }

    private void RegisterChord(int id, HotkeyChord chord)
    {
        bool bareMedia = chord.Modifiers == 0 && chord.VirtualKey is VkVolumeUp or VkVolumeDown or VkVolumeMute;
        if (bareMedia) return;
        RegisterHotKey(_hwnd, id, chord.Modifiers | ModNoRepeat, chord.VirtualKey);
    }

    private void Unregister()
    {
        UnregisterHotKey(_hwnd, IdUp);
        UnregisterHotKey(_hwnd, IdDown);
        UnregisterHotKey(_hwnd, IdMute);
        UnregisterHotKey(_hwnd, IdChordUp);
        UnregisterHotKey(_hwnd, IdChordDown);
        UnregisterHotKey(_hwnd, IdChordMute);
        _armed = false;
    }

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static bool Matches(HotkeyChord chord, int vk, bool ctrl, bool alt, bool shift, bool win)
    {
        if (chord.VirtualKey != vk) return false;
        bool wantCtrl = (chord.Modifiers & HotkeyChord.ModControl) != 0;
        bool wantAlt = (chord.Modifiers & HotkeyChord.ModAlt) != 0;
        bool wantShift = (chord.Modifiers & HotkeyChord.ModShift) != 0;
        bool wantWin = (chord.Modifiers & HotkeyChord.ModWin) != 0;
        return wantCtrl == ctrl && wantAlt == alt && wantShift == shift && wantWin == win;
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WndClassEx wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName,
        int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, int modifiers, int vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, int wParam, int lParam);
    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
