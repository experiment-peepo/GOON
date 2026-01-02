using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using TrainMeX.Classes;
using TrainMeX.ViewModels;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;

namespace TrainMeX.Windows {
    public partial class LauncherWindow : Window {
        private LauncherViewModel ViewModel => DataContext as LauncherViewModel;

        public LauncherWindow() {
            InitializeComponent();
            DataContext = new LauncherViewModel();
            ApplyAlwaysOnTopSetting();
        }

        private GlobalHotkeyService _hotkeys;

        protected override void OnSourceInitialized(EventArgs e) {
            base.OnSourceInitialized(e);
            InitializeHotkeys();
        }
        
        private void InitializeHotkeys() {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            if (_hotkeys != null) {
                _hotkeys.Dispose();
            }
            _hotkeys = new GlobalHotkeyService();
            
            var settings = App.Settings;
            _hotkeys.Initialize(helper.Handle, settings.PanicHotkeyModifiers, settings.PanicHotkeyKey ?? "End");
            
            _hotkeys.OnPanic += (s, args) => {
                App.VideoService.StopAll();
            };
        }
        
        public void ReloadHotkeys() {
            if (_hotkeys != null) {
                var settings = App.Settings;
                _hotkeys.Reinitialize(settings.PanicHotkeyModifiers, settings.PanicHotkeyKey ?? "End");
            }
        }

        public void ApplyAlwaysOnTopSetting() {
            var settings = App.Settings;
            this.Topmost = settings.LauncherAlwaysOnTop;
        }

        protected override void OnClosed(EventArgs e) {
            _hotkeys?.Dispose();
            (DataContext as IDisposable)?.Dispose();
            base.OnClosed(e);
        }

        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            // Mark event as handled to prevent event bubbling issues
            e.Handled = true;
            
            // Allow dragging even when maximized - restore first then drag
            if (WindowState == WindowState.Maximized) {
                // Calculate the position to restore to based on mouse position
                var point = PointToScreen(e.GetPosition(this));
                WindowState = WindowState.Normal;
                
                // Set window position relative to mouse
                Left = point.X - (RestoreBounds.Width * 0.5);
                Top = point.Y - 10; // Small offset from top
            }
            
            // Call DragMove immediately while the button is definitely pressed
            // Use the event args button state which is guaranteed to be pressed at this point
            if (e.ButtonState == MouseButtonState.Pressed) {
                try {
                    this.DragMove();
                } catch (InvalidOperationException) {
                    // Silently handle the case where DragMove fails
                    // This can happen in rare timing scenarios
                }
            }
        }
        
        // Make the entire window draggable, not just the header
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            // Only drag if clicking on the window background, not on controls
            if (e.OriginalSource is FrameworkElement element) {
                // Don't drag if clicking on buttons, textboxes, or other interactive controls
                if (element is Button || element is TextBox || element is ComboBox || 
                    element is Slider || element is ListView || element is ListViewItem ||
                    element is ScrollViewer || element is System.Windows.Controls.Primitives.ScrollBar) {
                    return;
                }
                
                // Check if we're clicking on a child of an interactive control
                var parent = VisualTreeHelper.GetParent(element);
                while (parent != null) {
                    if (parent is Button || parent is TextBox || parent is ComboBox || 
                        parent is Slider || parent is ListView || parent is ListViewItem ||
                        parent is ScrollViewer || parent is System.Windows.Controls.Primitives.ScrollBar) {
                        return;
                    }
                    parent = VisualTreeHelper.GetParent(parent);
                }
            }
            
            // Mark event as handled to prevent event bubbling issues
            e.Handled = true;
            
            // Allow dragging even when maximized - restore first then drag
            if (WindowState == WindowState.Maximized) {
                // Calculate the position to restore to based on mouse position
                var point = PointToScreen(e.GetPosition(this));
                WindowState = WindowState.Normal;
                
                // Set window position relative to mouse, ensuring it's visible
                var windowWidth = ActualWidth > 0 ? ActualWidth : Width;
                var windowHeight = ActualHeight > 0 ? ActualHeight : Height;
                
                // Center window on mouse cursor
                Left = Math.Max(0, point.X - (windowWidth * 0.5));
                Top = Math.Max(0, point.Y - 10); // Small offset from top
                
                // Ensure window doesn't go off screen
                var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)point.X, (int)point.Y));
                if (screen != null) {
                    var screenBounds = screen.WorkingArea;
                    if (Left + windowWidth > screenBounds.Right) {
                        Left = screenBounds.Right - windowWidth;
                    }
                    if (Top + windowHeight > screenBounds.Bottom) {
                        Top = screenBounds.Bottom - windowHeight;
                    }
                }
            }
            
            // Call DragMove immediately while the button is definitely pressed
            // Use the event args button state which is guaranteed to be pressed at this point
            if (e.ButtonState == MouseButtonState.Pressed) {
                try {
                    this.DragMove();
                } catch (InvalidOperationException) {
                    // Silently handle the case where DragMove fails
                    // This can happen in rare timing scenarios
                }
            }
        }



        private Point _startPoint;

        private void AddedFilesList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            _startPoint = e.GetPosition(null);
        }

        private void AddedFilesList_MouseMove(object sender, MouseEventArgs e) {
            Point mousePos = e.GetPosition(null);
            Vector diff = _startPoint - mousePos;

            if (e.LeftButton == MouseButtonState.Pressed &&
                (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                 Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)) {
                
                // Don't trigger drag-drop if we are clicking on a Slider or ComboBox
                var originalSource = e.OriginalSource as DependencyObject;
                if (FindAncestor<Slider>(originalSource) != null || FindAncestor<ComboBox>(originalSource) != null) {
                    return;
                }

                var listView = sender as ListView;
                var listViewItem = FindAncestor<ListViewItem>(originalSource);
                if (listViewItem == null) return;

                var data = (VideoItem)listView.ItemContainerGenerator.ItemFromContainer(listViewItem);
                
                DataObject dragData = new DataObject("VideoItem", data);
                DragDrop.DoDragDrop(listViewItem, dragData, DragDropEffects.Move);
            }
        }

        private void AddedFilesList_DragOver(object sender, DragEventArgs e) {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
                e.Effects = DragDropEffects.Copy;
            } else if (e.Data.GetDataPresent("VideoItem")) {
                e.Effects = DragDropEffects.Move;
            } else {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void AddedFilesList_Drop(object sender, DragEventArgs e) {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                ViewModel?.AddDroppedFiles(files);
            } else if (e.Data.GetDataPresent("VideoItem")) {
                var sourceItem = (VideoItem)e.Data.GetData("VideoItem");
                var targetItem = ((FrameworkElement)e.OriginalSource).DataContext as VideoItem;

                if (sourceItem != null && targetItem != null && sourceItem != targetItem) {
                    var newIndex = ViewModel.AddedFiles.IndexOf(targetItem);
                    ViewModel.MoveVideoItem(sourceItem, newIndex);
                }
            }
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject {
            do {
                if (current is T t) return t;
                current = VisualTreeHelper.GetParent(current);
            } while (current != null);
            return null;
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e) {
            var settingsWindow = new SettingsWindow {
                Owner = this
            };
            settingsWindow.ShowDialog();
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e) {
            if (WindowState == WindowState.Maximized) {
                WindowState = WindowState.Normal;
                MaximizeButton.Content = "□";
            } else {
                WindowState = WindowState.Maximized;
                MaximizeButton.Content = "❐";
            }
        }

        protected override void OnStateChanged(EventArgs e) {
            base.OnStateChanged(e);
            if (MaximizeButton != null) {
                MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
            }
            
            // Update border corner radius based on window state
            if (WindowState == WindowState.Maximized) {
                if (MainBorder != null) {
                    MainBorder.CornerRadius = new CornerRadius(0);
                }
                if (HeaderBorder != null) {
                    HeaderBorder.CornerRadius = new CornerRadius(0);
                }
            } else {
                if (MainBorder != null) {
                    MainBorder.CornerRadius = new CornerRadius(8);
                }
                if (HeaderBorder != null) {
                    HeaderBorder.CornerRadius = new CornerRadius(8, 8, 0, 0);
                }
            }
        }
    }

    public class StringToVisibilityConverter : IValueConverter {
        public static readonly StringToVisibilityConverter Instance = new StringToVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is string str && !string.IsNullOrWhiteSpace(str)) {
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    public class PluralConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is int count) {
                return count == 1 ? "video" : "videos";
            }
            return "videos";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    public class BooleanToVisibilityConverter : IValueConverter {
        public static readonly BooleanToVisibilityConverter Instance = new BooleanToVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is bool boolValue && boolValue) {
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is Visibility visibility) {
                return visibility == Visibility.Visible;
            }
            return false;
        }
    }

    public class StatusMessageTypeToBrushConverter : IValueConverter {
        public static readonly StatusMessageTypeToBrushConverter Instance = new StatusMessageTypeToBrushConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is ViewModels.StatusMessageType messageType) {
                return messageType switch {
                    ViewModels.StatusMessageType.Success => new SolidColorBrush(Color.FromArgb(0x33, 0x90, 0xEE, 0x90)), // Soft green with transparency
                    ViewModels.StatusMessageType.Warning => new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xD7, 0x00)), // Golden yellow with transparency
                    ViewModels.StatusMessageType.Error => new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x69, 0xB4)),   // HotPink with transparency (theme consistent)
                    _ => new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00)) // Default: dark with transparency
                };
            }
            return new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    public class StatusMessageTypeToForegroundConverter : IValueConverter {
public static readonly StatusMessageTypeToForegroundConverter Instance = new StatusMessageTypeToForegroundConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is ViewModels.StatusMessageType messageType) {
                return messageType switch {
                    ViewModels.StatusMessageType.Success => new SolidColorBrush(Color.FromArgb(0xCC, 0x90, 0xEE, 0x90)), // Light green
                    ViewModels.StatusMessageType.Warning => new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xD7, 0x00)), // Golden yellow
                    ViewModels.StatusMessageType.Error => new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0x69, 0xB4)),   // HotPink for errors (theme consistency)
                    _ => new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)) // Default: white
                };
            }
            return new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    public class ValidationStatusToBrushConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is ViewModels.FileValidationStatus status) {
                return status switch {
                    ViewModels.FileValidationStatus.Valid => new SolidColorBrush(Color.FromArgb(0x66, 0x90, 0xEE, 0x90)), // Soft green border
                    ViewModels.FileValidationStatus.Missing => new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0x69, 0xB4)), // HotPink border
                    ViewModels.FileValidationStatus.Invalid => new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xD7, 0x00)), // Golden yellow border
                    _ => new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x69, 0xB4)) // Default: HotPink border
                };
            }
            return new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x69, 0xB4));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    public class ValidationStatusToIconConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is ViewModels.FileValidationStatus status) {
                return status switch {
                    ViewModels.FileValidationStatus.Valid => "✓", // Checkmark
                    ViewModels.FileValidationStatus.Missing => "⚠", // Warning
                    ViewModels.FileValidationStatus.Invalid => "✗", // X mark
                    _ => "▶" // Play icon for unknown/not validated
                };
            }
            return "▶";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    public class ValidationStatusToOpacityConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is ViewModels.FileValidationStatus status) {
                return status == ViewModels.FileValidationStatus.Valid ? 1.0 : 0.7; // Gray out invalid files
            }
            return 1.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    public class ValidationStatusToForegroundConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is ViewModels.FileValidationStatus status) {
                return status switch {
                    ViewModels.FileValidationStatus.Valid => new SolidColorBrush(Color.FromRgb(0x90, 0xEE, 0x90)), // Soft green
                    ViewModels.FileValidationStatus.Missing => new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)), // HotPink
                    ViewModels.FileValidationStatus.Invalid => new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)), // Golden yellow
                    _ => new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)) // HotPink for unknown
                };
            }
            return new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    public class OpacityToIntConverter : IValueConverter {
        private const double MaxOpacity = 0.90;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is double opacity) {
                // Scale 0-0.90 opacity range to 0-100 display range
                return ((int)Math.Round((opacity / MaxOpacity) * 100)).ToString();
            }
            return "0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    public class OpacityScaleConverter : IValueConverter {
        private const double MaxOpacity = 0.90;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            // Convert: opacity (0-0.90) -> slider value (0-1.0)
            if (value is double opacity) {
                return opacity / MaxOpacity;
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            // ConvertBack: slider value (0-1.0) -> opacity (0-0.90)
            if (value is double sliderValue) {
                return sliderValue * MaxOpacity;
            }
            return 0.0;
        }
    }
}
