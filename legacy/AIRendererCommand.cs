using AIRenderer.Views;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using System;
using System.Windows;

namespace AIRenderer
{
    /// <summary>
    /// New AI Render command for image-to-image generation
    /// </summary>
    public class AIRenderCommand : Command
    {
        public AIRenderCommand()
        {
            Instance = this;
        }

        public static AIRenderCommand Instance { get; private set; }

        public override string EnglishName => "AIRender";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            try
            {
                // Get Rhino's main window handle
                IntPtr rhinoHandle = RhinoApp.MainWindowHandle();

                if (Application.Current != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var window = new AIRenderWindow();
                        // Set owner to Rhino main window to prevent hiding
                        var helper = new System.Windows.Interop.WindowInteropHelper(window);
                        helper.Owner = rhinoHandle;
                        window.Show();
                    }));
                }
                else
                {
                    var window = new AIRenderWindow();
                    var helper = new System.Windows.Interop.WindowInteropHelper(window);
                    helper.Owner = rhinoHandle;
                    window.Show();
                }

                RhinoApp.WriteLine("AIRender window opened.");
                return Result.Success;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Error opening AIRender window: {ex.Message}");
                return Result.Failure;
            }
        }
    }
}
