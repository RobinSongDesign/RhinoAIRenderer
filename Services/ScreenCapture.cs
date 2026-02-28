using Rhino;
using Rhino.DocObjects;
using Rhino.Display;
using Rhino.Geometry;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace AIRenderer.Services
{
    public static class ScreenCapture
    {
        /// <summary>
        /// Captures the active viewport as a Bitmap
        /// </summary>
        public static Bitmap CaptureActiveView()
        {
            var view = RhinoDoc.ActiveDoc.Views.ActiveView;
            if (view == null)
            {
                RhinoApp.WriteLine("No active view found.");
                return null;
            }

            return CaptureView(view);
        }

        /// <summary>
        /// Captures a Rhino view as a Bitmap
        /// </summary>
        public static Bitmap CaptureView(RhinoView view)
        {
            if (view == null)
                return null;

            try
            {
                // Get viewport size
                int width = view.ActiveViewport.Size.Width;
                int height = view.ActiveViewport.Size.Height;

                if (width <= 0 || height <= 0)
                {
                    RhinoApp.WriteLine("Invalid viewport dimensions.");
                    return null;
                }

                // Use ViewCapture to capture the viewport
                var capture = new ViewCapture
                {
                    Width = width,
                    Height = height,
                    TransparentBackground = false
                };

                var capturedBitmap = capture.CaptureToBitmap(view);
                return capturedBitmap;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Error capturing view: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets available viewport names
        /// </summary>
        public static string[] GetAvailableViewports()
        {
            var views = RhinoDoc.ActiveDoc.Views;
            if (views == null)
                return new string[0];

            return views.Select(v => v.MainViewport.Name).ToArray();
        }

        /// <summary>
        /// Captures a specific view by name
        /// </summary>
        public static Bitmap CaptureViewByName(string viewName)
        {
            var view = RhinoDoc.ActiveDoc.Views.Find(viewName, false);
            if (view == null)
            {
                RhinoApp.WriteLine($"View '{viewName}' not found.");
                return null;
            }

            return CaptureView(view);
        }

        /// <summary>
        /// Saves bitmap to a temporary file and returns the path
        /// </summary>
        public static string SaveToTempFile(Bitmap bitmap, string prefix = "capture")
        {
            if (bitmap == null)
                return null;

            string tempPath = Path.Combine(Path.GetTempPath(), $"{prefix}_{DateTime.Now:yyyyMMddHHmmss}.png");
            bitmap.Save(tempPath, ImageFormat.Png);
            return tempPath;
        }

        /// <summary>
        /// Converts Bitmap to base64 string
        /// </summary>
        public static string ToBase64(Bitmap bitmap, ImageFormat format = null)
        {
            if (bitmap == null)
                return null;

            format = format ?? ImageFormat.Png;
            using (MemoryStream ms = new MemoryStream())
            {
                bitmap.Save(ms, format);
                byte[] imageBytes = ms.ToArray();
                return Convert.ToBase64String(imageBytes);
            }
        }

        /// <summary>
        /// Converts BitmapSource to Bitmap
        /// </summary>
        public static Bitmap BitmapSourceToBitmap(BitmapSource bitmapSource)
        {
            if (bitmapSource == null)
                return null;

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));

            using (MemoryStream ms = new MemoryStream())
            {
                encoder.Save(ms);
                ms.Position = 0;
                return new Bitmap(ms);
            }
        }

        /// <summary>
        /// Converts Bitmap to BitmapSource for WPF display
        /// </summary>
        public static BitmapSource BitmapToBitmapSource(Bitmap bitmap)
        {
            if (bitmap == null)
                return null;

            try
            {
                // Method 1: Using memory stream
                using (MemoryStream ms = new MemoryStream())
                {
                    bitmap.Save(ms, ImageFormat.Png);
                    ms.Position = 0;

                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = ms;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();

                    return bitmapImage;
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"BitmapToBitmapSource error: {ex.Message}");
                try
                {
                    // Method 2: Using CopyPixels via interop
                    var width = bitmap.Width;
                    var height = bitmap.Height;
                    var stride = width * 4;
                    var pixels = new byte[height * stride];

                    var bitmapData = bitmap.LockBits(
                        new Rectangle(0, 0, width, height),
                        System.Drawing.Imaging.ImageLockMode.ReadOnly,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                    System.Runtime.InteropServices.Marshal.Copy(bitmapData.Scan0, pixels, 0, pixels.Length);
                    bitmap.UnlockBits(bitmapData);

                    var bitmapSource = BitmapSource.Create(
                        width, height,
                        bitmap.HorizontalResolution,
                        bitmap.VerticalResolution,
                        System.Windows.Media.PixelFormats.Bgra32,
                        null,
                        pixels,
                        stride);

                    bitmapSource.Freeze();
                    return bitmapSource;
                }
                catch (Exception ex2)
                {
                    RhinoApp.WriteLine($"Fallback conversion error: {ex2.Message}");
                    return null;
                }
            }
        }
    }
}
