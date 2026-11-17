using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Chameleon.App.Hosting;
using Chameleon.Core.Imaging;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

namespace Chameleon.Rhino.Hosting
{
    /// <summary>Viewport captures. Must be called on the UI thread.</summary>
    internal static class ViewCapturer
    {
        /// <summary>Captures the active viewport of the active document.</summary>
        public static ImageData CaptureActiveView(CaptureOptions options)
        {
            return Capture(RequireActiveView(), options);
        }

        /// <summary>
        /// Shows a named view in the active viewport, captures it and restores the previous camera
        /// (same procedure as the WPF version).
        /// </summary>
        public static ImageData CaptureNamedView(string viewName, CaptureOptions options)
        {
            RhinoDoc doc = RequireDoc();
            int index = doc.NamedViews.FindByName(viewName);
            if (index < 0)
            {
                throw new RhinoHostException("Named view '" + viewName + "' was not found.");
            }

            RhinoView view = RequireActiveView();
            RhinoViewport viewport = view.ActiveViewport;
            Point3d savedTarget = viewport.CameraTarget;
            Point3d savedLocation = viewport.CameraLocation;
            Vector3d savedUp = viewport.CameraUp;
            double savedLens = viewport.Camera35mmLensLength;
            bool savedParallel = viewport.IsParallelProjection;

            try
            {
                viewport.SetViewProjection(doc.NamedViews[index].Viewport, true);
                view.Redraw();
                return Capture(view, options);
            }
            finally
            {
                if (savedParallel)
                {
                    viewport.ChangeToParallelProjection(true);
                }
                else
                {
                    viewport.ChangeToPerspectiveProjection(true, savedLens);
                }

                viewport.SetCameraLocations(savedTarget, savedLocation);
                viewport.CameraUp = savedUp;
                view.Redraw();
            }
        }

        /// <summary>Names of the active document's named views.</summary>
        public static string[] NamedViewNames()
        {
            RhinoDoc? doc = RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                return new string[0];
            }

            var names = new string[doc.NamedViews.Count];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = doc.NamedViews[i].Name;
            }

            return names;
        }

        private static ImageData Capture(RhinoView view, CaptureOptions options)
        {
            Size size = view.ActiveViewport.Size;
            int width = options.Width ?? size.Width;
            int height = options.Height ?? size.Height;
            if (width <= 0 || height <= 0)
            {
                throw new RhinoHostException("The viewport has no area to capture.");
            }

            var capture = new ViewCapture { Width = width, Height = height, TransparentBackground = false };
            using (Bitmap? bitmap = capture.CaptureToBitmap(view))
            {
                if (bitmap == null)
                {
                    throw new RhinoHostException("Rhino could not capture the viewport.");
                }

                using (var stream = new MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Png);
                    return ImageData.FromBytes(stream.ToArray());
                }
            }
        }

        private static RhinoDoc RequireDoc()
        {
            return RhinoDoc.ActiveDoc ?? throw new RhinoHostException("No document is open in Rhino.");
        }

        private static RhinoView RequireActiveView()
        {
            return RequireDoc().Views.ActiveView ?? throw new RhinoHostException("Rhino has no active viewport.");
        }
    }
}
