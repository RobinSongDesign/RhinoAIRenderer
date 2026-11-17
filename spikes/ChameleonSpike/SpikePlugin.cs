using System.Runtime.InteropServices;

// Deliberately different from the product plug-in id so both can be loaded side by side.
[assembly: Guid("7c3f0e52-4f1a-4d0e-9b7c-2f6c1a5d9e11")]

namespace ChameleonSpike
{
    /// <summary>Plug-in entry point of the phase 0 spike.</summary>
    public class SpikePlugin : Rhino.PlugIns.PlugIn
    {
    }
}
