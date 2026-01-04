/*
	Copyright (C) 2021 Damsel

	This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

	This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.

	You should have received a copy of the GNU General Public License along with this program. If not, see <https://www.gnu.org/licenses/>. 
*/

using System;
using System.Windows;

namespace TrainMeX {
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public partial class App : System.Windows.Application {
        public static Classes.VideoPlayerService VideoService => Classes.ServiceContainer.TryGet<Classes.VideoPlayerService>(out var service) ? service : null;
        public static Classes.UserSettings Settings => Classes.ServiceContainer.TryGet<Classes.UserSettings>(out var settings) ? settings : null;
        public static Classes.HotkeyService Hotkeys => Classes.ServiceContainer.TryGet<Classes.HotkeyService>(out var hotkeys) ? hotkeys : null;

        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);
            
            // Enable GPU acceleration - Default enables hardware acceleration when available
            // WPF MediaElement uses Windows Media Foundation which automatically uses GPU for video decoding
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;
            
            // Add global exception handlers
            this.DispatcherUnhandledException += (s, args) => {
                try {
                    // Log the full technical details
                    Classes.Logger.Error("Unhandled exception in UI thread", args.Exception);
                    
                    // Show user-friendly message
                    var userMessage = "An unexpected error occurred in the application.\n\n" +
                                     "The error details have been logged. If this problem persists, " +
                                     "please check the application logs or contact support.";
                    
                    MessageBox.Show(userMessage, 
                        "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    args.Handled = true;
                } catch (Exception handlerEx) {
                    // If exception handler itself throws, log it but don't rethrow
                    // This prevents infinite exception loops
                    try {
                        Classes.Logger.Error("Exception in DispatcherUnhandledException handler", handlerEx);
                    } catch {
                        // If logging fails, silently ignore to prevent further issues
                    }
                    args.Handled = true;
                }
            };
            
            AppDomain.CurrentDomain.UnhandledException += (s, args) => {
                try {
                    var ex = args.ExceptionObject as Exception;
                    // Log the full technical details
                    Classes.Logger.Error("Fatal unhandled exception", ex);
                    
                    // Show user-friendly message
                    var userMessage = "A critical error occurred and the application needs to close.\n\n" +
                                     "The error details have been logged. Please check the application logs " +
                                     "for more information.";
                    
                    MessageBox.Show(userMessage, 
                        "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
                } catch (Exception handlerEx) {
                    // If exception handler itself throws, log it but don't rethrow
                    // This prevents issues during application shutdown
                    try {
                        Classes.Logger.Error("Exception in UnhandledException handler", handlerEx);
                    } catch {
                        // If logging fails, silently ignore to prevent further issues
                    }
                }
            };
            
            // Register Services
            Classes.ServiceContainer.Register(Classes.UserSettings.Load());
            Classes.ServiceContainer.Register(new Classes.VideoPlayerService());
            Classes.ServiceContainer.Register(new Classes.HotkeyService());
        }
    }
}
