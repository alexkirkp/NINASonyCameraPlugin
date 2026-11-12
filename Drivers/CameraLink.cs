using System;

namespace NINA.RetroKiwi.Plugin.SonyCamera.Drivers {
    /// <summary>
    /// Shared view of the camera link for the other devices that ride on the camera handle (the lens
    /// focuser). The camera driver sets it while it is reconnecting after an unplug and raises
    /// Reconnected once the new handle is open and set up.
    /// </summary>
    internal static class CameraLink {
        private static volatile bool _reconnecting;

        /// <summary>True while the camera handle is closed but the reconnect window is still open.</summary>
        public static bool Reconnecting {
            get => _reconnecting;
            set => _reconnecting = value;
        }

        /// <summary>Raised with the reopened camera handle once the camera is back.</summary>
        public static event Action<uint> Reconnected;

        public static void RaiseReconnected(uint handle) {
            Reconnected?.Invoke(handle);
        }
    }
}
