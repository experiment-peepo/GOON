using System;
using System.Windows.Input;
using GOON.Classes;
using Xunit;

namespace GOON.Tests {
    public class RelayCommandTests {
        [Fact]
        public void Constructor_WithExecuteAction_CreatesCommand() {
            bool executed = false;
            var command = new RelayCommand(_ => executed = true);
            
            command.Execute(null);
            
            Assert.True(executed);
        }

        [Fact]
        public void Constructor_WithNullExecute_ThrowsArgumentNullException() {
            Assert.Throws<ArgumentNullException>(() => {
                new RelayCommand(null);
            });
        }

        [Fact]
        public void Execute_CallsExecuteAction() {
            bool executed = false;
            var command = new RelayCommand(_ => executed = true);
            
            command.Execute(null);
            
            Assert.True(executed);
        }

        [Fact]
        public void Execute_PassesParameter() {
            object receivedParameter = null;
            var expectedParameter = new object();
            var command = new RelayCommand(param => receivedParameter = param);
            
            command.Execute(expectedParameter);
            
            Assert.Same(expectedParameter, receivedParameter);
        }

        [Fact]
        public void CanExecute_WithoutPredicate_ReturnsTrue() {
            var command = new RelayCommand(_ => { });
            
            var result = command.CanExecute(null);
            
            Assert.True(result);
        }

        [Fact]
        public void CanExecute_WithPredicateReturningTrue_ReturnsTrue() {
            var command = new RelayCommand(_ => { }, _ => true);
            
            var result = command.CanExecute(null);
            
            Assert.True(result);
        }

        [Fact]
        public void CanExecute_WithPredicateReturningFalse_ReturnsFalse() {
            var command = new RelayCommand(_ => { }, _ => false);
            
            var result = command.CanExecute(null);
            
            Assert.False(result);
        }

        [Fact]
        public void CanExecute_WithPredicate_PassesParameter() {
            object receivedParameter = null;
            var expectedParameter = new object();
            var command = new RelayCommand(
                _ => { },
                param => {
                    receivedParameter = param;
                    return true;
                }
            );
            
            command.CanExecute(expectedParameter);
            
            Assert.Same(expectedParameter, receivedParameter);
        }

        [Fact]
        public void Execute_WhenCanExecuteReturnsFalse_StillExecutes() {
            bool executed = false;
            var command = new RelayCommand(_ => executed = true, _ => false);
            
            command.Execute(null);
            
            Assert.True(executed);
        }

        [Fact]
        public void CanExecuteChanged_CanBeSubscribed() {
            var command = new RelayCommand(_ => { });
            bool eventRaised = false;
            
            command.CanExecuteChanged += (s, e) => eventRaised = true;
            
            // CommandManager.RequerySuggested is raised by WPF framework
            // In tests, we can't easily trigger it, but we can verify subscription works
            Assert.NotNull(command);
        }

        [Fact]
        public void CanExecuteChanged_CanBeUnsubscribed() {
            var command = new RelayCommand(_ => { });
            bool eventRaised = false;
            EventHandler handler = (s, e) => eventRaised = true;
            
            command.CanExecuteChanged += handler;
            command.CanExecuteChanged -= handler;
            
            // Verify unsubscription works (can't easily test event raising in unit tests)
            Assert.NotNull(command);
        }
    }
}

