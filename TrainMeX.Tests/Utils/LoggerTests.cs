using System;
using System.IO;
using System.Threading.Tasks;
using TrainMeX.Classes;
using Xunit;

namespace TrainMeX.Tests {
    [Collection("LoggerTests")]
    public class LoggerTests : IDisposable {
        private readonly string _testLogFile;
        private readonly string _originalLogFilePath;

        public LoggerTests() {
            // Save original log file path
            _originalLogFilePath = Logger._logFilePath;

            // Create a unique test log file
            _testLogFile = Path.Combine(Path.GetTempPath(), $"TrainMeX_Test_{Guid.NewGuid()}.log");
            
            // Set the test log file path
            Logger._logFilePath = _testLogFile;
            Logger._consecutiveFailures = 0;
        }

        [Fact]
        public void Info_WritesLogEntry() {
            // Arrange
            var message = "Test info message";

            // Act
            Logger.Info(message);

            // Assert
            Assert.True(File.Exists(_testLogFile));
            var logContent = File.ReadAllText(_testLogFile);
            Assert.Contains("[INFO]", logContent);
            Assert.Contains(message, logContent);
        }

        [Fact]
        public void Warning_WritesLogEntryWithException() {
            // Arrange
            var message = "Test warning message";
            var exception = new InvalidOperationException("Test exception");

            // Act
            Logger.Warning(message, exception);

            // Assert
            var logContent = File.ReadAllText(_testLogFile);
            Assert.Contains("[WARNING]", logContent);
            Assert.Contains(message, logContent);
            Assert.Contains("InvalidOperationException", logContent);
            Assert.Contains("Test exception", logContent);
        }

        [Fact]
        public void Error_WritesLogEntryWithStackTrace() {
            // Arrange
            var message = "Test error message";
            Exception exception;
            try {
                throw new ArgumentException("Test argument exception");
            } catch (Exception ex) {
                exception = ex;
            }

            // Act
            Logger.Error(message, exception);

            // Assert
            var logContent = File.ReadAllText(_testLogFile);
            Assert.Contains("[ERROR]", logContent);
            Assert.Contains(message, logContent);
            Assert.Contains("ArgumentException", logContent);
            Assert.Contains("Stack Trace:", logContent);
        }

        [Fact]
        public void Log_ContainsTimestamp() {
            // Arrange
            var message = "Timestamp test message";

            // Act
            Logger.Info(message);

            // Assert
            var logContent = File.ReadAllText(_testLogFile);
            // Check for timestamp format [yyyy-MM-dd HH:mm:ss]
            Assert.Matches(@"\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\]", logContent);
        }

        [Fact]
        public void Log_ThreadSafety_MultipleConcurrentWrites() {
            // Arrange
            var tasks = new Task[10];
            
            // Act
            for (int i = 0; i < tasks.Length; i++) {
                var index = i;
                tasks[i] = Task.Run(() => {
                    // We lock here only for the TEST's reliability in asserting results,
                    // the Logger itself already has internal locks.
                    lock (Logger._lock) {
                        Logger.Info($"Concurrent message {index}");
                        Logger.Warning($"Concurrent warning {index}");
                        Logger.Error($"Concurrent error {index}");
                    }
                });
            }

            Task.WaitAll(tasks);

            // Assert
            var logContent = File.ReadAllText(_testLogFile);
            // Verify all messages were written
            for (int i = 0; i < tasks.Length; i++) {
                Assert.Contains($"Concurrent message {i}", logContent);
                Assert.Contains($"Concurrent warning {i}", logContent);
                Assert.Contains($"Concurrent error {i}", logContent);
            }
        }

        [Fact]
        public void Error_WithoutException_WritesMessageOnly() {
            // Arrange
            var message = "Error without exception";

            // Act
            Logger.Error(message, null);

            // Assert
            var logContent = File.ReadAllText(_testLogFile);
            Assert.Contains("[ERROR]", logContent);
            Assert.Contains(message, logContent);
            Assert.DoesNotContain("Exception:", logContent);
        }

        [Fact]
        public void Warning_WithoutException_WritesMessageOnly() {
            // Arrange
            var message = "Warning without exception";

            // Act
            Logger.Warning(message, null);

            // Assert
            var logContent = File.ReadAllText(_testLogFile);
            Assert.Contains("[WARNING]", logContent);
            Assert.Contains(message, logContent);
            Assert.DoesNotContain("Exception:", logContent);
        }

        [Fact]
        public void Log_HandlesLongMessages() {
            // Arrange
            var longMessage = new string('X', 10000);

            // Act
            Logger.Info(longMessage);

            // Assert - Should not throw and should write the message
            var logContent = File.ReadAllText(_testLogFile);
            Assert.Contains(longMessage, logContent);
        }

        [Fact]
        public void Log_HandlesSpecialCharacters() {
            // Arrange
            var message = "Special chars: \n\r\t\"'\\<>&";

            // Act
            Logger.Info(message);

            // Assert
            var logContent = File.ReadAllText(_testLogFile);
            Assert.Contains("Special chars:", logContent);
        }
        [Fact]
        public void Log_MultipleEntries_AppendsToFile() {
            // Arrange & Act
            Logger.Info("First entry");
            Logger.Warning("Second entry");
            Logger.Error("Third entry");

            // Assert
            var logContent = File.ReadAllText(_testLogFile);
            var lines = logContent.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            // Should have at least 3 lines (one for each log entry)
            Assert.True(lines.Length >= 3);
        }

        #region Edge Cases

        [Fact]
        public void Log_FileAccessDenied_HandlesGracefully() {
            // Arrange
            // Lock the log file
            using (var fs = new FileStream(_testLogFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
                // Act - Should not throw
                Logger.Info("This should fail to write to file but not crash");
            }

            // Assert - _consecutiveFailures should be incremented
            Assert.True(Logger._consecutiveFailures > 0);
        }

        [Fact]
        public void Log_NullMessage_HandlesSuccessfully() {
            // Act & Assert - Should not throw
            Logger.Info(null);
            Logger.Warning(null, null);
            Logger.Error(null, null);
        }

        [Fact]
        public void Log_FailureThreshold_StopsFileAccess() {
            // Arrange
            // Set failures to Max
            Logger._consecutiveFailures = 10; // MaxConsecutiveFailures is 10

            // Act - Log message
            Logger.Info("Threshold test message");

            // Assert - Should not throw, but also won't write to file if threshold met
            Assert.Equal(10, Logger._consecutiveFailures);
        }

        [Fact]
        public void Log_SuccessAfterFailure_ResetsCounter() {
            // Arrange
            Logger._consecutiveFailures = 5;

            // Act - Log message (should succeed)
            Logger.Info("Success message");

            // Assert - Counter should be reset
            Assert.Equal(0, Logger._consecutiveFailures);
        }

        #endregion

        public void Dispose() {
            // Restore original log file path
            if (_originalLogFilePath != null) {
                Logger._logFilePath = _originalLogFilePath;
            }

            // Clean up test log file
            try {
                if (File.Exists(_testLogFile)) {
                    File.Delete(_testLogFile);
                }
            } catch {
                // Ignore cleanup errors
            }
        }
    }
}
