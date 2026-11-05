// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;

namespace WinPlay.App.Services;

internal static class PopupWindowService
{
    /// <summary>Correct the SDK's caption-height estimate for a popup without a title bar.</summary>
    internal static void ResizeClient(AppWindow window, IntPtr hwnd, int width, int height)
    {
        window.ResizeClient(new Windows.Graphics.SizeInt32(width, height));
        if (!GetClientRect(hwnd, out var client)) return;
        int dx = width - (client.Right - client.Left);
        int dy = height - (client.Bottom - client.Top);
        if (dx != 0 || dy != 0)
            window.Resize(new Windows.Graphics.SizeInt32(window.Size.Width + dx, window.Size.Height + dy));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
}
