/*
	Copyright (C) 2021 Damsel

	This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

	This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.

	You should have received a copy of the GNU General Public License along with this program. If not, see <https://www.gnu.org/licenses/>. 
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GOON.Classes {
    [SupportedOSPlatform("windows")]
    public class ScreenViewer {
        public Screen Screen;
        public string DeviceName;
        public bool IsAllScreens { get; private set; }

        public ScreenViewer(Screen screen) {
            Screen = screen;
            DeviceName = screen?.DeviceName;
            IsAllScreens = false;
        }

        public static ScreenViewer CreateAllScreens() {
            return new ScreenViewer(null) {
                DeviceName = "ALL_SCREENS",
                IsAllScreens = true
            };
        }

        public override bool Equals(object obj) => obj is ScreenViewer other && other.DeviceName == this.DeviceName;
        public override int GetHashCode() => DeviceName?.GetHashCode() ?? 0;

        public override string ToString() {
            if (IsAllScreens) return "[All Monitors]";

            // Extract screen number from DeviceName (e.g., "\\\\.\\DISPLAY1" -> "Screen 1")
            int screenNumber = 1;
            if (!string.IsNullOrEmpty(DeviceName)) {
                var match = Regex.Match(DeviceName, @"DISPLAY(\d+)", RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int num)) {
                    screenNumber = num;
                }
            }
            
            var bounds = Screen?.Bounds;
            var res = bounds.HasValue ? ($"{bounds.Value.Width}x{bounds.Value.Height}") : "Unknown";
            
            // Check if this is the primary screen
            bool isPrimary = Screen?.Primary ?? false;
            string primaryText = isPrimary ? " (Primary)" : "";
            
            return $"Screen {screenNumber} : {res}{primaryText}";
        }
    }
}
