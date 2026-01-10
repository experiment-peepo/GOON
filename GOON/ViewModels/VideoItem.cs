using System;
using System.IO;
using GOON.Classes;

namespace GOON.ViewModels {
    /// <summary>
    /// Validation status for a video file
    /// </summary>
    public enum FileValidationStatus {
        Unknown,
        Valid,
        Missing,
        Invalid
    }

    public class VideoItem : ObservableObject {
        public string FilePath { get; }
        
        /// <summary>
        /// Gets whether this item is a URL (not a local file path)
        /// </summary>
        public bool IsUrl => Uri.TryCreate(FilePath, UriKind.Absolute, out Uri uri) && 
                             (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        
        /// <summary>
        /// Optional title extracted from the video page. If null or empty, FileName will fall back to URL-based extraction.
        /// </summary>
        private string _title;
        public string Title {
            get => _title;
            set => SetProperty(ref _title, value);
        }
        
        /// <summary>
        /// Gets the display name for the video item
        /// </summary>
        public string FileName {
            get {
                // Prefer Title if available and not empty
                if (!string.IsNullOrWhiteSpace(Title)) {
                    return Title;
                }
                
                // Fall back to URL-based or file-based extraction
                if (IsUrl) {
                    try {
                        var uri = new Uri(FilePath);
                        var segments = uri.Segments;
                        if (segments.Length > 0) {
                            var lastSegment = segments[segments.Length - 1];
                            if (!string.IsNullOrWhiteSpace(lastSegment) && lastSegment != "/") {
                                return Uri.UnescapeDataString(lastSegment);
                            }
                        }
                        // Fallback to domain + path
                        return $"{uri.Host}{uri.AbsolutePath}";
                    } catch {
                        return FilePath;
                    }
                }
                return Path.GetFileName(FilePath);
            }
        }

        private ScreenViewer _assignedScreen;
        public ScreenViewer AssignedScreen {
            get => _assignedScreen;
            set => SetProperty(ref _assignedScreen, value);
        }

        private double _opacity = 0.9;
        public double Opacity {
            get => _opacity;
            set => SetProperty(ref _opacity, value);
        }

        private double _volume = 0.5;
        public double Volume {
            get => _volume;
            set => SetProperty(ref _volume, value);
        }

        private FileValidationStatus _validationStatus = FileValidationStatus.Unknown;
        public FileValidationStatus ValidationStatus {
            get => _validationStatus;
            set => SetProperty(ref _validationStatus, value);
        }

        private string _validationError;
        public string ValidationError {
            get => _validationError;
            set => SetProperty(ref _validationError, value);
        }

        /// <summary>
        /// The original page URL with full path (including slug). Used for re-extraction when video URLs expire.
        /// </summary>
        private string _originalPageUrl;
        public string OriginalPageUrl {
            get => _originalPageUrl;
            set => SetProperty(ref _originalPageUrl, value);
        }

        /// <summary>
        /// Gets whether the file is valid and exists
        /// </summary>
        public bool IsValid => ValidationStatus == FileValidationStatus.Valid;

        public VideoItem(string filePath, ScreenViewer defaultScreen = null) {
            FilePath = filePath;
            AssignedScreen = defaultScreen;
        }

        public override string ToString() {
            return FileName;
        }

        /// <summary>
        /// Validates the file or URL and updates the validation status
        /// </summary>
        public void Validate() {
            if (string.IsNullOrWhiteSpace(FilePath)) {
                ValidationStatus = FileValidationStatus.Invalid;
                ValidationError = "File path or URL is empty";
                return;
            }

            if (IsUrl) {
                // Validate URL
                if (!FileValidator.ValidateVideoUrl(FilePath, out string errorMessage)) {
                    ValidationStatus = FileValidationStatus.Invalid;
                    ValidationError = errorMessage;
                } else {
                    ValidationStatus = FileValidationStatus.Valid;
                    ValidationError = null;
                }
            } else {
                // Validate local file
                if (!FileValidator.ValidateVideoFile(FilePath, out string errorMessage)) {
                    ValidationStatus = File.Exists(FilePath) ? FileValidationStatus.Invalid : FileValidationStatus.Missing;
                    ValidationError = errorMessage;
                } else {
                    ValidationStatus = FileValidationStatus.Valid;
                    ValidationError = null;
                }
            }
        }
    }
}
