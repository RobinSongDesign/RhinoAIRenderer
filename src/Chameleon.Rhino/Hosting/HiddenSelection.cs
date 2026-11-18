using System;
using System.Linq;
using Rhino;

namespace Chameleon.Rhino.Hosting
{
    /// <summary>
    /// Clears the selection for the lifetime of the object and restores it afterwards, so viewport
    /// captures show neither selection highlights nor the gumball. Selection is not part of the
    /// undo history, so this leaves the document unchanged. Use on the UI thread.
    /// </summary>
    internal sealed class HiddenSelection : IDisposable
    {
        private readonly RhinoDoc _doc;
        private readonly Guid[] _selected;

        /// <summary>Clears the current selection of a document.</summary>
        public HiddenSelection(RhinoDoc doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _selected = doc.Objects.GetSelectedObjects(false, false).Select(o => o.Id).ToArray();
            if (_selected.Length > 0)
            {
                doc.Objects.UnselectAll();
                // The gumball follows the selection on redraw.
                doc.Views.Redraw();
            }
        }

        /// <summary>Selects the objects again.</summary>
        public void Dispose()
        {
            if (_selected.Length > 0)
            {
                _doc.Objects.Select(_selected);
                _doc.Views.Redraw();
            }
        }
    }
}
