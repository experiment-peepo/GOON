using System;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace TrainMeX.Classes {
    public class GlobalHotkeyService : IDisposable {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private IntPtr _windowHandle;
        private HwndSource _source;
        private const int HOTKEY_ID_PANIC = 9001;

        // Modifiers: Alt=1, Ctrl=2, Shift=4, Win=8
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_ALT = 0x0001;

        public event EventHandler OnPanic;

        public void Initialize(IntPtr windowHandle, uint modifiers, string keyName) {
            _windowHandle = windowHandle;
            _source = HwndSource.FromHwnd(_windowHandle);
            _source.AddHook(HwndHook);

            // Parse key name to Key enum
            Key key;
            uint virtualKey;
            if (Enum.TryParse<Key>(keyName, out key)) {
                virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
            } else {
                // Fallback to End key if parsing fails
                virtualKey = (uint)KeyInterop.VirtualKeyFromKey(Key.End);
            }
            
            // Check return value of RegisterHotKey - log warning if registration fails
            bool registered = RegisterHotKey(_windowHandle, HOTKEY_ID_PANIC, modifiers, virtualKey);
            if (!registered) {
                Logger.Warning($"Failed to register panic hotkey (modifiers: {modifiers}, key: {keyName}). The hotkey may already be in use by another application.");
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY) {
                int id = wParam.ToInt32();
                if (id == HOTKEY_ID_PANIC) {
                    OnPanic?.Invoke(this, EventArgs.Empty);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose() {
            _source?.RemoveHook(HwndHook);
            
            // Only unregister hotkeys if window handle is valid
            if (_windowHandle != IntPtr.Zero) {
                bool unregistered = UnregisterHotKey(_windowHandle, HOTKEY_ID_PANIC);
                if (!unregistered) {
                    Logger.Warning("Failed to unregister panic hotkey during disposal.");
                }
            }
        }
        
        public void Reinitialize(uint modifiers, string keyName) {
            // Unregister old hotkey
            if (_windowHandle != IntPtr.Zero) {
                bool unregistered = UnregisterHotKey(_windowHandle, HOTKEY_ID_PANIC);
                if (!unregistered) {
                    Logger.Warning("Failed to unregister old panic hotkey during reinitialize.");
                }
            }
            
            // Register new hotkey
            Key key;
            uint virtualKey;
            if (Enum.TryParse<Key>(keyName, out key)) {
                virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
            } else {
                // Fallback to End key if parsing fails
                virtualKey = (uint)KeyInterop.VirtualKeyFromKey(Key.End);
            }
            
            // Check return value of RegisterHotKey - log warning if registration fails
            bool registered = RegisterHotKey(_windowHandle, HOTKEY_ID_PANIC, modifiers, virtualKey);
            if (!registered) {
                Logger.Warning($"Failed to register panic hotkey during reinitialize (modifiers: {modifiers}, key: {keyName}). The hotkey may already be in use by another application.");
            }
        }
    }
}
