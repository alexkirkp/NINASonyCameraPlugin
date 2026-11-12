using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using Settings = NINA.RetroKiwi.Plugin.SonyCamera.Properties.Settings;

namespace NINA.RetroKiwi.Plugin.SonyCamera {
    /// <summary>
    /// Exports the plugin manifest (the plugin's name, version etc. come from the AssemblyInfo attributes)
    /// and holds the plugin-wide options. An instance of this class is the data context of the options
    /// page defined in Options.xaml.
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class SonyCamera : PluginBase, INotifyPropertyChanged {
        [ImportingConstructor]
        public SonyCamera() {
            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                CoreUtil.SaveSettings(Settings.Default);
            }
        }
        /// <summary>
        /// How long (seconds) to keep trying to reopen a camera that was unplugged before reporting it
        /// as disconnected. 0 turns automatic reconnect off.
        /// </summary>
        public int ReconnectGraceSeconds {
            get {
                return Settings.Default.ReconnectGraceSeconds;
            }
            set {
                Settings.Default.ReconnectGraceSeconds = Math.Max(0, value);
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        /// <summary>On connect, take PC control of the mode and select Manual (M) exposure mode, whatever the dial is set to. Not undone on disconnect: the camera stays in the mode the plugin set until the dial is moved.</summary>
        public bool SetManualExposureMode {
            get {
                return Settings.Default.SetManualExposureMode;
            }
            set {
                Settings.Default.SetManualExposureMode = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        /// <summary>On connect, select single-shot drive (BULB is only offered in single drive).</summary>
        public bool SetSingleDrive {
            get {
                return Settings.Default.SetSingleDrive;
            }
            set {
                Settings.Default.SetSingleDrive = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        /// <summary>On connect, select manual focus (needed for NINA's focuser).</summary>
        public bool SetManualFocus {
            get {
                return Settings.Default.SetManualFocus;
            }
            set {
                Settings.Default.SetManualFocus = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        /// <summary>On connect, select the widest aperture the attached lens reports.</summary>
        public bool SetWidestAperture {
            get {
                return Settings.Default.SetWidestAperture;
            }
            set {
                Settings.Default.SetWidestAperture = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        /// <summary>Snapshot exposures (autofocus, plate solving, alignment) use the electronic shutter; everything else uses the mechanical one.</summary>
        public bool ElectronicShutterForSnapshots {
            get {
                return Settings.Default.ElectronicShutterForSnapshots;
            }
            set {
                Settings.Default.ElectronicShutterForSnapshots = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        /// <summary>On connect, where images are saved: 0 = leave as is, 1 = PC only, 2 = PC + card. Card only is never offered, as no image would reach NINA.</summary>
        public int SaveDestination {
            get {
                return Settings.Default.SaveDestination;
            }
            set {
                Settings.Default.SaveDestination = Math.Max(0, Math.Min(2, value));
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
