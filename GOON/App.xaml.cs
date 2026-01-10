/*
	Copyright (C) 2021 Damsel

	This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

	This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.

	You should have received a copy of the GNU General Public License along with this program. If not, see <https://www.gnu.org/licenses/>. 
*/

using GOON.Classes;
using System;
using System.Windows;

namespace GOON {
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public partial class App : System.Windows.Application {
        public static VideoPlayerService VideoService => ServiceContainer.TryGet<VideoPlayerService>(out var service) ? service : null;
        public static UserSettings Settings => ServiceContainer.TryGet<UserSettings>(out var settings) ? settings : null;
        public static HotkeyService Hotkeys => ServiceContainer.TryGet<HotkeyService>(out var hotkeys) ? hotkeys : null;
        public static UrlCacheService UrlCache => ServiceContainer.TryGet<UrlCacheService>(out var urlCache) ? urlCache : null;
        public static TelemetryService Telemetry => ServiceContainer.TryGet<TelemetryService>(out var telemetry) ? telemetry : null;

        protected override void OnStartup(StartupEventArgs e) {
            
            // Add global exception handlers BEFORE anything else
            this.DispatcherUnhandledException += (s, args) => {
                try {
                    // Log the full technical details
                    Classes.Logger.Error("Unhandled exception in UI thread", args.Exception);
                    Console.Error.WriteLine($"Unhandled Exception: {args.Exception}");

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
                        Logger.Error("Exception in DispatcherUnhandledException handler", handlerEx);
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
                    Logger.Error("Fatal unhandled exception", ex);
                    Console.Error.WriteLine($"Fatal Exception: {ex}");

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
                        Logger.Error("Exception in UnhandledException handler", handlerEx);
                    } catch {
                        // If logging fails, silently ignore to prevent further issues
                    }
                }
            };
            
            // Register Services
            ServiceContainer.Register(UserSettings.Load());
            ServiceContainer.Register(new VideoPlayerService());
            ServiceContainer.Register(new HotkeyService());
            ServiceContainer.Register(new UrlCacheService());
            ServiceContainer.Register(new TelemetryService());
            ServiceContainer.Register(new YtDlpService());

            // Cleanup old cached videos (10+ days old) in background
            System.Threading.Tasks.Task.Run(() => {
                try {
                    var downloadService = new VideoDownloadService();
                    downloadService.CleanupOldFiles(10);
                } catch (Exception ex) {
                    Logger.Warning($"Failed to cleanup video cache: {ex.Message}");
                }
            });

            // base.OnStartup starts the UI - call LAST
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e) {
            // Ensure all video players are stopped and disposed
            if (ServiceContainer.TryGet<VideoPlayerService>(out var videoService)) {
                videoService.StopAll();
            }

            // Ensure hotkeys are unregistered
            if (ServiceContainer.TryGet<HotkeyService>(out var hotkeyService)) {
                hotkeyService.Dispose();
            }

            base.OnExit(e);
        }
    }
}
