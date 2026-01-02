using System;
using System.Windows.Input;

namespace TrainMeX.Classes {
    /// <summary>
    /// Relay command implementation for MVVM pattern
    /// </summary>
    public class RelayCommand : ICommand {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        /// <summary>
        /// Creates a new relay command
        /// </summary>
        /// <param name="execute">Action to execute</param>
        /// <param name="canExecute">Optional predicate to determine if command can execute</param>
        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null) {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>
        /// Determines if the command can execute
        /// </summary>
        /// <param name="parameter">Command parameter</param>
        /// <returns>True if command can execute</returns>
        public bool CanExecute(object parameter) {
            return _canExecute == null || _canExecute(parameter);
        }

        /// <summary>
        /// Executes the command
        /// </summary>
        /// <param name="parameter">Command parameter</param>
        public void Execute(object parameter) {
            _execute(parameter);
        }

        public event EventHandler CanExecuteChanged {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}
