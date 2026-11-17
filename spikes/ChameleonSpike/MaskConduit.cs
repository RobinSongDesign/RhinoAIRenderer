using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Display;
using Rhino.Geometry;

namespace ChameleonSpike
{
    /// <summary>
    /// Prototype for check G1: while enabled, every object is drawn flat black except the selected
    /// ones, which are drawn flat white, on a black backdrop. Depth testing stays on, so parts of a
    /// selected object hidden behind other geometry stay black.
    /// </summary>
    public sealed class MaskConduit : DisplayConduit
    {
        private readonly HashSet<Guid> _selected;
        private readonly DisplayMaterial _white = Flat(Color.White);
        private readonly DisplayMaterial _black = Flat(Color.Black);

        /// <summary>Creates a conduit for the given selected object ids.</summary>
        public MaskConduit(IEnumerable<Guid> selectedIds)
        {
            _selected = new HashSet<Guid>(selectedIds);
        }

        /// <inheritdoc />
        protected override void PreDrawObjects(DrawEventArgs e)
        {
            // Cover whatever background the display mode painted, without touching the depth buffer.
            Size size = e.Viewport.Size;
            e.Display.PushDepthTesting(false);
            e.Display.PushDepthWriting(false);
            e.Display.Draw2dRectangle(new Rectangle(0, 0, size.Width, size.Height), Color.Black, 0, Color.Black);
            e.Display.PopDepthWriting();
            e.Display.PopDepthTesting();
        }

        /// <inheritdoc />
        protected override void PreDrawObject(DrawObjectEventArgs e)
        {
            // Take over drawing completely: no wires, no materials, no lighting.
            e.DrawObject = false;

            Mesh[] meshes = e.RhinoObject.GetMeshes(MeshType.Render);
            if (meshes == null)
            {
                return;
            }

            DisplayMaterial material = _selected.Contains(e.RhinoObject.Id) ? _white : _black;
            foreach (Mesh mesh in meshes)
            {
                if (mesh != null)
                {
                    e.Display.DrawMeshShaded(mesh, material);
                }
            }
        }

        // Emission-only material: the color does not depend on scene lighting.
        private static DisplayMaterial Flat(Color color)
        {
            return new DisplayMaterial(Color.Black, Color.Black, Color.Black, color, 0.0, 0.0);
        }
    }
}
