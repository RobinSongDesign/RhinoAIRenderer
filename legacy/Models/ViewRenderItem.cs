using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace AIRenderer.Models
{
    public class ViewRenderItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private BitmapSource _sourceImage;
        private BitmapSource _resultImage;
        private string _status = "等待捕获";
        private bool _isGenerating;
        private string _addonPrompt = "";

        public string ViewName { get; set; }

        public string AddonPrompt
        {
            get => _addonPrompt;
            set { _addonPrompt = value ?? ""; OnPropertyChanged(); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public BitmapSource SourceImage
        {
            get => _sourceImage;
            set
            {
                _sourceImage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSourceImage));
            }
        }

        public BitmapSource ResultImage
        {
            get => _resultImage;
            set
            {
                _resultImage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasResult));
            }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public bool IsGenerating
        {
            get => _isGenerating;
            set { _isGenerating = value; OnPropertyChanged(); }
        }

        public bool HasSourceImage => _sourceImage != null;
        public bool HasResult => _resultImage != null;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
