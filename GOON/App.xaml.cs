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

        public static TelemetryService Telemetry => ServiceContainer.TryGet<TelemetryService>(out var telemetry) ? telemetry : null;
        public static IVideoUrlExtractor UrlExtractor => ServiceContainer.TryGet<IVideoUrlExtractor>(out var extractor) ? extractor : null;

        protected override void OnStartup(StartupEventArgs e) {
            // 1. Add global exception handlers FIRST so we can catch initialization crashes
            this.DispatcherUnhandledException += (s, args) => {
                try {
                    Classes.Logger.Error("Unhandled exception in UI thread", args.Exception);
                    Console.Error.WriteLine($"Unhandled Exception: {args.Exception}");

                    var userMessage = "An unexpected error occurred in the application.\n\n" +
                                     args.Exception.Message + "\n\n" +
                                     "The error details have been logged.";
                    
                    MessageBox.Show(userMessage, "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    args.Handled = true;
                } catch {
                    args.Handled = true;
                }
            };
            
            AppDomain.CurrentDomain.UnhandledException += (s, args) => {
                try {
                    var ex = args.ExceptionObject as Exception;
                    Logger.Error("Fatal unhandled exception", ex);
                    MessageBox.Show("A critical error occurred: " + ex?.Message, "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
                } catch { }
            };

            // 1.5. Rotate logs if too large (20MB)
            Classes.Logger.CheckAndRotateLogFile(20 * 1024 * 1024);

            // 2. Initialize Flyleaf Engine with robust paths and detailed logging
            try {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string ffmpegPath = System.IO.Path.Combine(baseDir, "FFmpeg");
                string pluginsPath = System.IO.Path.Combine(baseDir, "Plugins");

                // Redirect Flyleaf logs to our application logger
                FlyleafLib.Logger.CustomOutput = (msg) => {
                    Classes.Logger.Info(msg);
                };

                FlyleafLib.Engine.Start(new FlyleafLib.EngineConfig() {
                    FFmpegPath = ffmpegPath,
                    PluginsPath = pluginsPath,
                    UIRefresh = true,
                    LogLevel = FlyleafLib.LogLevel.Info,
                    LogOutput = ":custom",
                    FFmpegLogLevel = Flyleaf.FFmpeg.LogLevel.Warn
                });
            } catch (Exception ex) {
                Logger.Error("Failed to initialize Flyleaf Engine", ex);
                MessageBox.Show("Failed to initialize Video Engine:\n" + ex.Message, "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                // We should probably exit if the engine fails to start
                this.Shutdown();
                return;
            }
            
            // Register Services
            ServiceContainer.Register(UserSettings.Load());

            // Ensure Playlists directory exists
            try {
                if (!System.IO.Directory.Exists(AppPaths.PlaylistsDirectory)) {
                    System.IO.Directory.CreateDirectory(AppPaths.PlaylistsDirectory);
                }
            } catch (Exception ex) {
                Logger.Warning($"Failed to create Playlists directory: {ex.Message}");
            }

            ServiceContainer.Register(new VideoPlayerService());
            ServiceContainer.Register(new HotkeyService());
            var ytDlpService = new YtDlpService();
            ServiceContainer.Register(ytDlpService);

            ServiceContainer.Register<IVideoUrlExtractor>(new VideoUrlExtractor(null, ytDlpService));

            ServiceContainer.Register(new TelemetryService());

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

            // SESSION RESUME: Final save of positions
            PlaybackPositionTracker.Instance.SaveSync();

            // Ensure hotkeys are unregistered
            if (ServiceContainer.TryGet<HotkeyService>(out var hotkeyService)) {
                hotkeyService.Dispose();
            }

            base.OnExit(e);
        }
    }
}
