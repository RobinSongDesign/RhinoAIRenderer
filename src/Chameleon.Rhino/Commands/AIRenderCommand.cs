using System.Runtime.InteropServices;
using Rhino;
using Rhino.Commands;

namespace Chameleon.Rhino.Commands
{
    /// <summary>The WPF version's command name, kept so existing habits, aliases and toolbar buttons still work.</summary>
    [Guid("9e1d3c55-7a2b-4f0e-8c3d-2b6a41f5e7d2")]
    public class AIRenderCommand : Command
    {
        /// <inheritdoc />
        public override string EnglishName => "AIRender";

        /// <inheritdoc />
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            return ChameleonCommand.OpenWindow();
        }
    }
}
