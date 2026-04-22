using AIRenderer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rhino;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AIRenderer.Services
{
    public class AIRenderService
    {
        private readonly HttpClient _httpClient;

        public AIRenderService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }

        /// <summary>
        /// 生成图片（ProviderItem 版，支持内置和自定义服务商）
        /// </summary>
        public async Task<Bitmap> GenerateImageAsync(
            ProviderItem provider,
            string apiKey,
            string prompt,
            Bitmap sourceImage,
            RenderSettings settings)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                RhinoApp.WriteLine("API key is required.");
                return null;
            }
            if (sourceImage == null)
            {
                RhinoApp.WriteLine("Source image is required.");
                return null;
            }

            // 只有未被覆盖的内置 Gemini 格式服务商走枚举路由
            if (!provider.IsCustom && provider.BuiltInProvider.HasValue && provider.ApiFormat == "gemini")
                return await GenerateImageAsync(provider.BuiltInProvider.Value, apiKey, prompt, sourceImage, settings);

            string fullPrompt = prompt;
            if (!string.IsNullOrWhiteSpace(settings.SystemPrompt))
                fullPrompt = $"{settings.SystemPrompt}\n\n{prompt}";

            if (provider.ApiFormat == "openai")
                return await GenerateOpenAIAsync(provider, apiKey, fullPrompt, sourceImage, settings);

            return await GenerateCustomAsync(provider, apiKey, fullPrompt, sourceImage, settings);
        }

        /// <summary>
        /// 生成图片（根据不同服务商使用不同请求格式）
        /// </summary>
        public async Task<Bitmap> GenerateImageAsync(
            ApiProvider provider,
            string apiKey,
            string prompt,
            Bitmap sourceImage,
            RenderSettings settings)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                RhinoApp.WriteLine("API key is required.");
                return null;
            }

            if (sourceImage == null)
            {
                RhinoApp.WriteLine("Source image is required.");
                return null;
            }

            var config = ApiProviderConfig.GetConfig(provider);
            if (config == null)
            {
                RhinoApp.WriteLine("Unknown API provider.");
                return null;
            }

            // Combine system prompt with user prompt
            string fullPrompt = prompt;
            if (!string.IsNullOrWhiteSpace(settings.SystemPrompt))
            {
                fullPrompt = $"{settings.SystemPrompt}\n\n{prompt}";
            }

            switch (provider)
            {
                case ApiProvider.Gemini:
                    return await GenerateGeminiAsync(config, apiKey, fullPrompt, sourceImage, settings);
                case ApiProvider.BltAI:
                    return await GenerateBltAIAsync(config, apiKey, fullPrompt, sourceImage, settings);
                default:
                    return null;
            }
        }

        #region Gemini
        private async Task<Bitmap> GenerateGeminiAsync(
            ApiProviderConfig config, string apiKey, string prompt,
            Bitmap sourceImage, RenderSettings settings)
        {
            try
            {
                string imageBase64 = ScreenCapture.ToBase64(sourceImage, ImageFormat.Jpeg);
                string model = settings.SelectedModel ?? config.DefaultModel;
                string fullUrl = $"{config.BaseUrl.TrimEnd('/')}/v1beta/models/{model}:generateContent";

                string aspectRatio = settings.SelectedAspectRatio?.Ratio ?? "";
                string imageSize = settings.SelectedImageSize ?? "1K";

                string jsonImageConfig;
                if (string.IsNullOrEmpty(aspectRatio))
                {
                    jsonImageConfig = $"{{\"imageSize\":\"{imageSize}\"}}";
                }
                else
                {
                    jsonImageConfig = $"{{\"aspectRatio\":\"{aspectRatio}\",\"imageSize\":\"{imageSize}\"}}";
                }

                var payload = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new object[]
                            {
                                new { text = prompt },
                                new
                                {
                                    inline_data = new
                                    {
                                        mime_type = "image/png",
                                        data = imageBase64
                                    }
                                }
                            }
                        }
                    },
                    tools = new[] { new { google_search = new object() } },
                    generationConfig = new
                    {
                        responseModalities = new[] { "TEXT", "IMAGE" },
                        imageConfig = JsonConvert.DeserializeObject(jsonImageConfig)
                    }
                };

                var jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);

                RhinoApp.WriteLine($"Calling Gemini API: {fullUrl}");

                var response = await _httpClient.PostAsync(fullUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    RhinoApp.WriteLine($"API Error ({response.StatusCode}): {errorContent}");
                    return null;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                return ParseGeminiResponse(responseContent);
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Gemini API Error: {ex.Message}");
                return null;
            }
        }

        private Bitmap ParseGeminiResponse(string responseContent)
        {
            try
            {
                var json = JObject.Parse(responseContent);
                var candidates = json["candidates"];
                if (candidates == null || !candidates.HasValues)
                {
                    RhinoApp.WriteLine("No candidates in response.");
                    return null;
                }

                var parts = candidates[0]?["content"]?["parts"];
                if (parts == null) return null;

                string base64Image = null;
                foreach (var part in parts)
                {
                    var inlineData = part["inlineData"];
                    if (inlineData != null)
                    {
                        base64Image = inlineData["data"]?.ToString();
                        if (!string.IsNullOrEmpty(base64Image)) break;
                    }
                }

                if (string.IsNullOrWhiteSpace(base64Image))
                {
                    RhinoApp.WriteLine("No image found in API response.");
                    return null;
                }

                byte[] imageBytes = Convert.FromBase64String(base64Image);
                MemoryStream ms = new MemoryStream(imageBytes);
                Bitmap result = new Bitmap(ms);
                ms.Dispose();
                return result;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Parse Error: {ex.Message}");
                return null;
            }
        }
        #endregion

        #region BltAI
        private async Task<Bitmap> GenerateBltAIAsync(
            ApiProviderConfig config, string apiKey, string prompt,
            Bitmap sourceImage, RenderSettings settings)
        {
            try
            {
                string imageBase64 = ScreenCapture.ToBase64(sourceImage, ImageFormat.Png);
                string model = settings.SelectedModel ?? config.DefaultModel;
                string fullUrl = $"{config.BaseUrl.TrimEnd('/')}/v1beta/models/{model}:generateContent";

                string aspectRatio = settings.SelectedAspectRatio?.Ratio ?? "";
                string imageSize = settings.SelectedImageSize ?? "1K";

                string jsonImageConfig;
                if (string.IsNullOrEmpty(aspectRatio))
                {
                    jsonImageConfig = $"{{\"imageSize\":\"{imageSize}\"}}";
                }
                else
                {
                    jsonImageConfig = $"{{\"aspectRatio\":\"{aspectRatio}\",\"imageSize\":\"{imageSize}\"}}";
                }

                // 使用和Gemini官方一样的格式
                var payload = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new object[]
                            {
                                new { text = prompt },
                                new
                                {
                                    inline_data = new
                                    {
                                        mime_type = "image/png",
                                        data = imageBase64
                                    }
                                }
                            }
                        }
                    },
                    tools = new[] { new { google_search = new object() } },
                    generationConfig = new
                    {
                        responseModalities = new[] { "TEXT", "IMAGE" },
                        imageConfig = JsonConvert.DeserializeObject(jsonImageConfig)
                    }
                };

                var jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                RhinoApp.WriteLine($"Calling BltAI API: {fullUrl}");
                RhinoApp.WriteLine($"Model: {model}");

                var response = await _httpClient.PostAsync(fullUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    RhinoApp.WriteLine($"API Error ({response.StatusCode}): {errorContent}");
                    return null;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                RhinoApp.WriteLine($"BltAI Response: {responseContent.Substring(0, Math.Min(500, responseContent.Length))}...");
                return ParseGeminiResponse(responseContent);
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"BltAI API Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// BltAI 使用 Generations API (即梦4)
        /// </summary>
        private async Task<Bitmap> GenerateBltAIAsGenerationsAsync(
            ApiProviderConfig config, string apiKey, string prompt,
            Bitmap sourceImage, RenderSettings settings)
        {
            try
            {
                string model = settings.SelectedModel ?? config.DefaultModel;
                string aspectRatio = settings.SelectedAspectRatio?.Ratio ?? "1:1";

                string fullUrl = $"{config.BaseUrl.TrimEnd('/')}/v1/images/generations";

                // 转换 aspect ratio 格式
                aspectRatio = aspectRatio.Replace(":", "/");

                string imageParam = null;
                if (sourceImage != null)
                {
                    string imageBase64 = ScreenCapture.ToBase64(sourceImage, ImageFormat.Png);
                    imageParam = imageBase64;
                }

                object payload;
                if (!string.IsNullOrEmpty(imageParam))
                {
                    payload = new
                    {
                        model = model,
                        prompt = prompt,
                        image = new[] { imageParam }
                    };
                }
                else
                {
                    payload = new
                    {
                        model = model,
                        prompt = prompt
                    };
                }

                var jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                RhinoApp.WriteLine($"Calling BltAI (Generations API): {fullUrl}");
                RhinoApp.WriteLine($"Model: {model}");

                var response = await _httpClient.PostAsync(fullUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    RhinoApp.WriteLine($"API Error ({response.StatusCode}): {errorContent}");
                    return null;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                RhinoApp.WriteLine($"BltAI Generations Response: {responseContent.Substring(0, Math.Min(500, responseContent.Length))}...");
                return ParseGenerationsResponse(responseContent);
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"BltAI Generations API Error: {ex.Message}");
                return null;
            }
        }

        private Bitmap ParseGenerationsResponse(string responseContent)
        {
            try
            {
                var json = JObject.Parse(responseContent);
                var data = json["data"];
                if (data != null && data.HasValues)
                {
                    var firstItem = data[0];
                    var url = firstItem?["url"]?.ToString();
                    if (!string.IsNullOrEmpty(url))
                    {
                        return LoadImageFromUrl(url);
                    }
                }
                RhinoApp.WriteLine("No image found in Generations API response.");
                return null;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Parse Error: {ex.Message}");
                return null;
            }
        }

        private Bitmap LoadImageFromUrl(string url)
        {
            try
            {
                var imageBytes = _httpClient.GetByteArrayAsync(url).Result;
                MemoryStream ms = new MemoryStream(imageBytes);
                Bitmap bmp = new Bitmap(ms);
                ms.Dispose();
                return bmp;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Failed to load image from URL: {ex.Message}");
                return null;
            }
        }
        #endregion

        #region OpenAI Images API (gpt-image-2)
        private async Task<Bitmap> GenerateOpenAIAsync(
            ProviderItem provider, string apiKey, string prompt,
            Bitmap sourceImage, RenderSettings settings)
        {
            try
            {
                byte[] imageBytes;
                using (var ms = new MemoryStream())
                {
                    sourceImage.Save(ms, ImageFormat.Png);
                    imageBytes = ms.ToArray();
                }

                string model = settings.SelectedModel ?? provider.DefaultModel;
                string fullUrl = $"{provider.BaseUrl.TrimEnd('/')}/v1/images/edits";

                var imageContent = new ByteArrayContent(imageBytes);
                imageContent.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");

                using (var formData = new MultipartFormDataContent())
                {
                    formData.Add(imageContent, "image[]", "image.png");
                    formData.Add(new StringContent(prompt), "prompt");
                    formData.Add(new StringContent(model), "model");
                    formData.Add(new StringContent("1"), "n");

                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                    RhinoApp.WriteLine($"Calling OpenAI Images API ({provider.DisplayName}): {fullUrl}");
                    RhinoApp.WriteLine($"Model: {model}");

                    var response = await _httpClient.PostAsync(fullUrl, formData);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        RhinoApp.WriteLine($"API Error ({response.StatusCode}): {errorContent}");
                        return null;
                    }

                    return ParseOpenAIResponse(await response.Content.ReadAsStringAsync());
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"OpenAI API Error: {ex.Message}");
                return null;
            }
        }

        private Bitmap ParseOpenAIResponse(string responseContent)
        {
            try
            {
                var json = JObject.Parse(responseContent);
                var data = json["data"];
                if (data == null || !data.HasValues)
                {
                    RhinoApp.WriteLine($"No data in OpenAI response: {responseContent.Substring(0, Math.Min(300, responseContent.Length))}");
                    return null;
                }

                var b64 = data[0]?["b64_json"]?.ToString();
                if (!string.IsNullOrEmpty(b64))
                {
                    byte[] imageBytes = Convert.FromBase64String(b64);
                    var ms = new MemoryStream(imageBytes);
                    var result = new Bitmap(ms);
                    ms.Dispose();
                    return result;
                }

                var url = data[0]?["url"]?.ToString();
                if (!string.IsNullOrEmpty(url))
                    return LoadImageFromUrl(url);

                RhinoApp.WriteLine("No image found in OpenAI response.");
                return null;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Parse Error: {ex.Message}");
                return null;
            }
        }
        #endregion

        #region Custom Provider (Gemini-compatible)
        private async Task<Bitmap> GenerateCustomAsync(
            ProviderItem provider, string apiKey, string prompt,
            Bitmap sourceImage, RenderSettings settings)
        {
            try
            {
                string imageBase64 = ScreenCapture.ToBase64(sourceImage, ImageFormat.Png);
                string model = settings.SelectedModel ?? provider.DefaultModel;
                string fullUrl = $"{provider.BaseUrl.TrimEnd('/')}/v1beta/models/{model}:generateContent";

                string aspectRatio = settings.SelectedAspectRatio?.Ratio ?? "";
                string imageSize = settings.SelectedImageSize ?? "1K";
                string jsonImageConfig = string.IsNullOrEmpty(aspectRatio)
                    ? $"{{\"imageSize\":\"{imageSize}\"}}"
                    : $"{{\"aspectRatio\":\"{aspectRatio}\",\"imageSize\":\"{imageSize}\"}}";

                var payload = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new object[]
                            {
                                new { text = prompt },
                                new { inline_data = new { mime_type = "image/png", data = imageBase64 } }
                            }
                        }
                    },
                    tools = new[] { new { google_search = new object() } },
                    generationConfig = new
                    {
                        responseModalities = new[] { "TEXT", "IMAGE" },
                        imageConfig = JsonConvert.DeserializeObject(jsonImageConfig)
                    }
                };

                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                _httpClient.DefaultRequestHeaders.Clear();

                if (provider.AuthType == "goog")
                    _httpClient.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);
                else
                    _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                RhinoApp.WriteLine($"Calling Custom API ({provider.DisplayName}): {fullUrl}");
                var response = await _httpClient.PostAsync(fullUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    RhinoApp.WriteLine($"API Error ({response.StatusCode}): {errorContent}");
                    return null;
                }

                return ParseGeminiResponse(await response.Content.ReadAsStringAsync());
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Custom API Error: {ex.Message}");
                return null;
            }
        }
        #endregion
    }
}
