using System;
using Moq;
using GOON.Classes;
using GOON.ViewModels;

namespace GOON.Tests {
    public abstract class TestBase : IDisposable {
        protected Mock<UserSettings> MockSettings { get; } = new Mock<UserSettings>();
        protected Mock<IVideoUrlExtractor> MockExtractor { get; } = new Mock<IVideoUrlExtractor>();
        
        protected TestBase() {
            ServiceContainer.Clear();
            
            // Register mock settings
            ServiceContainer.Register(MockSettings.Object);
            ServiceContainer.Register<IVideoUrlExtractor>(MockExtractor.Object);
        }

        public virtual void Dispose() {
            ServiceContainer.Clear();
        }
    }
}
