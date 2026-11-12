using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using FTD2XX_NET;
using NINA.Core.Enum;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Equipment.SDK.CameraSDKs.ASTPANSDK;
using NINA.Equipment.Utility;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using NINA.Profile;
using NINA.Profile.Interfaces;
using Sony;
using PluginSettings = NINA.RetroKiwi.Plugin.SonyCamera.Properties.Settings;

namespace NINA.RetroKiwi.Plugin.SonyCamera.Drivers {
    public class CameraDriver : BaseINPC, ICamera {
        // Some camera settings we are interested in
        private const uint PROPID_BATTERY = 53784;
        private const uint PROPID_ISO = 0xD21E; // Actual ISO currently set
        private const uint PROPID_ISOS = 0xFFFE; // Registry-backed list of learnt ISOs (may be empty until learnt)

        // Capture Status
        private const uint CAPTURE_CREATED    = 0x0000;
        private const uint CAPTURE_CAPTURING  = 0x0001;
        private const uint CAPTURE_FAILED     = 0x0002;
        private const uint CAPTURE_CANCELLED  = 0x0003;
        private const uint CAPTURE_COMPLETE   = 0x0004;
        private const uint CAPTURE_STARTING   = 0x8001;
        private const uint CAPTURE_READING    = 0x8002;
        private const uint CAPTURE_PROCESSING = 0x8003;

        private SonyCameraInfo _camera = null;
        private SonyDevice _device = null;
        private IProfileService _profileService;
        private readonly IExposureDataFactory _exposureDataFactory;
        private bool _liveViewEnabled;
        private short _readoutModeForSnapImages;
        private short _readoutModeForNormalImages;
        private AsyncObservableCollection<BinningMode> _binningModes;

        public CameraDriver(IProfileService profileService, IExposureDataFactory exposureDataFactory, SonyDevice device) {
            _profileService = profileService;
            _exposureDataFactory = exposureDataFactory;
            _device = device;
        }

        #region Internal Helpers

        private PropertyValue GetPropertyValue(uint id) {
            ThrowIfNotUsable();
            return SonyDriver.GetInstance().GetProperty(_camera.Handle, id);
        }

        private IReadOnlyList<PropertyValueOption> GetAvailableIsoOptions() {
            if (_camera == null) {
                return Array.Empty<PropertyValueOption>();
            }

            uint[] propertyCandidates = { PROPID_ISOS, PROPID_ISO };

            foreach (var propertyId in propertyCandidates) {
                try {
                    var options = _camera.GetPropertyInfo(propertyId)?.Options()?.Where(o => o.Value <= 0x00FFFFFF).ToList();
                    if (options != null && options.Count > 0) {
                        return options;
                    }
                } catch (Exception ex) {
                    Logger.Warning($"Unable to enumerate ISO options for property 0x{propertyId:X}: {ex.Message}");
                }
            }

            Logger.Warning("Camera did not report any ISO options via known properties (registry ISO list may be empty until the camera learns it).");
            return Array.Empty<PropertyValueOption>();
        }

        private void NotifyGainPropertiesChanged() {
            RaisePropertyChanged(nameof(CanGetGain));
            RaisePropertyChanged(nameof(CanSetGain));
            RaisePropertyChanged(nameof(GainMin));
            RaisePropertyChanged(nameof(GainMax));
            RaisePropertyChanged(nameof(Gain));
            RaisePropertyChanged(nameof(Gains));
        }

        #endregion

        #region Supported Properties

        public bool HasShutter => true;

        // Although the driver supports camera temperature, it gets it from the ARW's
        // metadata after a photo is taken, because this code doesn't request processed
        // ARW, the temp cannot be determined.
        public double Temperature {
            get => double.NaN;
            /*{

                if (_camera != null) {
                    PropertyValue value = GetPropertyValue(PROPID_TEMPERATURE);

                    return (value.Value) / 10.0;
                } else {
                    return double.NaN;
                }
            }*/
        }

        public short BinX { get => 1; set => throw new NotImplementedException(); }
        public short BinY { get => 1; set => throw new NotImplementedException(); }

        public string SensorName {
            get {
                if (_camera != null) {
                    return _camera.SensorName;
                } else {
                    return string.Empty;
                }
            }
        }

        public SensorType SensorType { get => SensorType.RGGB; set => throw new NotImplementedException(); }

        public short BayerOffsetX { get => 1; set => throw new NotImplementedException(); }

        public short BayerOffsetY { get => 1; set => throw new NotImplementedException(); }

        public int CameraXSize {
            get {
                if (_camera != null) {
                    return _camera.ImageSize.Width;
                }
                else {
                    return 0;
                }
            }
        }

        public int CameraYSize {
            get {
                if (_camera != null) {
                    return _camera.ImageSize.Height;
                } else {
                    return 0;
                }
            }
        }

        public double ExposureMin {
            get {
                if (_camera != null) {
                    return _camera.ExposureMin;
                } else {
                    return double.NaN;
                }
            }
        }

        public double ExposureMax {
            get {
                if (_camera != null) {
                    return _camera.ExposureMax;
                } else {
                    return double.NaN;
                }
            }
        }

        public short MaxBinX { get => 1; set => throw new NotImplementedException(); }

        public short MaxBinY { get => 1; set => throw new NotImplementedException(); }

        public double PixelSizeX {
            get {
                if (_camera != null) {
                    return _camera.PixelWidth;
                } else {
                    return double.NaN;
                }
            }
        }

        public double PixelSizeY {
            get {
                if (_camera != null) {
                    return _camera.PixelHeight;
                } else {
                    return double.NaN;
                }
            }
        }

        public bool CanSetTemperature => false;

        public CameraStates CameraState => CameraStates.NoState; // TODO

        public bool CanShowLiveView {
            get {
                if (_camera != null) {
                    return _camera.SupportsPreview();
                } else {
                    return false;
                }
            }
        }

        public bool LiveViewEnabled {
            get => _liveViewEnabled;
            set {
                _liveViewEnabled = value;
                RaisePropertyChanged();
            }
        }

        public bool HasBattery => true;

        public int BatteryLevel {
            get {
                if (_camera != null) {
                    try {
                        return (int)GetPropertyValue(PROPID_BATTERY).Value;
                    } catch (CameraUnpluggedException) {
                        return 0; // unplugged or reconnecting
                    }
                } else {
                    return 0;
                }
            }
        }

        public int BitDepth {
            get {
                if (_camera != null) {
                    return _camera.BitsPerPixel;
                } else {
                    return 0;
                }
            }
        }

        public bool CanGetGain => GetAvailableIsoOptions().Any();

        public bool CanSetGain => CanGetGain;

        public int GainMax {
            get {
                var isoOptions = GetAvailableIsoOptions();
                if (!isoOptions.Any()) {
                    if (_camera != null) {
                        Logger.Error("Problem getting gain max: camera did not report ISO options.");
                    }
                    return -1;
                }

                return (int)isoOptions.Last().Value;
            }
        }

        public int GainMin {
            get {
                var isoOptions = GetAvailableIsoOptions();
                if (!isoOptions.Any()) {
                    if (_camera != null) {
                        Logger.Error("Problem getting gain min: camera did not report ISO options.");
                    }
                    return -1;
                }

                return (int)isoOptions.Min(o => o.Value);
            }
        }

        public int Gain {
            get {
                if (_camera != null) {
                    try {
                        PropertyValue value = GetPropertyValue(PROPID_ISO);

                        return (int)(value.Value == 0xffffff ? 0 : value.Value);
                    } catch (CameraUnpluggedException) {
                        return -1; // expected while unplugged or reconnecting; NINA polls this
                    } catch (Exception ex) {
                        Logger.Error("Problem getting gain", ex);
                        return -1;
                    }
                } else {
                    return -1;
                }
            }

            set {
                if (_camera != null) {
                    try {
                        if (GetLinkState() == LinkState.Reconnecting) {
                            // Applied once the camera is back, so a change made during a drop isn't lost
                            _pendingGain = value;
                            Logger.Info($"Camera is reconnecting; gain {value} will be set when it is back");
                            return;
                        }

                        SonyDriver.GetInstance().SetProperty(_camera.Handle, PROPID_ISO, (uint)value);
                        RaisePropertyChanged(nameof(Gain));
                    } catch (CameraUnpluggedException) {
                        // Window ran out; nothing to set on a camera that is gone
                    } catch (Exception ex) {
                        Logger.Error($"Problem setting gain to {value}", ex);
                    }
                }
            }
        }

        public IList<int> Gains {
            get {
                List<int> gains = new List<int>();

                foreach (var iso in GetAvailableIsoOptions()) {
                    if (iso.Value == 0xffffff) {
                        gains.Add(0); // AUTO
                    } else {
                        gains.Add((int)iso.Value);
                    }
                }

                return gains;
            }
        }

        public string Id => "Sony";

        public string Name {
            get => _device.Model;
            set => throw new NotImplementedException();
        }

        public string DisplayName {
            get => _device.Model;
            set => throw new NotImplementedException();
        }

        public string Category { get => "Sony"; }

        public bool Connected {
            get {
                // Stays true while a reconnect is being attempted, so NINA never sees a brief drop
                return _camera != null && GetLinkState() != LinkState.Lost;
            }
        }

        #region Unplug / reconnect

        private enum LinkState { Ok, Reconnecting, Lost }

        private readonly object _linkLock = new object();
        private DateTime? _dropStartedUtc;          // set while a reconnect is in progress
        private bool _gaveUp;                       // the grace window ran out; camera reported disconnected
        private bool _deadHandleClosed;             // the loop has already closed the removed camera's handle
        private CancellationTokenSource _reconnectCts;
        private TaskCompletionSource<bool> _reconnected;
        private int? _pendingGain;                  // gain NINA set during the window
        private CaptureSequence _pendingExposure;  // started during the window; begun once reconnected

        private static readonly TimeSpan ReconnectRetryInterval = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Combines the DLL's removed flag with the reconnect state. The first time a removal is seen
        /// this starts the reconnect (or, if the grace window is 0, gives up straight away).
        /// </summary>
        private LinkState GetLinkState() {
            if (_camera == null) {
                return LinkState.Ok;
            }

            lock (_linkLock) {
                if (_gaveUp) {
                    return LinkState.Lost;
                }

                if (_dropStartedUtc != null) {
                    return LinkState.Reconnecting;
                }

                if (!IsCameraRemoved()) {
                    return LinkState.Ok;
                }

                var window = TimeSpan.FromSeconds(Math.Max(0, PluginSettings.Default.ReconnectGraceSeconds));

                if (window == TimeSpan.Zero) {
                    _gaveUp = true;
                    return LinkState.Lost;
                }

                _dropStartedUtc = DateTime.UtcNow;
                CameraLink.Reconnecting = true;
                _reconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _reconnectCts = new CancellationTokenSource();

                var oldCamera = _camera;
                var cts = _reconnectCts;
                var started = _dropStartedUtc.Value;

                Logger.Warning($"Sony camera unplugged; trying to reconnect for up to {window.TotalSeconds:0} s");
                Task.Run(() => ReconnectLoop(oldCamera, started, window, cts.Token));

                return LinkState.Reconnecting;
            }
        }

        private void ThrowIfNotUsable() {
            var state = GetLinkState();

            if (state != LinkState.Ok) {
                throw new CameraUnpluggedException(state == LinkState.Reconnecting);
            }
        }

        private async Task ReconnectLoop(SonyCameraInfo oldCamera, DateTime started, TimeSpan window, CancellationToken token) {
            var driver = SonyDriver.GetInstance();

            try {
                driver.CloseCamera(oldCamera.Handle);
            } catch (Exception ex) {
                Logger.Warning($"Closing the removed camera handle failed (ignored): {ex.Message}");
            }

            lock (_linkLock) {
                _deadHandleClosed = true;
            }

            while (!token.IsCancellationRequested && DateTime.UtcNow - started < window) {
                try {
                    var device = FindDevice(driver);

                    if (device != null) {
                        var reopened = driver.OpenCamera(device.Id);

                        lock (_linkLock) {
                            if (token.IsCancellationRequested) {
                                // Disconnect() ran while we were opening; don't resurrect the connection
                                try { driver.CloseCamera(reopened.Handle); } catch (Exception) { }
                                return;
                            }

                            Logger.Info(device.Id == _device.Id
                                ? "Reconnect: camera has the same device id as before"
                                : $"Reconnect: camera device id changed from {_device.Id} to {device.Id}");

                            _device = device;
                            _camera = reopened;
                            _deadHandleClosed = false;
                            // _dropStartedUtc stays set: the link reports Reconnecting (so exposures
                            // and property calls are deferred) until the setup below has finished.
                        }

                        try {
                            ShootingSetup.Apply(reopened); // connect-time settings are lost with the old handle
                        } catch (Exception ex) {
                            Logger.Error(ex);
                        }

                        int? gain;
                        lock (_linkLock) {
                            gain = _pendingGain;
                            _pendingGain = null;
                        }

                        if (gain != null) {
                            try {
                                driver.SetProperty(reopened.Handle, PROPID_ISO, (uint)gain.Value);
                            } catch (Exception ex) {
                                Logger.Error($"Problem setting gain to {gain} after reconnect", ex);
                            }
                        }

                        lock (_linkLock) {
                            if (token.IsCancellationRequested) {
                                return; // Disconnect() ran during the setup
                            }

                            _dropStartedUtc = null; // only now is the link usable again
                            _removalLogged = false;
                        }

                        var seconds = (DateTime.UtcNow - started).TotalSeconds;
                        Logger.Warning($"Sony camera reconnected after {seconds:0} s");
                        Notification.ShowWarning($"Sony camera was unplugged and reconnected after {seconds:0} s");
                        NotifyGainPropertiesChanged();
                        CameraLink.Reconnecting = false;

                        try {
                            CameraLink.RaiseReconnected(reopened.Handle); // re-attach the lens focuser
                        } catch (Exception ex) {
                            Logger.Error(ex);
                        }

                        _reconnected.TrySetResult(true);
                        return;
                    }
                } catch (Exception ex) {
                    Logger.Debug($"Reconnect attempt failed: {ex.Message}");
                }

                try {
                    await Task.Delay(ReconnectRetryInterval, token);
                } catch (OperationCanceledException) {
                    return;
                }
            }

            if (token.IsCancellationRequested) {
                return;
            }

            lock (_linkLock) {
                _gaveUp = true;
                CameraLink.Reconnecting = false;
            }

            Logger.Error($"Sony camera did not come back within {window.TotalSeconds:0} s; reporting it as disconnected");
            Notification.ShowError("Sony camera was unplugged and did not come back. Reconnect it and press Connect.");
            _reconnected.TrySetResult(false);
        }

        /// <summary>
        /// Finds the camera again after a replug: the same device id if it is still there, otherwise
        /// the one device of the same make and model.
        /// </summary>
        private SonyDevice FindDevice(SonyDriver driver) {
            var devices = driver.Cameras().ToList();

            return devices.FirstOrDefault(d => d.Id == _device.Id)
                ?? devices.FirstOrDefault(d => d.Manufacturer == _device.Manufacturer && d.Model == _device.Model);
        }

        private void ResetLinkState() {
            CancellationTokenSource cts;

            lock (_linkLock) {
                cts = _reconnectCts;
                _reconnectCts = null;
                _dropStartedUtc = null;
                CameraLink.Reconnecting = false;
                _gaveUp = false;
                _removalLogged = false;
                _pendingExposure = null;
                _pendingGain = null;
            }

            cts?.Cancel();
            _reconnected?.TrySetResult(false);
        }

        /// <summary>
        /// Waits for an in-progress reconnect. Returns normally once the camera is back; throws
        /// CameraUnpluggedException if the window runs out, OperationCanceledException if cancelled.
        /// </summary>
        private async Task WaitForReconnect(CancellationToken token) {
            var tcs = _reconnected;

            if (tcs == null) {
                return;
            }

            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using (token.Register(() => cancelled.TrySetCanceled())) {
                var finished = await Task.WhenAny(tcs.Task, cancelled.Task);

                token.ThrowIfCancellationRequested();

                if (!await finished) {
                    throw new CameraUnpluggedException(false);
                }
            }
        }

        #endregion

        /// <summary>
        /// The single place the plugin asks whether the camera has been unplugged.
        /// </summary>
        private bool IsCameraRemoved() {
            var camera = _camera;

            if (camera == null) {
                return false;
            }

            try {
                bool removed = SonyDriver.GetInstance().IsCameraRemoved(camera.Handle);

                if (removed && !_removalLogged) {
                    _removalLogged = true;
                    Logger.Warning("The Sony camera has been unplugged");
                }

                return removed;
            } catch (Exception ex) {
                Logger.Error(ex);
                return false;
            }
        }

        private bool _removalLogged = false;

        public string Description {
            get {
                if (_camera != null) {
                    return _camera.GetDescription();
                } else {
                    return _device.GetDescription();
                }
            }
        }

        public string DriverInfo => "https://retro.kiwi";

        public string DriverVersion => string.Empty;

        public double TemperatureSetPoint {
            get => double.NaN;

            set {
            }
        }

        public bool CanSubSample => false;

        public bool EnableSubSample { get; set; }

        public int SubSampleX { get; set; }

        public int SubSampleY { get; set; }

        public int SubSampleWidth { get; set; }

        public int SubSampleHeight { get; set; }

        public bool CoolerOn {
            get => false;
            set {
            }
        }

        public double CoolerPower => double.NaN;

        public bool HasDewHeater => false;

        public bool DewHeaterOn {
            get => false;
            set {
            }
        }

        public bool CanSetOffset => false;

        public int Offset { get => -1; set => throw new NotImplementedException(); }

        public int OffsetMin => 0;

        public int OffsetMax => 0;

        public bool CanSetUSBLimit => false;

        public int USBLimit { get => -1; set => throw new NotImplementedException(); }

        public int USBLimitMin => -1;

        public int USBLimitMax => -1;

        public int USBLimitStep => -1;

        public double ElectronsPerADU => double.NaN;

        public IList<string> ReadoutModes => new List<string> { "Default" };

        public short ReadoutMode {
            get => 0;
            set { }
        }

        public short ReadoutModeForSnapImages {
            get => _readoutModeForSnapImages;
            set {
                _readoutModeForSnapImages = value;
                RaisePropertyChanged();
            }
        }

        public short ReadoutModeForNormalImages {
            get => _readoutModeForNormalImages;
            set {
                _readoutModeForNormalImages = value;
                RaisePropertyChanged();
            }
        }

        public AsyncObservableCollection<BinningMode> BinningModes {
            get {
                if (_binningModes == null) {
                    _binningModes = new AsyncObservableCollection<BinningMode>();
                    _binningModes.Add(new BinningMode(1, 1));
                }

                return _binningModes;
            }
        }

        #endregion

        #region Supported Methods

        public void StartLiveView(CaptureSequence sequence) {
            LiveViewEnabled = true;
        }

        public void StopLiveView() {
            LiveViewEnabled = false;
        }

        public Task<bool> Connect(CancellationToken token) {
            return Task.Run<bool>(() => {
                try {
                    ResetLinkState();
                    _camera = SonyDriver.GetInstance().OpenCamera(_device.Id);
                } catch (Exception ex) {
                    Logger.Error(ex);
                    _camera = null;
                }

                if (_camera != null) {
                    try {
                        ShootingSetup.Apply(_camera);
                    } catch (Exception ex) {
                        // Never fail the connect over setup
                        Logger.Error(ex);
                    }
                }

                NotifyGainPropertiesChanged();
                return _camera != null;
            });
        }

        public void Disconnect() {
            if (_camera != null) {
                bool alreadyClosed;

                lock (_linkLock) {
                    alreadyClosed = _deadHandleClosed;
                }

                ResetLinkState();

                if (!alreadyClosed) {
                    try {
                        SonyDriver.GetInstance().CloseCamera(_camera.Handle);
                    } catch (Exception ex) {
                        Logger.Error(ex);
                    }
                }

                lock (_linkLock) {
                    _deadHandleClosed = false;
                }

                _camera = null;
                NotifyGainPropertiesChanged();
            }
        }

        public Task<IExposureData> DownloadLiveView(CancellationToken token) {
            return Task.Run<IExposureData>(() => {
                ThrowIfNotUsable();

                using (var memStream = new MemoryStream(SonyDriver.GetInstance().GetLiveView(_camera.Handle))) {
                    memStream.Position = 0;

                    JpegBitmapDecoder decoder =
                        new JpegBitmapDecoder(memStream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);

                    FormatConvertedBitmap bitmap = new FormatConvertedBitmap();
                    bitmap.BeginInit();
                    bitmap.Source = decoder.Frames[0];
                    bitmap.DestinationFormat = System.Windows.Media.PixelFormats.Gray16;
                    bitmap.EndInit();

                    ushort[] outArray = new ushort[bitmap.PixelWidth * bitmap.PixelHeight];
                    bitmap.CopyPixels(outArray, 2 * bitmap.PixelWidth, 0);

                    var metaData = new ImageMetaData();

                    return _exposureDataFactory.CreateImageArrayExposureData(
                        input: outArray,
                        width: bitmap.PixelWidth,
                        height: bitmap.PixelHeight,
                        bitDepth: 16,
                        isBayered: false,
                        metaData: metaData);
                }
            });
        }

        public void SetupDialog() {
            throw new NotImplementedException();
        }

        public void StartExposure(CaptureSequence sequence) {
            if (_camera != null) {
                var state = GetLinkState();

                if (state == LinkState.Lost) {
                    throw new CameraUnpluggedException(false);
                }

                if (state == LinkState.Reconnecting) {
                    // StartExposure can't wait, so remember it; WaitUntilExposureIsReady starts it
                    // once the camera is back (and can honour NINA's cancellation token while waiting).
                    Logger.Info("Camera is reconnecting; deferring the exposure until it is back");
                    _pendingExposure = sequence;
                    return;
                }

                BeginCapture(sequence);
            }
        }

        private void BeginCapture(CaptureSequence sequence) {
            SonyDriver driver = SonyDriver.GetInstance();
            uint captureStatus = driver.GetCaptureStatus(_camera.Handle);

            if (captureStatus == CAPTURE_CAPTURING || captureStatus == CAPTURE_PROCESSING || captureStatus == CAPTURE_STARTING ||
                captureStatus == CAPTURE_READING || captureStatus == CAPTURE_PROCESSING) {
                Notification.ShowWarning("Another exposure still in progress. Cancelling it to start another.");
            }

            // Tell the camera to cancel capture, we do this every time regardless - this will reset the status to be non-complete
            driver.CancelCapture(_camera.Handle);

            double exposureTime = sequence.ExposureTime;

            // The shutter type has to be set before the shutter speed: the electronic shutter has no BULB
            SetShutterType(driver, sequence, exposureTime);

            // Set the shutter speed (BULB above 30 s) rather than shooting at whatever the camera was left on
            try {
                driver.SetExposureTime(_camera.Handle, (float)exposureTime);
                VerifyShutterSpeed(driver, exposureTime);
            } catch (SonyException e) {
                Logger.Error($"Could not set the shutter speed for a {exposureTime}s exposure: {e.Message}");
                throw new SonyException($"Could not set the camera's shutter speed for a {exposureTime}s exposure: {e.Message}");
            }

            driver.StartCapture(_camera.Handle, (float)exposureTime);
        }

        private const uint PROPERTY_SHUTTER_TYPE = 0xD0DF; // 2 = mechanical, 3 = electronic
        private const uint SHUTTER_MECHANICAL = 2;
        private const int SHUTTER_TYPE_SETTLE_MS = 1500;
        private const uint SHUTTER_ELECTRONIC = 3;

        /// <summary>
        /// With the option on, snapshot exposures use the electronic shutter and everything else the
        /// mechanical one. Only writes when the type needs to change; cameras without the property
        /// are left alone. A failure is logged, not fatal: the exposure still runs.
        /// </summary>
        private void SetShutterType(SonyDriver driver, CaptureSequence sequence, double exposureTime) {
            if (!PluginSettings.Default.ElectronicShutterForSnapshots) {
                return;
            }

            try {
                if (_camera.GetPropertyInfo(PROPERTY_SHUTTER_TYPE) == null) {
                    return;
                }

                // The electronic shutter has no BULB, so a long snapshot stays mechanical
                bool snapshot = string.Equals(sequence.ImageType, "SNAPSHOT", StringComparison.OrdinalIgnoreCase);
                uint wanted = snapshot && exposureTime <= LONGEST_TIMED_SPEED ? SHUTTER_ELECTRONIC : SHUTTER_MECHANICAL;
                var current = driver.GetProperty(_camera.Handle, PROPERTY_SHUTTER_TYPE);

                if (current.Id == PROPERTY_SHUTTER_TYPE && current.Value == wanted) {
                    return;
                }

                Logger.Info($"Setting shutter type to {(wanted == SHUTTER_ELECTRONIC ? "electronic" : "mechanical")} for a {sequence.ImageType} exposure of {exposureTime}s");
                driver.SetProperty(_camera.Handle, PROPERTY_SHUTTER_TYPE, wanted);

                // The camera rebuilds its shutter speed list for the new type (the electronic one has no
                // BULB) and can reset the speed while it does. Wait for the type to show, then let the
                // list settle, so the speed written next isn't overwritten.
                for (int i = 0; i < 20; i++) {
                    var now = driver.GetProperty(_camera.Handle, PROPERTY_SHUTTER_TYPE);
                    if (now.Id == PROPERTY_SHUTTER_TYPE && now.Value == wanted) {
                        break;
                    }
                    Thread.Sleep(100);
                }

                Thread.Sleep(SHUTTER_TYPE_SETTLE_MS);
            } catch (Exception e) {
                Logger.Warning($"Could not set the shutter type: {e.Message}");
            }
        }

        private const uint PROPERTY_SHUTTER_SPEED = 0xD20D;
        private const double LONGEST_TIMED_SPEED = 30.0;

        /// <summary>
        /// The camera can accept a shutter write and still end up somewhere unusable (e.g. S mode has
        /// no BULB, so a long request lands on 30"). Read the speed back and refuse to shoot if it
        /// can't give the requested length.
        /// </summary>
        private void VerifyShutterSpeed(SonyDriver driver, double exposureTime) {
            if (exposureTime <= LONGEST_TIMED_SPEED) {
                try {
                    var set = driver.GetProperty(_camera.Handle, PROPERTY_SHUTTER_SPEED);
                    Logger.Info($"Shutter speed read back for a {exposureTime}s exposure: value 0x{set.Value:X}, text '{set.Text}'");
                } catch (Exception e) {
                    Logger.Warning($"Could not read back the shutter speed: {e.Message}");
                }
                return;
            }

            // The camera reports the new value asynchronously; give it a moment
            for (int i = 0; i < 10; i++) {
                var property = driver.GetProperty(_camera.Handle, PROPERTY_SHUTTER_SPEED);
                Logger.Info($"Shutter speed read back for a {exposureTime}s exposure: id 0x{property.Id:X}, value 0x{property.Value:X}, text '{property.Text}'");

                // An unrendered property comes back all zero, so require the id to match before trusting 0 as BULB
                if (property.Id == PROPERTY_SHUTTER_SPEED && property.Value == 0) {
                    return; // BULB
                }

                Thread.Sleep(100);
            }

            throw new SonyException(
                $"The camera did not go to BULB for a {exposureTime}s exposure and would stop at its own speed; set the camera to M (manual) mode.");
        }

        public void StopExposure() {
            AbortExposure();
        }

        public void AbortExposure() {
            _pendingExposure = null;

            // Nothing to cancel on a camera that is gone or being reopened
            if (_camera != null && GetLinkState() == LinkState.Ok) {
                SonyDriver.GetInstance().CancelCapture(_camera.Handle);
            }
        }

        public async Task WaitUntilExposureIsReady(CancellationToken token) {
            using (token.Register(AbortExposure)) {
                uint[] completionStates = { CAPTURE_CANCELLED, CAPTURE_COMPLETE, CAPTURE_FAILED };

                SonyDriver driver = SonyDriver.GetInstance();
                uint finalStatus = CAPTURE_CREATED;

                try {
                    var pending = _pendingExposure;

                    if (pending != null) {
                        Logger.Info("Waiting for the camera to reconnect before starting the exposure");
                        await WaitForReconnect(token);
                        _pendingExposure = null;
                        BeginCapture(pending);
                    }

                    uint captureStatus = driver.GetCaptureStatus(_camera.Handle);
                    Logger.Info(
                        $"Waiting for image to be ready, current state is {captureStatus}, completion states are {String.Join(", ", completionStates)}");

                    while (!completionStates.Contains(captureStatus)) {
                        await CoreUtil.Wait(TimeSpan.FromMilliseconds(100), token);

                        ThrowIfNotUsable();

                        captureStatus = driver.GetCaptureStatus(_camera.Handle);
                    }

                    Logger.Info($"Wait for image ready complete, completion state is {captureStatus}");
                    finalStatus = captureStatus;
                } catch (OperationCanceledException) {
                    throw;
                } catch (CameraUnpluggedException) {
                    throw;
                } catch (Exception ex) when (GetLinkState() != LinkState.Ok) {
                    Logger.Error("WaitUntilExposureIsReady failed after the camera was unplugged", ex);
                    throw new CameraUnpluggedException(GetLinkState() == LinkState.Reconnecting);
                } catch (Exception ex) {
                    Logger.Error("WaitUntilExposureIsReady got exception", ex);
                    throw new SonyException("Problem while waiting for image to be ready (see log)");
                }

                if (finalStatus == CAPTURE_FAILED) {
                    throw new SonyException("Exposure failed (see log)");
                }

                if (finalStatus == CAPTURE_CANCELLED) {
                    throw new OperationCanceledException("Exposure was cancelled");
                }
            }
        }

        public Task<IExposureData> DownloadExposure(CancellationToken token) {
            return Task.Run<IExposureData>(() => {
                byte[] rawImageData = SonyDriver.GetInstance().GetLastImage();

                if (rawImageData == null) {
                    throw new SonyException("No image available from the camera");
                }

                var metaData = new ImageMetaData();

                return _exposureDataFactory.CreateRAWExposureData(
                    converter: _profileService.ActiveProfile.CameraSettings.RawConverter,
                    rawBytes: rawImageData,
                    rawType: "arw",
                    bitDepth: this.BitDepth,
                    metaData: metaData);
            });
        }
        #endregion

        #region Unsupported Methods

        public string Action(string actionName, string actionParameters) {
            throw new NotImplementedException();
        }


        public void SendCommandBlind(string command, bool raw = true) {
            throw new NotImplementedException();
        }

        public bool SendCommandBool(string command, bool raw = true) {
            throw new NotImplementedException();
        }

        public string SendCommandString(string command, bool raw = true) {
            throw new NotImplementedException();
        }

        public void SetBinning(short x, short y) {
            // Ignore
        }
        
        public void UpdateSubSampleArea() {
            if (_camera == null) {
                EnableSubSample = false;
                SubSampleX = 0;
                SubSampleY = 0;
                SubSampleWidth = 0;
                SubSampleHeight = 0;
                return;
            }

            if (EnableSubSample && !CanSubSample) {
                Logger.Warning("Sub-sampling requested but not supported for Sony cameras. Falling back to full frame.");
                EnableSubSample = false;
            }

            // Sony cameras currently expose the entire frame, so always reset to the sensor dimensions.
            SubSampleX = 0;
            SubSampleY = 0;
            SubSampleWidth = _camera.ImageSize.Width;
            SubSampleHeight = _camera.ImageSize.Height;
        }

        #endregion


        // TODO!!! WE NEED ONE
        public bool HasSetupDialog => false;

        public IList<string> SupportedActions => new List<string>();
    }
}
