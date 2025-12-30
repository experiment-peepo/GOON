using System;
using System.Windows;
using TrainMe.Classes;
using TrainMe.ViewModels;

namespace TrainMe.Windows {
    public partial class HypnoWindow : Window {
        private HypnoViewModel _viewModel;

        public HypnoWindow() {
            InitializeComponent();
            WindowServices.ToTransparentWindow(this);
            
            _viewModel = new HypnoViewModel();
            DataContext = _viewModel;

            _viewModel.RequestPlay += (s, e) => FirstVideo.Play();
            _viewModel.RequestPause += (s, e) => FirstVideo.Pause();
            _viewModel.RequestStop += (s, e) => {
                FirstVideo.Stop();
                FirstVideo.Close(); // Release file handle if possible
            };
        }

        public HypnoViewModel ViewModel => _viewModel;

        private void FirstVideo_MediaEnded(object sender, RoutedEventArgs e) {
            _viewModel.OnMediaEnded();
        }

        private void FirstVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e) {
            _viewModel.OnMediaFailed(e.ErrorException);
        }

        private void Window_SourceInitialized(object sender, EventArgs e) {
            // Optional: Maximize logic if needed, but usually we move to specific screen
        }
    }
}
