using System;
using System.Runtime.InteropServices;
using Chameleon.Rhino.UI;
using Rhino;
using Rhino.Commands;

namespace Chameleon.Rhino.Commands
{
    /// <summary>Opens the Chameleon window.</summary>
    [Guid("5b2a7a0e-2f4f-4c55-9c0c-6a8f2d0f6c11")]
    public class ChameleonCommand : Command
    {
        /// <inheritdoc />
        public override string EnglishName => "Chameleon";

        /// <inheritdoc />
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            return OpenWindow();
        }

        /// <summary>Shared by every command that opens the window.</summary>
        internal static Result OpenWindow()
        {
            try
            {
                MainWindowController.Show();
                return Result.Success;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("Chameleon could not start: " + ex.Message);
                ChameleonPlugin.Instance?.Host?.Log("Opening the window failed: " + ex);
                return Result.Failure;
            }
        }
    }
}
