using Eto.Drawing;
using Rhino;

namespace Chameleon.Rhino.UI
{
    /// <summary>Remembers the window's position and size in the plug-in's persistent settings.</summary>
    internal sealed class WindowPlacement
    {
        private const string KeyX = "Window.X";
        private const string KeyY = "Window.Y";
        private const string KeyWidth = "Window.Width";
        private const string KeyHeight = "Window.Height";

        private readonly PersistentSettings _settings;

        public WindowPlacement(PersistentSettings settings)
        {
            _settings = settings;
        }

        /// <summary>The saved bounds, or null when none were saved yet or they are no longer usable.</summary>
        public Rectangle? Load(Size minimum)
        {
            int width = _settings.GetInteger(KeyWidth, 0);
            int height = _settings.GetInteger(KeyHeight, 0);
            if (width < minimum.Width || height < minimum.Height)
            {
                return null;
            }

            return new Rectangle(_settings.GetInteger(KeyX, 0), _settings.GetInteger(KeyY, 0), width, height);
        }

        public void Save(Rectangle bounds)
        {
            _settings.SetInteger(KeyX, bounds.X);
            _settings.SetInteger(KeyY, bounds.Y);
            _settings.SetInteger(KeyWidth, bounds.Width);
            _settings.SetInteger(KeyHeight, bounds.Height);
        }
    }
}
