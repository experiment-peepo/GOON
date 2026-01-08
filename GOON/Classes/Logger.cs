using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace GOON.Classes {
    /// <summary>
    /// Simple file-based logger with different log levels
    /// </summary>
    public static class Logger {
        internal static readonly object _lock = new object();
        internal static string _logFilePath;
        internal static int _consecutiveFailures = 0;
        internal const int MaxConsecutiveFailures = 10; // Stop trying file logging after this many failures

        static Logger() {
            _logFilePath = AppPaths.LogFile;
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
                    
                    // Try to write to file if we haven't exceeded failure limit
                    if (_consecutiveFailures < MaxConsecutiveFailures) {
                        try {
                            File.AppendAllText(_logFilePath, logEntry);
                            _consecutiveFailures = 0; // Reset on success
                        } catch (Exception fileEx) {
                            _consecutiveFailures++;
                            // Fallback to Debug output when file logging fails
                            Debug.WriteLine($"[LOGGER FILE ERROR] Failed to write to log file ({_consecutiveFailures}/{MaxConsecutiveFailures}): {fileEx.Message}");
                            Debug.WriteLine($"[FALLBACK LOG] {logEntry.TrimEnd()}");
                        }
                    } else {
                        // File logging has failed too many times, use Debug output only
                        Debug.WriteLine($"[FALLBACK LOG] {logEntry.TrimEnd()}");
                    }
                }
            } catch (Exception ex) {
                // Last resort: try Debug.WriteLine without any formatting
                try {
                    Debug.WriteLine($"[LOGGER CRITICAL ERROR] {message} | Exception: {ex.Message}");
                } catch {
                    // Absolutely nothing we can do at this point
                }
            }
        }
    }
}


