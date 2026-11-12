using System;
using System.Collections.Generic;
using System.Linq;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using Sony;
using PluginSettings = NINA.RetroKiwi.Plugin.SonyCamera.Properties.Settings;

namespace NINA.RetroKiwi.Plugin.SonyCamera.Drivers {
    /// <summary>
    /// Puts the camera into the astro shooting setup on connect: PC mode control, M mode, single
    /// drive, manual focus, widest aperture and a save destination that reaches the PC.
    /// Only properties the camera lists are written; anything else is logged and skipped, and a
    /// refused write becomes a NINA warning rather than a failed connect.
    /// </summary>
    internal static class ShootingSetup {
        internal const uint PROP_PC_CONTROL_MODE = 0xD25A; // 0 = mode dial decides, 1 = PC decides
        internal const uint PROP_EXPOSURE_MODE = 0x500E;
        internal const uint PROP_DRIVE_MODE = 0x5013;
        internal const uint PROP_FOCUS_MODE = 0x500A;
        internal const uint PROP_APERTURE = 0x5007;
        internal const uint PROP_SAVE_DESTINATION = 0xD222;

        // Wi-Fi forms; over USB the a6700 reports 16-bit values, so the allowed list is checked first
        private const uint EXPOSURE_MODE_MANUAL = 0x1;
        private const uint DRIVE_MODE_SINGLE = 0x1;
        private const uint FOCUS_MODE_MANUAL = 0x1;
        private const uint SAVE_PC_ONLY = 0x01;
        private const uint SAVE_PC_AND_CARD = 0x11;

        internal static void Apply(SonyCameraInfo camera, ISonyDriver driver = null) {
            var s = PluginSettings.Default;
            driver = driver ?? SonyDriver.GetInstance();
            var problems = new List<string>();

            LogProperties(driver, camera);

            if (s.SetManualExposureMode) {
                // The dial has to hand control to the PC before the mode can be written
                if (Has(camera, PROP_PC_CONTROL_MODE)) {
                    Write(driver, camera, PROP_PC_CONTROL_MODE, 1, "PC control of the mode", problems);
                } else {
                    Logger.Info("Shooting setup: camera does not list PC mode control (0xD25A); skipping it");
                }

                WriteMatching(driver, camera, PROP_EXPOSURE_MODE, "Manual exposure mode", EXPOSURE_MODE_MANUAL, new[] { "manual", "m" }, problems);
            }

            if (s.SetSingleDrive) {
                WriteMatching(driver, camera, PROP_DRIVE_MODE, "Single-shot drive", DRIVE_MODE_SINGLE, new[] { "single", "single shot", "single shooting" }, problems);
            }

            if (s.SetManualFocus) {
                WriteMatching(driver, camera, PROP_FOCUS_MODE, "Manual focus", FOCUS_MODE_MANUAL, new[] { "manual", "manual focus", "mf" }, problems);
            }

            if (s.SetWidestAperture) {
                var info = camera.GetPropertyInfo(PROP_APERTURE);
                var options = info?.Options().ToList();

                if (options == null || options.Count == 0) {
                    Logger.Info("Shooting setup: camera reports no aperture values (manual lens?); skipping widest aperture");
                } else {
                    // f-number x 100, so the widest is the smallest value
                    var widest = options.OrderBy(o => o.Value).First();
                    Write(driver, camera, PROP_APERTURE, widest.Value, $"widest aperture ({widest.Name})", problems);
                }
            }

            // 0 = leave alone, 1 = PC only, 2 = PC + card. Card only is never offered.
            if (s.SaveDestination == 1 || s.SaveDestination == 2) {
                var wanted = s.SaveDestination == 1 ? SAVE_PC_ONLY : SAVE_PC_AND_CARD;
                var label = s.SaveDestination == 1 ? "save to PC only" : "save to PC + card";

                if (!Has(camera, PROP_SAVE_DESTINATION)) {
                    Logger.Info("Shooting setup: camera does not list the save destination (0xD222); skipping it");
                } else if (!Allowed(camera, PROP_SAVE_DESTINATION, wanted)) {
                    Logger.Warning($"Shooting setup: {label} is not in the camera's allowed save destinations; skipping it");
                    problems.Add(label);
                } else {
                    Write(driver, camera, PROP_SAVE_DESTINATION, wanted, label, problems);
                }
            }

            if (problems.Count > 0) {
                Notification.ShowWarning("Sony camera setup not fully applied: " + string.Join(", ", problems) +
                    ". See the NINA log.");
            }
        }

        /// <summary>
        /// Logs what the camera reports for each setup property (type, flags, current value and
        /// allowed values), so a write that has no effect can be diagnosed from the log.
        /// </summary>
        private static void LogProperties(ISonyDriver driver, SonyCameraInfo camera) {
            foreach (var id in new[] { PROP_PC_CONTROL_MODE, PROP_EXPOSURE_MODE, PROP_DRIVE_MODE, PROP_FOCUS_MODE, PROP_APERTURE, PROP_SAVE_DESTINATION }) {
                var info = camera.GetPropertyInfo(id);

                if (info == null) {
                    Logger.Info($"Shooting setup: 0x{id:X4} is not listed by the camera");
                    continue;
                }

                string current;
                try {
                    var v = driver.GetProperty(camera.Handle, id);
                    current = $"0x{v.Value:X} '{v.Text}'";
                } catch (Exception ex) {
                    current = "unreadable: " + ex.Message;
                }

                Logger.Info($"Shooting setup: 0x{id:X4} {info.Name} type=0x{info.DataType:X} flags=0x{info.Flags:X} current={current} allowed=[" +
                    string.Join(", ", info.Options().Select(o => $"0x{o.Value:X}={o.Name}")) + "]");
            }
        }

        private static bool Has(SonyCameraInfo camera, uint id) => camera.GetPropertyInfo(id) != null;

        private static bool Allowed(SonyCameraInfo camera, uint id, uint value) {
            var info = camera.GetPropertyInfo(id);

            // A property with no enumerated list accepts anything the camera does
            return info != null && (!info.IsEnum() || info.Options().Any(o => o.Value == value));
        }

        /// <summary>
        /// Writes the wanted value, or the allowed option whose name matches, since the a6700's USB
        /// value widths differ from the Wi-Fi ones.
        /// </summary>
        private static void WriteMatching(ISonyDriver driver, SonyCameraInfo camera, uint id, string label, uint wanted, string[] names, List<string> problems) {
            var info = camera.GetPropertyInfo(id);

            if (info == null) {
                Logger.Info($"Shooting setup: camera does not list 0x{id:X4}; skipping {label}");
                return;
            }

            var options = info.Options().ToList();
            uint value = wanted;

            if (options.Count > 0) {
                var match = options.FirstOrDefault(o => o.Value == wanted);

                if (match.Name == null && match.Value != wanted) {
                    match = options.FirstOrDefault(o => names.Contains((o.Name ?? "").Trim().ToLowerInvariant()));
                    if (match.Name == null) {
                        Logger.Warning($"Shooting setup: {label} is not in the camera's allowed values for 0x{id:X4}: " +
                            string.Join(", ", options.Select(o => $"0x{o.Value:X}={o.Name}")));
                        problems.Add(label);
                        return;
                    }
                }

                value = match.Value;
            }

            Write(driver, camera, id, value, label, problems);
        }

        private static void Write(ISonyDriver driver, SonyCameraInfo camera, uint id, uint value, string label, List<string> problems) {
            try {
                // Some properties (aperture) only become writable a moment after the mode change
                // lands, so a refused write is retried briefly before giving up
                for (int attempt = 1; ; attempt++) {
                    try {
                        driver.SetProperty(camera.Handle, id, value);
                        break;
                    } catch (PropertyWriteRefusedException) when (attempt < 5) {
                        System.Threading.Thread.Sleep(400);
                    }
                }

                // The DLL refuses writes the camera ignores, but read back anyway as a cheap check
                var actual = driver.GetProperty(camera.Handle, id);

                if (actual.Value != value) {
                    Logger.Warning($"Shooting setup: {label} was not applied (0x{id:X4} wanted 0x{value:X}, camera still reports 0x{actual.Value:X} '{actual.Text}')");
                    problems.Add(label);
                    return;
                }

                Logger.Info($"Shooting setup: set {label} (0x{id:X4} = 0x{value:X})");
            } catch (PropertyWriteRefusedException) {
                Logger.Warning($"Shooting setup: the camera refused {label} (0x{id:X4} = 0x{value:X}); the mode dial or lens controls it");
                problems.Add(label + " (the mode dial controls this)");
            } catch (Exception ex) {
                Logger.Warning($"Shooting setup: could not set {label} (0x{id:X4} = 0x{value:X}): {ex.Message}");
                problems.Add(label);
            }
        }
    }
}
