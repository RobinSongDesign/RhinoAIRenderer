using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace ChameleonSpike
{
    /// <summary>
    /// Prototype for check G1: while enabled, every object is drawn flat black except the given
    /// ones, which are drawn flat white, on a black backdrop. Depth testing stays on, so parts of a
    /// chosen object hidden behind other geometry stay black.
    /// </summary>
    /// <remarks>
    /// Geometry is drawn from the objects themselves (breps, extrusions, meshes, SubDs, block
    /// instances) rather than from cached render meshes, which do not exist in wireframe-style
    /// display modes. Curves, points and annotations cover no area and are not drawn.
    /// </remarks>
    public sealed class MaskConduit : DisplayConduit
    {
        private readonly HashSet<Guid> _selected;
        private readonly DisplayMaterial _white = Flat(Color.White);
        private readonly DisplayMaterial _black = Flat(Color.Black);

        // Conversions are made once per object and reused by every draw pass.
        private readonly Dictionary<Guid, Brep?> _breps = new Dictionary<Guid, Brep?>();
        private readonly Dictionary<Guid, Mesh?> _subdMeshes = new Dictionary<Guid, Mesh?>();

        /// <summary>Creates a conduit for the given object ids.</summary>
        public MaskConduit(IEnumerable<Guid> selectedIds)
        {
            _selected = new HashSet<Guid>(selectedIds);
        }

        /// <summary>Objects drawn white in the last pass, for the report.</summary>
        public int WhiteObjectsDrawn { get; private set; }

        /// <summary>Objects drawn black in the last pass, for the report.</summary>
        public int BlackObjectsDrawn { get; private set; }

        /// <inheritdoc />
        protected override void PreDrawObjects(DrawEventArgs e)
        {
            WhiteObjectsDrawn = 0;
            BlackObjectsDrawn = 0;

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

            bool white = _selected.Contains(e.RhinoObject.Id);
            if (DrawObject(e.Display, e.RhinoObject, white ? _white : _black, 0))
            {
                if (white)
                {
                    WhiteObjectsDrawn++;
                }
                else
                {
                    BlackObjectsDrawn++;
                }
            }
        }

        /// <returns>True when something with an area was drawn.</returns>
        private bool DrawObject(DisplayPipeline display, RhinoObject obj, DisplayMaterial material, int depth)
        {
            switch (obj)
            {
                case InstanceObject instance when depth < 8:
                    // Blocks: draw the definition's objects with the instance transform.
                    bool drawn = false;
                    display.PushModelTransform(instance.InstanceXform);
                    try
                    {
                        foreach (RhinoObject part in instance.InstanceDefinition.GetObjects())
                        {
                            drawn |= DrawObject(display, part, material, depth + 1);
                        }
                    }
                    finally
                    {
                        display.PopModelTransform();
                    }

                    return drawn;

                case BrepObject _:
                case ExtrusionObject _:
                    Brep? brep = BrepOf(obj);
                    if (brep == null)
                    {
                        return false;
                    }

                    display.DrawBrepShaded(brep, material);
                    return true;

                case MeshObject meshObject:
                    display.DrawMeshShaded(meshObject.MeshGeometry, material);
                    return true;

                case SubDObject _:
                    Mesh? mesh = SubDMeshOf(obj);
                    if (mesh == null)
                    {
                        return false;
                    }

                    display.DrawMeshShaded(mesh, material);
                    return true;

                default:
                    return false;
            }
        }

        private Brep? BrepOf(RhinoObject obj)
        {
            if (!_breps.TryGetValue(obj.Id, out Brep? brep))
            {
                brep = obj.Geometry is Extrusion extrusion ? extrusion.ToBrep(false) : obj.Geometry as Brep;
                _breps[obj.Id] = brep;
            }

            return brep;
        }

        private Mesh? SubDMeshOf(RhinoObject obj)
        {
            if (!_subdMeshes.TryGetValue(obj.Id, out Mesh? mesh))
            {
                mesh = obj.Geometry is SubD subd ? Mesh.CreateFromSubD(subd, 2) : null;
                _subdMeshes[obj.Id] = mesh;
            }

            return mesh;
        }

        // Emission-only material: the color does not depend on scene lighting.
        private static DisplayMaterial Flat(Color color)
        {
            return new DisplayMaterial(Color.Black, Color.Black, Color.Black, color, 0.0, 0.0);
        }
    }
}
