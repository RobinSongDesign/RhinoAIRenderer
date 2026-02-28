using AIRenderer.Models;
using AIRenderer.Services;
using Rhino;
using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace AIRenderer.ViewModels
{
    public class AIRenderViewModel : INotifyPropertyChanged
    {
        private readonly GeminiAPIService _apiService;
        private RenderSettings _settings;
        private BitmapSource _sourceImage;
        private BitmapSource _resultImage;
        private string _statusMessage = "Ready";
        private bool _isGenerating;
        private bool _hasSourceImage;
        private bool _hasResultImage;

        public AIRenderViewModel() : this("", "gemini-3-pro-image-preview")
        {
        }

        public AIRenderViewModel(string apiKey, string selectedModel)
        {
            _apiService = new GeminiAPIService();
            _settings = new RenderSettings();

            // Apply pre-loaded settings
            if (!string.IsNullOrEmpty(apiKey))
            {
                Settings.ApiKey = apiKey;
            }
            if (!string.IsNullOrEmpty(selectedModel))
            {
                Settings.SelectedModel = selectedModel;
            }

            // Initialize commands
            CaptureCommand = new RelayCommand(CaptureScreen, () => !IsGenerating);
            GenerateCommand = new RelayCommand(async () => await GenerateImageAsync(), CanGenerate);
            ClearCommand = new RelayCommand(ClearAll, () => !IsGenerating);
            SaveResultCommand = new RelayCommand(SaveResult, () => HasResultImage);
            UseResultAsSourceCommand = new RelayCommand(UseResultAsSource, () => HasResultImage);
        }

        private void SaveSettings()
        {
            SettingsService.SaveSettings(Settings.ApiKey, Settings.SelectedModel);
        }

        public RenderSettings Settings
        {
            get => _settings;
            set
            {
                _settings = value;
                OnPropertyChanged();
            }
        }

        public BitmapSource SourceImage
        {
            get => _sourceImage;
            set
            {
                _sourceImage = value;
                HasSourceImage = value != null;
                OnPropertyChanged();
            }
        }

        public BitmapSource ResultImage
        {
            get => _resultImage;
            set
            {
                _resultImage = value;
                HasResultImage = value != null;
                OnPropertyChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                _isGenerating = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool HasSourceImage
        {
            get => _hasSourceImage;
            set
            {
                _hasSourceImage = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool HasResultImage
        {
            get => _hasResultImage;
            set
            {
                _hasResultImage = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string[] AvailableViewports => ScreenCapture.GetAvailableViewports();

        // Commands
        public ICommand CaptureCommand { get; }
        public ICommand GenerateCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand SaveResultCommand { get; }
        public ICommand UseResultAsSourceCommand { get; }

        private void CaptureScreen()
        {
            try
            {
                StatusMessage = "Capturing viewport...";
                var bitmap = ScreenCapture.CaptureActiveView();

                if (bitmap != null)
                {
                    SourceImage = ScreenCapture.BitmapToBitmapSource(bitmap);
                    ResultImage = null;

                    // Set source dimensions
                    Settings.SetSourceDimensions(bitmap.Width, bitmap.Height);

                    StatusMessage = $"Captured: {bitmap.Width}x{bitmap.Height}";
                    RhinoApp.WriteLine($"Screenshot captured: {bitmap.Width}x{bitmap.Height}");
                }
                else
                {
                    StatusMessage = "Failed to capture viewport";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                RhinoApp.WriteLine($"Capture error: {ex}");
            }
        }

        private bool CanGenerate()
        {
            return !IsGenerating && HasSourceImage &&
                   !string.IsNullOrWhiteSpace(Settings.ApiUrl) &&
                   !string.IsNullOrWhiteSpace(Settings.ApiKey);
        }

        private async Task GenerateImageAsync()
        {
            // Save settings before generating
            SaveSettings();

            if (SourceImage == null)
            {
                StatusMessage = "Please capture a source image first";
                return;
            }

            if (string.IsNullOrWhiteSpace(Settings.Prompt))
            {
                StatusMessage = "Please enter a prompt";
                return;
            }

            try
            {
                IsGenerating = true;
                StatusMessage = "Generating...";

                // Convert BitmapSource back to Bitmap for API
                var sourceBitmap = ScreenCapture.BitmapSourceToBitmap(SourceImage);

                var resultBitmap = await _apiService.GenerateImageAsync(
                    Settings.ApiUrl,
                    Settings.ApiKey,
                    Settings.Prompt,
                    sourceBitmap,
                    Settings);

                if (resultBitmap != null)
                {
                    ResultImage = ScreenCapture.BitmapToBitmapSource(resultBitmap);
                    StatusMessage = $"Generated: {resultBitmap.Width}x{resultBitmap.Height}";
                    RhinoApp.WriteLine($"Image generated successfully: {resultBitmap.Width}x{resultBitmap.Height}");
                }
                else
                {
                    StatusMessage = "Generation failed - check API response";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                RhinoApp.WriteLine($"Generation error: {ex}");
            }
            finally
            {
                IsGenerating = false;
            }
        }

        private void ClearAll()
        {
            SourceImage = null;
            ResultImage = null;
            Settings.Prompt = "";
            StatusMessage = "Ready";
        }

        private void SaveResult()
        {
            if (ResultImage == null) return;

            try
            {
                var bitmap = ScreenCapture.BitmapSourceToBitmap(ResultImage);

                var saveDialog = new SaveFileDialog
                {
                    Filter = "PNG Image|*.png|JPEG Image|*.jpg|All Files|*.*",
                    DefaultExt = ".png",
                    FileName = $"AIRender_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    var format = Path.GetExtension(saveDialog.FileName).ToLower() == ".jpg"
                        ? System.Drawing.Imaging.ImageFormat.Jpeg
                        : System.Drawing.Imaging.ImageFormat.Png;

                    bitmap.Save(saveDialog.FileName, format);
                    StatusMessage = $"Saved to: {saveDialog.FileName}";
                    RhinoApp.WriteLine($"Result saved to: {saveDialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Save error: {ex.Message}";
            }
        }

        private void UseResultAsSource()
        {
            if (ResultImage == null) return;

            try
            {
                // Copy result to source
                SourceImage = ResultImage;
                ResultImage = null;

                // Update dimensions
                if (SourceImage != null)
                {
                    Settings.SetSourceDimensions((int)SourceImage.Width, (int)SourceImage.Height);
                }

                StatusMessage = "Result copied to source";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Simple ICommand implementation for MVVM
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object parameter) => _execute();

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}
