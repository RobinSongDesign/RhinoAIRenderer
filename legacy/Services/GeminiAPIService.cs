using AIRenderer.Models;
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
    public class GeminiAPIService
    {
        private readonly HttpClient _httpClient;

        public GeminiAPIService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }

        /// <summary>
        /// Generates an image using the Gemini REST API
        /// </summary>
        public async Task<Bitmap> GenerateImageAsync(
            string baseUrl,
            string apiKey,
            string prompt,
            Bitmap sourceImage,
            RenderSettings settings)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                RhinoApp.WriteLine("API URL is required.");
                return null;
            }

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

            // Combine system prompt with user prompt
            string fullPrompt = prompt;
            if (!string.IsNullOrWhiteSpace(settings.SystemPrompt))
            {
                fullPrompt = $"{settings.SystemPrompt}\n\n{prompt}";
            }

            try
            {
                // Convert source image to base64
                string imageBase64 = ScreenCapture.ToBase64(sourceImage, ImageFormat.Jpeg);

                // Construct full URL
                string model = settings.SelectedModel;
                string fullUrl = $"{baseUrl.TrimEnd('/')}/v1beta/models/{model}:generateContent";

                // Get aspect ratio and image size from settings
                string aspectRatio = settings.SelectedAspectRatio?.Ratio ?? "";
                string imageSize = settings.SelectedImageSize ?? "1K";

                // Build imageConfig - only include aspectRatio if specified
                object imageConfig;
                if (string.IsNullOrEmpty(aspectRatio))
                {
                    imageConfig = new { imageSize = imageSize };
                }
                else
                {
                    imageConfig = new { aspectRatio = aspectRatio, imageSize = imageSize };
                }

                // Build the request payload in Gemini format
                var payload = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new object[]
                            {
                                new { text = fullPrompt },
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
                        imageConfig = imageConfig
                    }
                };

                var jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Set API key header
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);

                RhinoApp.WriteLine($"Calling API: {fullUrl}");
                RhinoApp.WriteLine($"Model: {model}");
                RhinoApp.WriteLine($"Prompt: {fullPrompt}");
                RhinoApp.WriteLine($"Aspect Ratio: {aspectRatio}, Image Size: {imageSize}");

                // Make the API request
                var response = await _httpClient.PostAsync(fullUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    RhinoApp.WriteLine($"API Error ({response.StatusCode}): {errorContent}");
                    return null;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                RhinoApp.WriteLine($"API Response received: {responseContent.Length} characters");

                // Parse the response to get the generated image
                return ParseGeneratedImage(responseContent);
            }
            catch (HttpRequestException ex)
            {
                RhinoApp.WriteLine($"HTTP Error: {ex.Message}");
                return null;
            }
            catch (TaskCanceledException ex)
            {
                RhinoApp.WriteLine($"Request timeout: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parses the API response to extract the generated image
        /// </summary>
        private Bitmap ParseGeneratedImage(string responseContent)
        {
            try
            {
                var json = JObject.Parse(responseContent);

                // Gemini response format: candidates[0].content.parts[].inlineData.data
                var candidates = json["candidates"];
                if (candidates == null || !candidates.HasValues)
                {
                    RhinoApp.WriteLine("No candidates in response.");
                    RhinoApp.WriteLine($"Response: {responseContent}");
                    return null;
                }

                var firstCandidate = candidates[0];
                var content = firstCandidate["content"];
                if (content == null)
                {
                    RhinoApp.WriteLine("No content in candidate.");
                    return null;
                }

                var parts = content["parts"];
                if (parts == null)
                {
                    RhinoApp.WriteLine("No parts in content.");
                    return null;
                }

                // Find the part with inlineData
                string base64Image = null;
                foreach (var part in parts)
                {
                    var inlineData = part["inlineData"];
                    if (inlineData != null)
                    {
                        base64Image = inlineData["data"]?.ToString();
                        if (!string.IsNullOrEmpty(base64Image))
                            break;
                    }
                }

                if (string.IsNullOrWhiteSpace(base64Image))
                {
                    RhinoApp.WriteLine("No image found in API response.");
                    RhinoApp.WriteLine($"Response: {responseContent}");
                    return null;
                }

                // Decode base64 to bitmap
                byte[] imageBytes = Convert.FromBase64String(base64Image);
                using (MemoryStream ms = new MemoryStream(imageBytes))
                {
                    return new Bitmap(ms);
                }
            }
            catch (JsonException ex)
            {
                RhinoApp.WriteLine($"JSON Parse Error: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Image Parse Error: {ex.Message}");
                return null;
            }
        }
    }
}
