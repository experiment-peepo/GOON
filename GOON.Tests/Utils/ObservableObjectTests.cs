using System.ComponentModel;
using GOON.Classes;
using Xunit;

namespace GOON.Tests {
    public class ObservableObjectTests {
        private class TestObservable : ObservableObject {
            private string _testProperty;
            
            public string TestProperty {
                get => _testProperty;
                set => SetProperty(ref _testProperty, value);
            }
            
            private int _intProperty;
            
            public int IntProperty {
                get => _intProperty;
                set => SetProperty(ref _intProperty, value);
            }
        }

        [Fact]
        public void SetProperty_WhenValueChanges_RaisesPropertyChanged() {
            var obj = new TestObservable();
            string changedProperty = null;
            
            obj.PropertyChanged += (s, e) => changedProperty = e.PropertyName;
            
            obj.TestProperty = "NewValue";
            
            Assert.Equal(nameof(TestObservable.TestProperty), changedProperty);
        }

        [Fact]
        public void SetProperty_WhenValueDoesNotChange_DoesNotRaisePropertyChanged() {
            var obj = new TestObservable();
            obj.TestProperty = "Value";
            bool eventRaised = false;
            
            obj.PropertyChanged += (s, e) => eventRaised = true;
            
            obj.TestProperty = "Value";
            
            Assert.False(eventRaised);
        }

        [Fact]
        public void SetProperty_WithDifferentValues_UpdatesProperty() {
            var obj = new TestObservable();
            obj.TestProperty = "Value1";
            Assert.Equal("Value1", obj.TestProperty);
            
            obj.TestProperty = "Value2";
            Assert.Equal("Value2", obj.TestProperty);
        }

        [Fact]
        public void SetProperty_WithIntValue_Works() {
            var obj = new TestObservable();
            string changedProperty = null;
            
            obj.PropertyChanged += (s, e) => changedProperty = e.PropertyName;
            
            obj.IntProperty = 42;
            
            Assert.Equal(nameof(TestObservable.IntProperty), changedProperty);
            Assert.Equal(42, obj.IntProperty);
        }

        [Fact]
        public void SetProperty_WithNullValue_Works() {
            var obj = new TestObservable();
            obj.TestProperty = "Value";
            string changedProperty = null;
            
            obj.PropertyChanged += (s, e) => changedProperty = e.PropertyName;
            
            obj.TestProperty = null;
            
            Assert.Equal(nameof(TestObservable.TestProperty), changedProperty);
            Assert.Null(obj.TestProperty);
        }

        [Fact]
        public void PropertyChanged_Event_CanBeSubscribed() {
            var obj = new TestObservable();
            string changedProperty = null;
            
            obj.PropertyChanged += (s, e) => changedProperty = e.PropertyName;
            
            // OnPropertyChanged is protected, so we test via SetProperty
            obj.TestProperty = "NewValue";
            
            Assert.Equal(nameof(TestObservable.TestProperty), changedProperty);
        }

        [Fact]
        public void SetProperty_MultipleProperties_RaisesCorrectEvents() {
            var obj = new TestObservable();
            var changedProperties = new System.Collections.Generic.List<string>();
            
            obj.PropertyChanged += (s, e) => changedProperties.Add(e.PropertyName);
            
            obj.TestProperty = "Value1";
            obj.IntProperty = 10;
            obj.TestProperty = "Value2";
            
            Assert.Equal(3, changedProperties.Count);
            Assert.Contains(nameof(TestObservable.TestProperty), changedProperties);
            Assert.Contains(nameof(TestObservable.IntProperty), changedProperties);
        }
    }
}

