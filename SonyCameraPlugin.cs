using NINA.RetroKiwi.Plugin.SonyCamera.Properties;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Image.ImageData;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Settings = NINA.RetroKiwi.Plugin.SonyCamera.Properties.Settings;

namespace NINA.RetroKiwi.Plugin.SonyCamera {
    /// <summary>
    /// This class exports the IPluginManifest interface and will be used for the general plugin information and options
    /// The base class "PluginBase" will populate all the necessary Manifest Meta Data out of the AssemblyInfo attributes. Please fill these accordingly
    /// 
    /// An instance of this class will be created and set as datacontext on the plugin options tab in N.I.N.A. to be able to configure global plugin settings
    /// The user interface for the settings will be defined by a DataTemplate with the key having the naming convention "SonyCameraPlugin_Options" where SonyCameraPlugin corresponds to the AssemblyTitle - In this template example it is found in the Options.xaml
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class SonyCamera : PluginBase, INotifyPropertyChanged {
        private readonly IPluginOptionsAccessor pluginSettings;
        private readonly IProfileService profileService;
        private readonly IImageSaveMediator imageSaveMediator;

        // Implementing a file pattern
//        private readonly ImagePattern exampleImagePattern = new ImagePattern("$$EXAMPLEPATTERN$$", "An example of an image pattern implementation", "Sony Camera Plugin");

        [ImportingConstructor]
        public SonyCamera(IProfileService profileService, IOptionsVM options, IImageSaveMediator imageSaveMediator) {
            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                CoreUtil.SaveSettings(Settings.Default);
            }

            // This helper class can be used to store plugin settings that are dependent on the current profile
            this.pluginSettings = new PluginOptionsAccessor(profileService, Guid.Parse(this.Identifier));
            this.profileService = profileService;
            // React on a changed profile
            profileService.ProfileChanged += ProfileService_ProfileChanged;

            // Hook into image saving for adding FITS keywords or image file patterns
            this.imageSaveMediator = imageSaveMediator;

            // Run these handlers when an image is being saved
            this.imageSaveMediator.BeforeImageSaved += ImageSaveMediator_BeforeImageSaved;
            this.imageSaveMediator.BeforeFinalizeImageSaved += ImageSaveMediator_BeforeFinalizeImageSaved;

            // Register a new image file pattern for the Options > Imaging > File Patterns area
//            options.AddImagePattern(exampleImagePattern);
        }

        public override Task Teardown() {
            // Make sure to unregister an event when the object is no longer in use. Otherwise garbage collection will be prevented.
            profileService.ProfileChanged -= ProfileService_ProfileChanged;
            imageSaveMediator.BeforeImageSaved -= ImageSaveMediator_BeforeImageSaved;
            imageSaveMediator.BeforeFinalizeImageSaved -= ImageSaveMediator_BeforeFinalizeImageSaved;

            return base.Teardown();
        }

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            // Rase the event that this profile specific value has been changed due to the profile switch
            RaisePropertyChanged(nameof(ProfileSpecificNotificationMessage));
        }

        private Task ImageSaveMediator_BeforeImageSaved(object sender, BeforeImageSavedEventArgs e) {
            // Insert the example FITS keyword of a specific data type into the image metadata object prior to the file being saved
            // FITS keywords have a maximum of 8 characters. Comments are options. Comments that are too long will be truncated.
/*
            string exampleKeywordComment = "This is a {0} keyword";

            // string
            string exampleStringKeywordName = "STRKEYWD";
            string exampleStringKeywordValue = "Example";
            e.Image.MetaData.GenericHeaders.Add(new StringMetaDataHeader(exampleStringKeywordName, exampleStringKeywordValue, string.Format(exampleKeywordComment, "string")));

            // integer
            string exampleIntKeywordName = "INTKEYWD";
            int exampleIntKeywordValue = 5;
            e.Image.MetaData.GenericHeaders.Add(new IntMetaDataHeader(exampleIntKeywordName, exampleIntKeywordValue, string.Format(exampleKeywordComment, "integer")));

            // double
            string exampleDoubleKeywordName = "DBLKEYWD";
            double exampleDoubleKeywordValue = 1.3d;
            e.Image.MetaData.GenericHeaders.Add(new DoubleMetaDataHeader(exampleDoubleKeywordName, exampleDoubleKeywordValue, string.Format(exampleKeywordComment, "double")));

            // Classes also exist for other data types:
            // BoolMetaDataHeader()
            // DateTimeMetaDataHeader()
*/
            return Task.CompletedTask;
        }

        private Task ImageSaveMediator_BeforeFinalizeImageSaved(object sender, BeforeFinalizeImageSavedEventArgs e) {
/*            // Populate the example image pattern with data. This can provide data that may not be immediately available
            e.AddImagePattern(new ImagePattern(exampleImagePattern.Key, exampleImagePattern.Description, exampleImagePattern.Category) {
                Value = $"{DateTime.Now:yyyy-MM-ddTHH:mm:ss.ffffffK}"
            });
*/
            return Task.CompletedTask;
        }

        public string DefaultNotificationMessage {
            get {
                return Settings.Default.DefaultNotificationMessage;
            }
            set {
                Settings.Default.DefaultNotificationMessage = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
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

        public string ProfileSpecificNotificationMessage {
            get {
                return pluginSettings.GetValueString(nameof(ProfileSpecificNotificationMessage), string.Empty);
            }
            set {
                pluginSettings.SetValueString(nameof(ProfileSpecificNotificationMessage), value);
                RaisePropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
