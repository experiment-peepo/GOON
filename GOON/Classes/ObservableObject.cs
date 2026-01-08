using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GOON.Classes {
    /// <summary>
    /// Base class for objects that need to notify property changes
    /// </summary>
    public class ObservableObject : INotifyPropertyChanged {
        /// <summary>
        /// Event raised when a property value changes
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Raises the PropertyChanged event
        /// </summary>
        /// <param name="propertyName">Name of the property that changed</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Sets a property value and raises PropertyChanged if the value changed
        /// </summary>
        /// <typeparam name="T">Property type</typeparam>
        /// <param name="field">Backing field</param>
        /// <param name="value">New value</param>
        /// <param name="propertyName">Property name (automatically inferred)</param>
        /// <returns>True if value changed, false otherwise</returns>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null) {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
