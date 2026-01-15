using System;
using System.Diagnostics;
using FlyleafLib.MediaPlayer;

namespace GOON.Classes {
    /// <summary>
    /// A synchronized clock implementation for multi-monitor playback.
    /// Uses Stopwatch for high-resolution timing, converted to 100ns Ticks.
    /// </summary>
    public class SharedClock : IClock {
        private long _baseSW;
        private long _baseTicks;
        private long _pausedTicks;
        private bool _isRunning;
        private double _speed = 1.0;
        private static readonly double _swToTicks = 10000000.0 / Stopwatch.Frequency;

        public SharedClock() {
            _baseSW = 0;
            _baseTicks = 0;
            _pausedTicks = 0;
            _isRunning = false;
        }

        public long Ticks {
            get {
                if (!_isRunning) return _pausedTicks;
                long elapsedSW = Stopwatch.GetTimestamp() - _baseSW;
                return _baseTicks + (long)(elapsedSW * _swToTicks * _speed);
            }
        }

        public double Speed {
            get => _speed;
            set {
                if (Math.Abs(_speed - value) < 0.001) return;
                // Capture current ticks before changing speed to maintain continuity
                _baseTicks = Ticks;
                _baseSW = Stopwatch.GetTimestamp();
                _speed = value;
            }
        }

        public void Start() {
            if (_isRunning) return;
            _baseSW = Stopwatch.GetTimestamp();
            _baseTicks = _pausedTicks;
            _isRunning = true;
        }

        public void Pause() {
            if (!_isRunning) return;
            _pausedTicks = Ticks;
            _isRunning = false;
        }

        public void Seek(long ticks) {
            _baseTicks = ticks;
            _baseSW = Stopwatch.GetTimestamp();
            if (!_isRunning) _pausedTicks = ticks;
        }
        
        public bool IsRunning => _isRunning;

        public void Reset() {
            _baseSW = Stopwatch.GetTimestamp();
            _baseTicks = 0;
            _pausedTicks = 0;
            _isRunning = false;
        }
    }
}
