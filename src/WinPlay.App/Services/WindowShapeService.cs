// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace WinPlay.App.Services;

/// <summary>Clips the complete native surface, including its backdrop, to one DPI-aware outline.</summary>
internal static class WindowShapeService
{
    private static readonly ConditionalWeakTable<Window, Registration> Registrations = new();

    internal static void Rounded(Window window) => Register(window, capsule: false);
    internal static void Capsule(Window window) => Register(window, capsule: true);

    private static void Register(Window window, bool capsule)
    {
        if (!Registrations.TryGetValue(window, out var registration))
        {
            registration = new Registration(window, capsule);
            Registrations.Add(window, registration);
            registration.Attach();
        }
        registration.Update();
    }

    internal static void Refresh(Window window)
    {
        if (Registrations.TryGetValue(window, out var registration)) registration.Update();
    }

    private sealed class Registration(Window window, bool capsule)
    {
        private readonly IntPtr _hwnd = WindowNative.GetWindowHandle(window);
        private readonly AppWindow _appWindow = window.AppWindow;
        private WindowShapeGeometry? _last;
        private bool _updating;

        internal void Attach()
        {
            _appWindow.Changed += OnChanged;
            if (window.Content is FrameworkElement root) root.SizeChanged += OnSizeChanged;
            window.Closed += OnClosed;
        }

        private void OnChanged(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (args.DidSizeChange || args.DidPositionChange) Update();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs args) => Update();

        internal void Update()
        {
            if (_updating) return;
            _updating = true;
            try { UpdateRegion(); }
            finally { _updating = false; }
        }

        private void UpdateRegion()
        {
            if (!GetWindowRect(_hwnd, out var outer) || !GetClientRect(_hwnd, out var client)) return;
            int width = client.Right - client.Left, height = client.Bottom - client.Top;
            if (width <= 0 || height <= 0) return;
            var origin = new Point();
            if (!ClientToScreen(_hwnd, ref origin)) return;
            var geometry = WindowShapeGeometry.Create(origin.X - outer.Left, origin.Y - outer.Top,
                width, height, GetDpiForWindow(_hwnd), capsule);
            if (_last == geometry) return;
            IntPtr region = CreateRoundRectRgn(geometry.Left, geometry.Top, geometry.Right, geometry.Bottom,
                geometry.Radius * 2, geometry.Radius * 2);
            if (region == IntPtr.Zero) return;
            if (SetWindowRgn(_hwnd, region, true) == 0)
            {
                DeleteObject(region); // On success Windows owns and later releases the region.
                return;
            }
            _last = geometry;
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            _appWindow.Changed -= OnChanged;
            if (window.Content is FrameworkElement root) root.SizeChanged -= OnSizeChanged;
            window.Closed -= OnClosed;
            Registrations.Remove(window);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);
}
