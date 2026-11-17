namespace Chameleon.Core.Sessions
{
    /// <summary>
    /// A viewport camera at capture time. Rhino object masks only line up with a capture taken from
    /// the same camera, which <see cref="Signature"/> identifies.
    /// </summary>
    public sealed class CameraState
    {
        /// <summary>Camera position (x, y, z).</summary>
        public double[] Location { get; set; } = new double[3];

        /// <summary>Point the camera looks at (x, y, z).</summary>
        public double[] Target { get; set; } = new double[3];

        /// <summary>Camera up direction (x, y, z).</summary>
        public double[] Up { get; set; } = new double[3];

        /// <summary>Lens length in millimetres.</summary>
        public double LensLength { get; set; }

        /// <summary>True for a parallel projection.</summary>
        public bool Parallel { get; set; }

        /// <summary>Hash of the other fields plus the viewport aspect ratio.</summary>
        public string Signature { get; set; } = string.Empty;

        /// <summary>Copies this camera.</summary>
        public CameraState Clone()
        {
            return new CameraState
            {
                Location = (double[])Location.Clone(),
                Target = (double[])Target.Clone(),
                Up = (double[])Up.Clone(),
                LensLength = LensLength,
                Parallel = Parallel,
                Signature = Signature
            };
        }
    }
}
