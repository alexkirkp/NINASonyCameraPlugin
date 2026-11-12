using Xunit;

namespace NINA.RetroKiwi.Plugin.SonyCamera.Tests {
    /// <summary>
    /// Tests that drive the static CameraLink (reconnect flag and event) run in this one collection,
    /// so they never run in parallel with each other.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class CameraLinkCollection {
        public const string Name = "CameraLink";
    }
}
