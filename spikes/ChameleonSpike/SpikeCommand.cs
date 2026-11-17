using System;
using Rhino;
using Rhino.Commands;

namespace ChameleonSpike
{
    /// <summary>Opens the spike window and runs every check.</summary>
    public class SpikeCommand : Command
    {
        private static SpikeForm? _form;

        /// <inheritdoc />
        public override string EnglishName => "ChameleonSpike";

        /// <inheritdoc />
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            try
            {
                if (_form == null || _form.IsDisposed)
                {
                    _form = new SpikeForm();
                    _form.Closed += (s, e) => _form = null;
                }

                _form.Show();
                _form.BringToFront();
                return Result.Success;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("ChameleonSpike failed to start: " + ex);
                return Result.Failure;
            }
        }
    }
}
