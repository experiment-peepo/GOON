using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

namespace GOON.Classes
{
    /// <summary>
    /// Broadcasts GOON window positions to HotScreen via shared memory
    /// for accurate censor box alignment
    /// </summary>
    public class HotScreenBridge : IDisposable
    {
        private MemoryMappedFile _mmf;
        private const string MMF_NAME = "GOON_WindowPositions";
        private const int MMF_SIZE = 1024; // Support up to 16 windows (64 bytes each)
        private bool _isInitialized;

        public HotScreenBridge()
        {
            try
            {
                _mmf = MemoryMappedFile.CreateOrOpen(MMF_NAME, MMF_SIZE);
                _isInitialized = true;
                Logger.Info("HotScreenBridge: Initialized shared memory for HotScreen integration");
            }
            catch (Exception ex)
            {
                Logger.Error($"HotScreenBridge: Failed to create shared memory: {ex.Message}", ex);
                _isInitialized = false;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        /// <summary>
        /// Update window position for a specific window
        /// </summary>
        public void UpdateWindowPosition(Window window)
        {
            if (!_isInitialized || _mmf == null) return;

            try
            {
                var helper = new WindowInteropHelper(window);
                var hwnd = helper.Handle;
                if (hwnd == IntPtr.Zero) return;

                var screen = Screen.FromHandle(hwnd);
                int screenId = Array.IndexOf(Screen.AllScreens, screen);

                int x, y, width, height;

                // Use client area if enabled (excludes window borders/titlebar)
                if (App.Settings.HotScreenUseClientArea)
                {
                    // Get client area rectangle
                    if (GetClientRect(hwnd, out RECT clientRect))
                    {
                        // Convert client area top-left to screen coordinates
                        POINT topLeft = new POINT { X = 0, Y = 0 };
                        ClientToScreen(hwnd, ref topLeft);

                        x = topLeft.X;
                        y = topLeft.Y;
                        width = clientRect.Right - clientRect.Left;
                        height = clientRect.Bottom - clientRect.Top;
                    }
                    else
                    {
                        // Fallback to window bounds
                        x = (int)window.Left;
                        y = (int)window.Top;
                        width = (int)window.ActualWidth;
                        height = (int)window.ActualHeight;
                    }
                }
                else
                {
                    // Use full window bounds
                    x = (int)window.Left;
                    y = (int)window.Top;
                    width = (int)window.ActualWidth;
                    height = (int)window.ActualHeight;
                }

                // Apply manual offset adjustments
                x += App.Settings.HotScreenOffsetX;
                y += App.Settings.HotScreenOffsetY;

                // Write to shared memory
                using (var accessor = _mmf.CreateViewAccessor())
                {
                    int offset = screenId * 64; // 64 bytes per screen

                    accessor.Write(offset + 0, screenId);
                    accessor.Write(offset + 4, x);
                    accessor.Write(offset + 8, y);
                    accessor.Write(offset + 12, width);
                    accessor.Write(offset + 16, height);
                    accessor.Write(offset + 20, 1); // Active flag

                    // Write timestamp for staleness detection
                    long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    accessor.Write(offset + 24, timestamp);

                    // Write offset values for debugging
                    accessor.Write(offset + 32, App.Settings.HotScreenOffsetX);
                    accessor.Write(offset + 36, App.Settings.HotScreenOffsetY);
                }

                Logger.Info($"HotScreenBridge: Updated screen {screenId} position: ({x}, {y}) {width}x{height} [offset: {App.Settings.HotScreenOffsetX}, {App.Settings.HotScreenOffsetY}]");
            }
            catch (Exception ex)
            {
                Logger.Warning($"HotScreenBridge: Failed to update position: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Clear window position for a screen (when window closes)
        /// </summary>
        public void ClearWindowPosition(int screenId)
        {
            if (!_isInitialized || _mmf == null) return;

            try
            {
                using (var accessor = _mmf.CreateViewAccessor())
                {
                    int offset = screenId * 64;
                    accessor.Write(offset + 20, 0); // Clear active flag
                }

                Logger.Info($"HotScreenBridge: Cleared screen {screenId} position");
            }
            catch (Exception ex)
            {
                Logger.Warning($"HotScreenBridge: Failed to clear position: {ex.Message}", ex);
            }
        }

        public void Dispose()
        {
            _mmf?.Dispose();
            _isInitialized = false;
        }
    }
}
