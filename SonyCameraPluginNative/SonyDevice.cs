using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sony {
    public class SonyDevice {
        private PortableDeviceInfo _deviceInfo;
        public SonyDevice(PortableDeviceInfo info) {
            _deviceInfo = info;
        }

        public string Id {
            get {
                return _deviceInfo.id;
            }
        }

        /// <summary>
        /// An Id for NINA to tell cameras apart and to save in the profile. Built from the model plus
        /// the USB instance part of the device id (which carries the body's serial number when the
        /// camera exposes one), so it does not depend on the full WPD path.
        /// </summary>
        public string UniqueId {
            get {
                return BuildUniqueId(_deviceInfo.model, _deviceInfo.id);
            }
        }

        public static string BuildUniqueId(string model, string deviceId) {
            string instance = deviceId ?? "";

            // USB style: \\?\usb#vid_054c&pid_0c03#<instance>#{guid}
            var parts = instance.Split('#');
            if (parts.Length >= 3 && parts[0].EndsWith("usb", StringComparison.OrdinalIgnoreCase)) {
                instance = parts[1] + "#" + parts[2];
            }

            return $"Sony_{model}_{instance}".ToLowerInvariant();
        }

        public string Manufacturer {
            get {
                return _deviceInfo.manufacturer;
            }
        }

        public string Model {
            get {
                return _deviceInfo.model;
            }
        }

        public string Path {
            get {
                return _deviceInfo.devicePath;
            }
        }
    }
}
