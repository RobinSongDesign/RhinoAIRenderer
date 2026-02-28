using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIRenderer.Models
{
    public class RenderSettings : INotifyPropertyChanged
    {
        private string _apiUrl = "https://generativelanguage.googleapis.com";
        private string _apiKey = "";
        private string _prompt = "";
        private string _selectedModel = "gemini-3-pro-image-preview";
        private int _seed = -1;
        private int _steps = 20;
        private float _guidanceScale = 7.5f;
        private int _width = 512;
        private int _height = 512;
        private double _strength = 0.75;

        // Available models - Gemini models (Nano Banana)
        public List<string> AvailableModels { get; } = new List<string>
        {
            "gemini-3.1-flash-image-preview",
            "gemini-3-pro-image-preview",
            "gemini-2.5-flash-image"
        };

        // Model display names
        public Dictionary<string, string> ModelDisplayNames { get; } = new Dictionary<string, string>
        {
            { "gemini-3.1-flash-image-preview", "Nano Banana 2" },
            { "gemini-3-pro-image-preview", "Nano Banana Pro" },
            { "gemini-2.5-flash-image", "Nano Banana" }
        };

        public string SelectedModel
        {
            get => _selectedModel;
            set { _selectedModel = value; OnPropertyChanged(); }
        }

        public string ApiUrl
        {
            get => _apiUrl;
            set { _apiUrl = value; OnPropertyChanged(); }
        }

        public string ApiKey
        {
            get => _apiKey;
            set { _apiKey = value; OnPropertyChanged(); }
        }

        public string Prompt
        {
            get => _prompt;
            set { _prompt = value; OnPropertyChanged(); }
        }

        public int Seed
        {
            get => _seed;
            set { _seed = value; OnPropertyChanged(); }
        }

        public int Steps
        {
            get => _steps;
            set { _steps = value; OnPropertyChanged(); }
        }

        public float GuidanceScale
        {
            get => _guidanceScale;
            set { _guidanceScale = value; OnPropertyChanged(); }
        }

        public int Width
        {
            get => _width;
            set { _width = value; OnPropertyChanged(); }
        }

        public int Height
        {
            get => _height;
            set { _height = value; OnPropertyChanged(); }
        }

        public double Strength
        {
            get => _strength;
            set { _strength = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
