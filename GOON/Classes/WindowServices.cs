/*
	Copyright (C) 2021 Damsel

	This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

	This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.

	You should have received a copy of the GNU General Public License along with this program. If not, see <https://www.gnu.org/licenses/>. 
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Runtime.Versioning;
using System.Windows.Interop;

namespace GOON.Classes {
    [SupportedOSPlatform("windows")]
    public static class WindowServices {
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int GWL_EXSTYLE = (-20);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [SupportedOSPlatform("windows")]
        public static Screen[] GetAllScreens()
        {
            return Screen.AllScreens;
        }

        [SupportedOSPlatform("windows")]
        public static int GetNumberOfScreens()
        {
            return Screen.AllScreens.Length;
        }

        [SupportedOSPlatform("windows")]
        public static List<ScreenViewer> GetAllScreenViewers() {
            List<ScreenViewer> list = new List<ScreenViewer>();
            foreach(Screen screen in GetAllScreens()) {
                list.Add(new ScreenViewer(screen));
            }
            return list;
        }

        [SupportedOSPlatform("windows")]
        public static void MoveWindowToScreen(Window window, Screen screen) {
            if (window == null || screen == null) return;

            window.WindowState = WindowState.Normal;
            window.WindowStartupLocation = WindowStartupLocation.Manual;

            var workingArea = screen.WorkingArea;
            window.Left = workingArea.Left;
            window.Top = workingArea.Top;
            window.Width = workingArea.Width;
            window.Height = workingArea.Height;

            window.WindowState = WindowState.Maximized;
        }
    }
}
