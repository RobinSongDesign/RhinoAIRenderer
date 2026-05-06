using AIRenderer.Models;
using AIRenderer.Services;
using Microsoft.Win32;
using Rhino;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace AIRenderer.ViewModels
{
    public class BatchRenderViewModel : INotifyPropertyChanged
    {
        private readonly AIRenderService _renderService;
        private ObservableCollection<ViewRenderItem> _items = new ObservableCollection<ViewRenderItem>();
        private bool _isRunning;
        private string _statusMessage = Loc.Get("msg.click_to_load");
        private int _doneCount;
        private int _totalCount;
        private CancellationTokenSource _cts;
        private RenderSettings _settings;
        private System.Windows.Media.Imaging.BitmapSource _referenceImage;

        /// <param name="sharedSettings">传入主 ViewModel 的 Settings 实现共享；为 null 时自行加载</param>
        public BatchRenderViewModel(RenderSettings sharedSettings = null)
        {
            _renderService = new AIRenderService();

            if (sharedSettings != null)
            {
                _settings = sharedSettings;
            }
            else
            {
                _settings = new RenderSettings();
                var (_, selectedModel, provider) = SettingsService.LoadSettingsWithProvider();
                _settings.SelectedProviderItem = provider;
                _settings.SelectedModel = selectedModel;
            }

            _items.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasBatchItems));
            LoadAndCaptureCommand = new RelayCommand(LoadAndCapture, () => !IsRunning);
            RenderSelectedCommand = new RelayCommand(async () => await RenderSelectedAsync(), CanRenderSelected);
            CancelCommand       = new RelayCommand(Cancel, () => IsRunning);
            SaveAllCommand      = new RelayCommand(SaveAll, () => Items.Any(i => i.HasResult) && !IsRunning);
            SelectAllCommand    = new RelayCommand(() => { foreach (var i in Items) i.IsSelected = true; });
            DeselectAllCommand  = new RelayCommand(() => { foreach (var i in Items) i.IsSelected = false; });
        }

        // ── Properties ─────────────────────────────────────────────────────────

        public ObservableCollection<ViewRenderItem> Items
        {
            get => _items;
            set { _items = value; OnPropertyChanged(); }
        }

        public RenderSettings Settings => _settings;
        public bool HasBatchItems => _items.Count > 0;
        public string ProviderDisplayName => _settings.SelectedProviderItem?.DisplayName ?? "—";

        public System.Windows.Media.Imaging.BitmapSource ReferenceImage
        {
            get => _referenceImage;
            set { _referenceImage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasReferenceImage)); }
        }
        public bool HasReferenceImage => _referenceImage != null;

        public bool IsRunning
        {
            get => _isRunning;
            set { _isRunning = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public int DoneCount
        {
            get => _doneCount;
            set { _doneCount = value; OnPropertyChanged(); }
        }

        public int TotalCount
        {
            get => _totalCount;
            set { _totalCount = value; OnPropertyChanged(); }
        }

        // ── Commands ───────────────────────────────────────────────────────────

        public ICommand LoadAndCaptureCommand { get; }
        public ICommand RenderSelectedCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SaveAllCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand DeselectAllCommand { get; }

        // ── Load + Capture ─────────────────────────────────────────────────────

        private void LoadAndCapture()
        {
            var names = ScreenCapture.GetNamedViewNames();
            Items.Clear();

            if (names.Count == 0)
            {
                StatusMessage = Loc.Get("msg.no_named_views");
                return;
            }

            foreach (var name in names)
                Items.Add(new ViewRenderItem { ViewName = name, Status = Loc.Get("status.waiting") });

            StatusMessage = string.Format(Loc.Get("msg.loading"), names.Count);
            CommandManager.InvalidateRequerySuggested();

            // 立即捕获全部预览
            foreach (var item in Items)
                CaptureItem(item);

            StatusMessage = string.Format(Loc.Get("msg.loaded"), Items.Count(i => i.HasSourceImage), Items.Count);
        }

        // ── Capture helpers (called from code-behind) ─────────────────────────

        public void CaptureItem(ViewRenderItem item)
        {
            try
            {
                item.Status = Loc.Get("status.capturing");
                var bitmap = ScreenCapture.CaptureNamedView(item.ViewName);
                if (bitmap != null)
                {
                    item.SourceImage = ScreenCapture.BitmapToBitmapSource(bitmap);
                    item.Status = Loc.Get("status.ready");
                }
                else
                {
                    item.Status = Loc.Get("status.capture_failed");
                }
            }
            catch (Exception ex)
            {
                item.Status = "捕获失败";
                RhinoApp.WriteLine($"Capture error '{item.ViewName}': {ex.Message}");
            }
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// 单独重新生成某一项，以其他已完成项的结果作为一致性参考。
        /// </summary>
        public async Task RegenerateItemAsync(ViewRenderItem item)
        {
            if (item == null || item.IsGenerating || !item.HasSourceImage) return;

            var provider = _settings.SelectedProviderItem
                ?? ProviderItem.FromBuiltIn(ApiProviderConfig.GetConfig(ApiProvider.BltAI));
            string apiKey = SettingsService.GetApiKey(provider.Id);

            // 其他所有已完成项的结果作一致性参考
            var references = Items
                .Where(i => i != item && i.HasResult)
                .Select(i => ScreenCapture.BitmapSourceToBitmap(i.ResultImage))
                .Where(b => b != null)
                .ToList();

            Bitmap refBitmap = HasReferenceImage
                ? ScreenCapture.BitmapSourceToBitmap(_referenceImage)
                : null;

            string itemPrompt = _settings.Prompt;
            if (!string.IsNullOrWhiteSpace(item.AddonPrompt))
                itemPrompt += "\n\n" + item.AddonPrompt;

            item.IsGenerating = true;
            item.Status = references.Count > 0
                ? string.Format(Loc.Get("msg.regen_ref"), references.Count)
                : Loc.Get("status.regenerating");

            try
            {
                var sourceBitmap = ScreenCapture.BitmapSourceToBitmap(item.SourceImage);
                Bitmap result;

                if (references.Count > 0 && provider.ApiFormat != "openai")
                {
                    result = await _renderService.GenerateChainedAsync(
                        provider, apiKey, itemPrompt, sourceBitmap, references, _settings, refBitmap);
                }
                else
                {
                    result = await _renderService.GenerateImageAsync(
                        provider, apiKey, itemPrompt, sourceBitmap, _settings, refBitmap);
                }

                if (result != null)
                {
                    item.ResultImage = ScreenCapture.BitmapToBitmapSource(result);
                    item.Status = Loc.Get("status.done");
                }
                else
                {
                    item.Status = Loc.Get("status.failed");
                }
            }
            catch (Exception ex)
            {
                item.Status = Loc.Get("status.error");
                RhinoApp.WriteLine($"Regenerate error '{item.ViewName}': {ex.Message}");
            }
            finally
            {
                item.IsGenerating = false;
            }
        }

        // ── Batch render ──────────────────────────────────────────────────────

        private bool CanRenderSelected()
            => !IsRunning && Items.Any(i => i.IsSelected && i.HasSourceImage);

        private async Task RenderSelectedAsync()
        {
            var selected = Items.Where(i => i.IsSelected && i.HasSourceImage).ToList();
            if (selected.Count == 0) return;

            var provider = _settings.SelectedProviderItem
                ?? ProviderItem.FromBuiltIn(ApiProviderConfig.GetConfig(ApiProvider.BltAI));
            string apiKey = SettingsService.GetApiKey(provider.Id);

            IsRunning = true;
            _cts = new CancellationTokenSource();
            DoneCount = 0;
            TotalCount = selected.Count;

            // 参考图（每次请求都携带）
            Bitmap refBitmap = HasReferenceImage
                ? ScreenCapture.BitmapSourceToBitmap(_referenceImage)
                : null;

            // 累计成功渲染的结果，供后续请求作一致性参考
            var previousResults = new List<Bitmap>();

            for (int i = 0; i < selected.Count; i++)
            {
                if (_cts.IsCancellationRequested) break;

                var item = selected[i];
                item.IsGenerating = true;
                item.Status = previousResults.Count == 0
                    ? Loc.Get("status.generating")
                    : string.Format(Loc.Get("msg.generating_ref"), previousResults.Count);
                StatusMessage = string.Format(Loc.Get("msg.generating_item"), i + 1, TotalCount, item.ViewName);

                // 合并共用 prompt + 当前视图的独立 addon prompt
                string itemPrompt = _settings.Prompt;
                if (!string.IsNullOrWhiteSpace(item.AddonPrompt))
                    itemPrompt += "\n\n" + item.AddonPrompt;

                Bitmap result = null;
                try
                {
                    var sourceBitmap = ScreenCapture.BitmapSourceToBitmap(item.SourceImage);

                    if (previousResults.Count == 0 || provider.ApiFormat == "openai")
                    {
                        result = await _renderService.GenerateImageAsync(
                            provider, apiKey, itemPrompt, sourceBitmap, _settings, refBitmap);
                    }
                    else
                    {
                        result = await _renderService.GenerateChainedAsync(
                            provider, apiKey, itemPrompt, sourceBitmap, previousResults, _settings, refBitmap);
                    }

                    if (result != null)
                    {
                        item.ResultImage = ScreenCapture.BitmapToBitmapSource(result);
                        item.Status = Loc.Get("status.done");
                        previousResults.Add(result); // 加入参考链
                    }
                    else
                    {
                        item.Status = Loc.Get("status.failed");
                        // 失败时不加入链，避免污染后续参考
                    }
                }
                catch (Exception ex)
                {
                    item.Status = Loc.Get("status.error");
                    RhinoApp.WriteLine($"Chain render error '{item.ViewName}': {ex.Message}");
                }
                finally
                {
                    item.IsGenerating = false;
                    DoneCount++;
                }
            }

            IsRunning = false;
            StatusMessage = _cts.IsCancellationRequested
                ? string.Format(Loc.Get("msg.cancelled"), DoneCount, TotalCount)
                : string.Format(Loc.Get("msg.all_done"), DoneCount);
            CommandManager.InvalidateRequerySuggested();
        }

        private void Cancel() => _cts?.Cancel();

        // ── Save ──────────────────────────────────────────────────────────────

        public void SaveItem(ViewRenderItem item)
        {
            if (!item.HasResult) return;
            var dialog = new SaveFileDialog
            {
                Title = $"保存 {item.ViewName}",
                Filter = "PNG 图片|*.png|JPEG 图片|*.jpg",
                FileName = SanitizeName(item.ViewName),
                DefaultExt = ".png"
            };
            if (dialog.ShowDialog() != true) return;

            var fmt = Path.GetExtension(dialog.FileName).ToLower() == ".jpg"
                ? System.Drawing.Imaging.ImageFormat.Jpeg
                : System.Drawing.Imaging.ImageFormat.Png;
            ScreenCapture.BitmapSourceToBitmap(item.ResultImage).Save(dialog.FileName, fmt);
        }

        private void SaveAll()
        {
            var done = Items.Where(i => i.HasResult).ToList();
            if (!done.Any()) return;

            // Use SaveFileDialog to pick a folder (user saves "dummy" file → we use its directory)
            var dialog = new SaveFileDialog
            {
                Title = "选择保存目录（在目标文件夹中输入任意文件名）",
                Filter = "PNG 图片|*.png",
                FileName = "在此处选择文件夹",
                CheckFileExists = false
            };
            if (dialog.ShowDialog() != true) return;

            string folder = Path.GetDirectoryName(dialog.FileName);
            int saved = 0;

            foreach (var item in done)
            {
                try
                {
                    string path = Path.Combine(folder, SanitizeName(item.ViewName) + ".png");
                    ScreenCapture.BitmapSourceToBitmap(item.ResultImage)
                        .Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    saved++;
                }
                catch (Exception ex)
                {
                    RhinoApp.WriteLine($"Save error '{item.ViewName}': {ex.Message}");
                }
            }

            StatusMessage = $"已保存 {saved} 张到 {folder}";
        }

        private static string SanitizeName(string name)
            => string.Concat(name.Split(Path.GetInvalidFileNameChars()));

        // ── INotifyPropertyChanged ─────────────────────────────────────────────

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
