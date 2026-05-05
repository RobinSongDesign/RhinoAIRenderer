using AIRenderer.Models;
using AIRenderer.Services;
using AIRenderer.ViewModels;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace AIRenderer.Views
{
    public partial class AIRenderWindow : Window
    {
        private AIRenderViewModel _viewModel;

        public AIRenderWindow()
        {
            InitializeComponent();

            var (apiKey, selectedModel, selectedProvider) = SettingsService.LoadSettingsWithProvider();
            _viewModel = new AIRenderViewModel(apiKey, selectedModel, selectedProvider);
            DataContext = _viewModel;
        }

        // ── Mode toggle ───────────────────────────────────────────────────

        private void SingleModeBtn_Click(object sender, RoutedEventArgs e)
            => _viewModel.IsBatchMode = false;

        private void BatchModeBtn_Click(object sender, RoutedEventArgs e)
            => _viewModel.IsBatchMode = true;

        // ── Settings ──────────────────────────────────────────────────────

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow { Owner = this };
            if (settingsWindow.ShowDialog() == true)
            {
                var (apiKey, selectedModel, selectedProvider) = SettingsService.LoadSettingsWithProvider();
                _viewModel.Settings.ApiKey = apiKey;
                _viewModel.Settings.SelectedModel = selectedModel;
                _viewModel.Settings.SelectedProviderItem = selectedProvider;
            }
        }

        // ── Batch operations ──────────────────────────────────────────────

        private void BatchCaptureOne_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is ViewRenderItem item)
                _viewModel.BatchVM.CaptureItem(item);
        }

        private void BatchSaveOne_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is ViewRenderItem item)
                _viewModel.BatchVM.SaveItem(item);
        }

        // ── Reference image ───────────────────────────────────────────────

        private void ReferenceZone_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void ReferenceZone_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            var imageFile = files?.FirstOrDefault(f =>
            {
                var ext = Path.GetExtension(f).ToLower();
                return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".webp";
            });
            if (imageFile != null) SetReferenceImage(imageFile);
        }

        private void ReferenceZone_Click(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel.BatchVM.HasReferenceImage) return;
            var dialog = new OpenFileDialog
            {
                Title = "选择参考图",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.webp"
            };
            if (dialog.ShowDialog() == true) SetReferenceImage(dialog.FileName);
            e.Handled = true;
        }

        private void SetReferenceImage(string filePath)
        {
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.UriSource = new System.Uri(filePath);
                img.EndInit();
                img.Freeze();
                _viewModel.BatchVM.ReferenceImage = img;
            }
            catch { }
        }

        private void ClearReference_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.BatchVM.ReferenceImage = null;
            e.Handled = true;
        }

        // ── Image preview ─────────────────────────────────────────────────

        private void AnyImage_PreviewClick(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is BitmapSource img)
            {
                PreviewImage.Source = img;
                PreviewOverlay.Visibility = Visibility.Visible;
            }
            e.Handled = true;
        }

        private void PreviewOverlay_Click(object sender, MouseButtonEventArgs e)
        {
            PreviewOverlay.Visibility = Visibility.Collapsed;
            PreviewImage.Source = null;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape && PreviewOverlay.Visibility == Visibility.Visible)
            {
                PreviewOverlay.Visibility = Visibility.Collapsed;
                PreviewImage.Source = null;
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e) { }
    }
}
