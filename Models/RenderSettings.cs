using System;
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
        private string _systemPrompt = "这是一张渲染图，不要更改相机位置、fov，保持图中物体结构和透视的一致性。";
        private string _selectedModel = "gemini-3.1-flash-image-preview";

        private int _width = 512;
        private int _height = 512;

        // Source image dimensions (from capture)
        private int _sourceWidth = 0;
        private int _sourceHeight = 0;

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

        // Preset style templates
        public List<StyleTemplate> StyleTemplates { get; } = new List<StyleTemplate>
        {
            new StyleTemplate { Name = "Custom", Prompt = "" },
            new StyleTemplate { Name = "Architectural Render", Prompt = "Architectural render, high quality, 4K, realistic lighting, white background" },
            new StyleTemplate { Name = "MIR Style", Prompt = "MIR render style, architectural visualization, cinematic, unreal engine 5" },
            new StyleTemplate { Name = "Sketch", Prompt = "Architectural sketch, hand drawn, pencil style, white paper" },
            new StyleTemplate { Name = "Cyberpunk", Prompt = "Cyberpunk style, neon lights, futuristic, dark atmosphere" },
            new StyleTemplate { Name = "Minimalist", Prompt = "Minimalist design, clean lines, white background, modern architecture" },
            new StyleTemplate { Name = "Interior", Prompt = "Loft style, industrial, exposed brick, vintage furniture" }
        };

        private StyleTemplate _selectedStyle;

        // Aspect ratio presets
        public List<AspectRatio> AspectRatios { get; } = new List<AspectRatio>
        {
            new AspectRatio { Name = "Original", Ratio = "" },
            new AspectRatio { Name = "1:1", Ratio = "1:1" },
            new AspectRatio { Name = "4:3", Ratio = "4:3" },
            new AspectRatio { Name = "3:2", Ratio = "3:2" },
            new AspectRatio { Name = "16:9", Ratio = "16:9" },
            new AspectRatio { Name = "9:16", Ratio = "9:16" },
            new AspectRatio { Name = "21:9", Ratio = "21:9" }
        };

        private AspectRatio _selectedAspectRatio;
        public AspectRatio SelectedAspectRatio
        {
            get => _selectedAspectRatio ?? AspectRatios[0];
            set { _selectedAspectRatio = value; OnPropertyChanged(); }
        }

        private string _selectedImageSize = "1024x1024";
        public string SelectedImageSize
        {
            get => _selectedImageSize;
            set { _selectedImageSize = value; OnPropertyChanged(); }
        }

        // Image sizes
        public List<string> ImageSizes { get; } = new List<string>
        {
            "0.5K",
            "1K",
            "2K",
            "4K"
        };
        public StyleTemplate SelectedStyle
        {
            get => _selectedStyle ?? StyleTemplates[0];
            set
            {
                _selectedStyle = value;
                OnPropertyChanged();
                // Clear prompt when selecting a style (replace mode)
                if (value != null && value.Name != "None")
                {
                    Prompt = value.Prompt ?? "";
                }
            }
        }

        // Properties
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

        public string SystemPrompt
        {
            get => _systemPrompt;
            set { _systemPrompt = value; OnPropertyChanged(); }
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

        public int SourceWidth
        {
            get => _sourceWidth;
            set { _sourceWidth = value; OnPropertyChanged(); }
        }

        public int SourceHeight
        {
            get => _sourceHeight;
            set { _sourceHeight = value; OnPropertyChanged(); }
        }

        public void SetSourceDimensions(int width, int height)
        {
            SourceWidth = width;
            SourceHeight = height;
            Width = width;
            Height = height;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class StyleTemplate
    {
        public string Name { get; set; }
        public string Prompt { get; set; }
    }

    public class AspectRatio
    {
        public string Name { get; set; }
        public string Ratio { get; set; }
    }
}
