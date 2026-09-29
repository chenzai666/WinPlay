// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Text;

namespace WinPlay.App.Services;

/// <summary>
/// Hides the Windows volume flyout while WinPlay owns the volume keys.
/// Only ShellExperienceHost's small NativeHWNDHost is touched — never closed.
/// </summary>
internal static class WindowsVolumeFlyout
{
    private static int _hiding;

    public static void HideBurst()
    {
        if (Interlocked.CompareExchange(ref _hiding, 1, 0) != 0) return;
        var thread = new Thread(() =>
        {
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    HideOnce();
                    Thread.Sleep(50);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _hiding, 0);
            }
        })
        { IsBackground = true, Name = "WinPlay hide volume flyout" };
        thread.Start();
    }

    private static void HideOnce()
    {
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            var cls = new StringBuilder(64);
            GetClassName(hwnd, cls, cls.Capacity);
            if (!cls.ToString().Equals("NativeHWNDHost", StringComparison.Ordinal)) return true;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (!IsShellExperienceHost(pid)) return true;
            if (!GetWindowRect(hwnd, out RECT rect)) return true;
            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;
            if (w is >= 40 and <= 900 && h is >= 40 and <= 500)
                ShowWindow(hwnd, 0); // SW_HIDE
            return true;
        }, IntPtr.Zero);
    }

    private static bool IsShellExperienceHost(uint pid)
    {
        IntPtr handle = OpenProcess(0x1000, false, pid);
        if (handle == IntPtr.Zero) return false;
        try
        {
            var buf = new StringBuilder(260);
            int size = buf.Capacity;
            if (!QueryFullProcessImageName(handle, 0, buf, ref size)) return false;
            return buf.ToString().Contains("ShellExperienceHost", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
