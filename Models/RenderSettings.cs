using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIRenderer.Models
{
    public class RenderSettings : INotifyPropertyChanged
    {
        private ApiProvider _selectedProvider = ApiProvider.BltAI;
        private string _apiKey = "";
        private string _apiUrl = "";
        private string _prompt = "";
        private string _systemPrompt = "这是一张渲染图，不要更改相机位置、fov，保持图中物体结构和透视的一致性。";
        private string _selectedModel = "gemini-3.1-flash-image-preview";

        private int _width = 512;
        private int _height = 512;

        // Source image dimensions (from capture)
        private int _sourceWidth = 0;
        private int _sourceHeight = 0;

        // Available providers
        public List<ApiProviderConfig> AvailableProviders { get; } = ApiProviderConfig.GetAllProviders();

        // Current provider config
        private ApiProviderConfig _currentProviderConfig;

        public RenderSettings()
        {
            // Initialize with default provider (BltAI)
            LoadProviderModels(ApiProvider.BltAI);
        }

        public ApiProvider SelectedProvider
        {
            get => _selectedProvider;
            set
            {
                if (_selectedProvider != value)
                {
                    _selectedProvider = value;
                    OnPropertyChanged();
                    // Load models for the selected provider
                    LoadProviderModels(value);
                }
            }
        }

        private void LoadProviderModels(ApiProvider provider)
        {
            _currentProviderConfig = ApiProviderConfig.GetConfig(provider);
            if (_currentProviderConfig != null)
            {
                AvailableModels = _currentProviderConfig.Models;
                ModelDisplayNames = _currentProviderConfig.ModelDisplayNames;
                // Build model list with display names
                ModelList = new List<ModelItem>();
                foreach (var model in _currentProviderConfig.Models)
                {
                    string displayName = model;
                    if (_currentProviderConfig.ModelDisplayNames != null &&
                        _currentProviderConfig.ModelDisplayNames.TryGetValue(model, out var name))
                    {
                        displayName = name;
                    }
                    ModelList.Add(new ModelItem { DisplayName = displayName, Model = model });
                }
                // Reset to default model
                SelectedModel = _currentProviderConfig.DefaultModel;
                // Set SelectedModelItem to match
                foreach (var item in ModelList)
                {
                    if (item.Model == SelectedModel)
                    {
                        _selectedModelItem = item;
                        break;
                    }
                }
                // Update API URL
                _apiUrl = _currentProviderConfig.BaseUrl;
            }
            OnPropertyChanged(nameof(AvailableModels));
            OnPropertyChanged(nameof(ModelDisplayNames));
            OnPropertyChanged(nameof(SelectedProviderDisplayName));
            OnPropertyChanged(nameof(SelectedModelDisplayName));
            OnPropertyChanged(nameof(ApiUrl));
            OnPropertyChanged(nameof(ModelList));
            OnPropertyChanged(nameof(SelectedModelItem));
        }

        // Display names for UI
        public string SelectedProviderDisplayName => _currentProviderConfig?.DisplayName ?? "Gemini";
        public string SelectedModelDisplayName
        {
            get
            {
                if (_currentProviderConfig?.ModelDisplayNames != null &&
                    _currentProviderConfig.ModelDisplayNames.TryGetValue(_selectedModel, out var displayName))
                {
                    return displayName;
                }
                return _selectedModel;
            }
        }

        // Available models for current provider
        public List<string> AvailableModels { get; private set; } = new List<string>();

        // Model list with display names
        public List<ModelItem> ModelList { get; private set; } = new List<ModelItem>();

        // Model display names for current provider
        public Dictionary<string, string> ModelDisplayNames { get; private set; } = new Dictionary<string, string>();

        // Preset style templates
        public List<StyleTemplate> StyleTemplates { get; } = new List<StyleTemplate>
        {
            new StyleTemplate { Name = "Custom", Prompt = "" },
            new StyleTemplate { Name = "建筑渲染", Prompt = "Professional architectural rendering with photorealistic lighting, high dynamic range (HDR), ambient occlusion, soft shadows, depth of field, ultra-detailed textures, 4K resolution, clean white background or neutral gray studio backdrop, architectural photography style with proper perspective and vanishing points" },
            new StyleTemplate { Name = "MIR风格", Prompt = "MIR architectural visualization style, cinematic rendering, moody atmosphere with dramatic lighting, high contrast, photorealistic finish, unreal engine 5 quality, architectural magazine editorial style, wide angle lens perspective, hyper-detailed render with lens flare and bloom effects" },
            new StyleTemplate { Name = "手绘草图", Prompt = "Architectural sketch rendered in realistic hand-drawn graphite pencil technique on textured paper, architectural line drawing with proper proportions, architectural presentation sketch style, loose gestural strokes, cross-hatching for shading, sketchbook aesthetic, white or cream paper background" },
            new StyleTemplate { Name = "赛博朋克", Prompt = "Cyberpunk aesthetic with neon lighting, reflective wet surfaces, holographic billboards, volumetric fog, dramatic low-angle shot, futuristic cityscape at night, cinematic color grading with cyan and magenta accents, Blade Runner inspired atmosphere, high contrast, lens flare, bokeh" },
            new StyleTemplate { Name = "极简主义", Prompt = "Minimalist architectural photography, clean geometric compositions, abundant negative space, soft natural lighting from large windows, monochrome or neutral color palette, high-key studio lighting, Hasselblad medium format camera quality, architectural digest editorial style, pristine white backgrounds" },
            new StyleTemplate { Name = "室内设计", Prompt = "Interior design photography with warm ambient lighting, golden hour natural light streaming through windows, cozy atmosphere, professional interior magazine shoot, depth of field with subject in focus and blurred background, 35mm lens perspective, lifestyle photography style, high-end residential interior, proper white balance" },
            new StyleTemplate { Name = "写实摄影", Prompt = "Professional architectural photography shot with Canon EOS R5 or Sony A7R V, 24-70mm f/2.8 lens, proper exposure with f/8 for maximum sharpness, perspective correction, architectural tripod setup, neutral density filters for long exposure, hyperfocal distance focusing, commercial real estate photography style" },
            new StyleTemplate { Name = "黄昏氛围", Prompt = "Golden hour architectural photography with warm sunset lighting casting long dramatic shadows, sky gradient from orange to purple, silhouette effect, romantic mood, cinematic landscape photography, time-lapse inspired aesthetic, professional architectural twilight shot, warm color temperature around 3200K" },
            new StyleTemplate { Name = "水彩画", Prompt = "Watercolor painting illustration style, soft bleeding colors, wet-on-wet technique, architectural subject rendered in artistic watercolor, light and airy palette, delicate brush strokes, textured watercolor paper background, architectural illustration in art gallery style" },
            new StyleTemplate { Name = "电影海报", Prompt = "Movie poster style composition, dramatic cinematic lighting, shallow depth of field, subject framed as hero, cinematic color grading with teal and orange tones, film grain texture, anamorphic lens flare, epic wide shot, IMAX aspect ratio, Hollywood movie production quality" }
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

        private string _selectedImageSize = "1K";
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
        private ModelItem _selectedModelItem;
        public ModelItem SelectedModelItem
        {
            get => _selectedModelItem;
            set
            {
                _selectedModelItem = value;
                if (value != null)
                {
                    _selectedModel = value.Model;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedModel));
            }
        }

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

    public class ModelItem
    {
        public string DisplayName { get; set; }
        public string Model { get; set; }
    }
}
