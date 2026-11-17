using System;
using Chameleon.App;
using Chameleon.Rhino.Hosting;

namespace Chameleon.Rhino.UI
{
    /// <summary>Keeps at most one Chameleon window open.</summary>
    internal static class MainWindowController
    {
        private static MainForm? _form;

        /// <summary>Shows the window, creating it (and starting the application) when needed.</summary>
        public static void Show()
        {
            if (_form != null)
            {
                _form.BringToFront();
                _form.Focus();
                return;
            }

            ChameleonPlugin plugin = ChameleonPlugin.Instance ?? throw new InvalidOperationException("The Chameleon plug-in is not loaded.");
            ChameleonApp app = plugin.GetOrStartApp();
            RhinoHost host = plugin.Host ?? throw new InvalidOperationException("The Rhino host did not start.");

            _form = new MainForm(app.BaseUrl, host, new WindowPlacement(plugin.Settings));
            _form.Closed += (sender, e) => _form = null;
            _form.Show();
        }
    }
}
