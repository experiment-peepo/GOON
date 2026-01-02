using System;
using System.IO;
using System.Threading;

namespace TrainMeX.Classes {
    /// <summary>
    /// Simple file-based logger with different log levels
    /// </summary>
    public static class Logger {
        private static readonly object _lock = new object();
        private static string _logFilePath;

        static Logger() {
            _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TrainMeX.log");
        }

        /// <summary>
        /// Log an error message
        /// </summary>
        public static void Error(string message, Exception exception = null) {
            Log("ERROR", message, exception);
        }

        /// <summary>
        /// Log a warning message
        /// </summary>
        public static void Warning(string message, Exception exception = null) {
            Log("WARNING", message, exception);
        }

        /// <summary>
        /// Log an info message
        /// </summary>
        public static void Info(string message) {
            Log("INFO", message, null);
        }

        private static void Log(string level, string message, Exception exception) {
            try {
                lock (_lock) {
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    var logEntry = $"[{timestamp}] [{level}] {message}";
                    
                    if (exception != null) {
                        logEntry += $"\nException: {exception.GetType().Name}: {exception.Message}";
                        if (exception.StackTrace != null) {
                            logEntry += $"\nStack Trace: {exception.StackTrace}";
                        }
                    }
                    
                    logEntry += Environment.NewLine;
                    
                    File.AppendAllText(_logFilePath, logEntry);
                }
            } catch {
                // Silently fail if logging fails to avoid infinite loops
            }
        }
    }
}


