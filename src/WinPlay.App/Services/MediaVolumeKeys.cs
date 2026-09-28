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
    private const int IdUp = 1;
    private const int IdDown = 2;
    private const int IdMute = 3;
    private const int WmAppArm = 0x8000 + 20;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private IntPtr _hwnd;
    private bool _armed;
    private bool _disposed;

    public event Action? VolumeUp;
    public event Action? VolumeDown;
    public event Action? Mute;

    public MediaVolumeKeys()
    {
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
        Unregister();
        GC.KeepAlive(proc);
    }

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmAppArm)
        {
            bool want = wParam != IntPtr.Zero;
            if (want == _armed) return IntPtr.Zero;
            if (want) Register();
            else Unregister();
            return IntPtr.Zero;
        }
        if (msg == WmHotkey && _armed)
        {
            switch ((int)wParam)
            {
                case IdUp: VolumeUp?.Invoke(); break;
                case IdDown: VolumeDown?.Invoke(); break;
                case IdMute: Mute?.Invoke(); break;
            }
            return IntPtr.Zero;
        }
        if (msg == 0x0010 || msg == 0x0002)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void Register()
    {
        Unregister();
        bool up = RegisterHotKey(_hwnd, IdUp, 0, VkVolumeUp);
        bool down = RegisterHotKey(_hwnd, IdDown, 0, VkVolumeDown);
        bool mute = RegisterHotKey(_hwnd, IdMute, 0, VkVolumeMute);
        _armed = up || down || mute;
    }

    private void Unregister()
    {
        UnregisterHotKey(_hwnd, IdUp);
        UnregisterHotKey(_hwnd, IdDown);
        UnregisterHotKey(_hwnd, IdMute);
        _armed = false;
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

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
