using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rhino;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AIRenderer.Services
{
    /// <summary>
    /// 通用 Generations API 服务 (图生图&文生图)
    /// </summary>
    public class GenerationsAPIService
    {
        private readonly HttpClient _httpClient;
        private readonly string _systemPrompt = "这是一张渲染图，不要更改相机位置、fov，保持图中物体结构和透视的一致性。";

        public GenerationsAPIService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }

        /// <summary>
        /// 生成图片
        /// </summary>
        public async Task<Bitmap> GenerateImageAsync(
            string baseUrl,
            string apiKey,
            string prompt,
            Bitmap sourceImage,
            string model,
            string aspectRatio,
            string imageSize)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                RhinoApp.WriteLine("API key is required.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                RhinoApp.WriteLine("Model is required.");
                return null;
            }

            // Build prompt with system prompt
            string fullPrompt = prompt;
            if (!string.IsNullOrWhiteSpace(_systemPrompt))
            {
                fullPrompt = $"{_systemPrompt}\n\n{prompt}";
            }

            // Add reference image to prompt if source image exists
            string imageParam = null;
            if (sourceImage != null)
            {
                string imageBase64 = ScreenCapture.ToBase64(sourceImage, ImageFormat.Png);
                imageParam = imageBase64;
            }

            try
            {
                string fullUrl = $"{baseUrl.TrimEnd('/')}/v1/images/generations";

                // Build request payload
                object payload;
                if (!string.IsNullOrEmpty(imageParam))
                {
                    // Image to image
                    payload = new
                    {
                        model = model,
                        prompt = fullPrompt,
                        aspect_ratio = aspectRatio,
                        image = new[] { imageParam }
                    };
                }
                else
                {
                    // Text to image
                    payload = new
                    {
                        model = model,
                        prompt = fullPrompt,
                        aspect_ratio = aspectRatio
                    };
                }

                var jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                RhinoApp.WriteLine($"Calling Generations API: {fullUrl}");
                RhinoApp.WriteLine($"Model: {model}, Prompt: {fullPrompt.Substring(0, Math.Min(50, fullPrompt.Length))}...");

                var response = await _httpClient.PostAsync(fullUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    RhinoApp.WriteLine($"API Error ({response.StatusCode}): {errorContent}");
                    return null;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                return ParseGenerationsResponse(responseContent);
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Generations API Error: {ex.Message}");
                return null;
            }
        }

        private Bitmap ParseGenerationsResponse(string responseContent)
        {
            try
            {
                var json = JObject.Parse(responseContent);

                // Try different response formats
                // Format 1: data[0].url
                var data = json["data"];
                if (data != null && data.HasValues)
                {
                    var firstItem = data[0];
                    var url = firstItem?["url"]?.ToString();
                    if (!string.IsNullOrEmpty(url))
                    {
                        return LoadImageFromUrl(url);
                    }

                    // Format 2: data[0].b64_json
                    var b64Json = firstItem?["b64_json"]?.ToString();
                    if (!string.IsNullOrEmpty(b64Json))
                    {
                        byte[] imageBytes = Convert.FromBase64String(b64Json);
                        MemoryStream ms = new MemoryStream(imageBytes);
                        Bitmap bmp = new Bitmap(ms);
                        ms.Dispose();
                        return bmp;
                    }
                }

                // Format 3: image (direct base64)
                var imageBase64 = json["image"]?.ToString();
                if (!string.IsNullOrEmpty(imageBase64))
                {
                    byte[] imageBytes = Convert.FromBase64String(imageBase64);
                    MemoryStream ms = new MemoryStream(imageBytes);
                    Bitmap bmp = new Bitmap(ms);
                    ms.Dispose();
                    return bmp;
                }

                RhinoApp.WriteLine("No image found in Generations API response.");
                RhinoApp.WriteLine($"Response: {responseContent.Substring(0, Math.Min(500, responseContent.Length))}");
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
                // Download image from URL
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
    }
}
