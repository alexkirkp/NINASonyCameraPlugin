using System;
using System.Threading;
using System.Threading.Tasks;
using NINA.Equipment.Model;
using NINA.RetroKiwi.Plugin.SonyCamera.Drivers;
using Sony;
using Xunit;
using PluginSettings = NINA.RetroKiwi.Plugin.SonyCamera.Properties.Settings;

namespace NINA.RetroKiwi.Plugin.SonyCamera.Tests {
    /// <summary>
    /// Runs the camera driver's unplug / reconnect logic against a fake native driver. The tests share
    /// static state (plugin settings, CameraLink), so they live in one class and run one at a time.
    /// </summary>
    public class ReconnectTests : IDisposable {
        private const uint PropIso = 0xD21E;
        private const string DevicePath = @"\\?\usb#vid_054c&pid_0c03#sn1#{guid}";

        private readonly FakeSonyDriver _fake = new FakeSonyDriver();
        private readonly int _savedGrace = PluginSettings.Default.ReconnectGraceSeconds;
        private CameraDriver _camera;

        public void Dispose() {
            try { _camera?.Disconnect(); } catch (Exception) { }
            PluginSettings.Default.ReconnectGraceSeconds = _savedGrace;
            CameraLink.Reconnecting = false;
        }

        private async Task<CameraDriver> ConnectedCamera(int graceSeconds) {
            PluginSettings.Default.ReconnectGraceSeconds = graceSeconds;
            _fake.SetDevices(FakeSonyDriver.MakeDevice(DevicePath));
            _camera = new CameraDriver(null, null, FakeSonyDriver.MakeDevice(DevicePath), _fake) {
                ReconnectRetryInterval = TimeSpan.FromMilliseconds(20)
            };

            Assert.True(await _camera.Connect(CancellationToken.None));
            Assert.True(_camera.Connected);
            return _camera;
        }

        private static async Task WaitFor(Func<bool> condition, string what, int timeoutMs = 5000) {
            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (!condition()) {
                if (DateTime.UtcNow > until) {
                    throw new TimeoutException("Timed out waiting for " + what);
                }

                await Task.Delay(10);
            }
        }

        private Task WaitReconnected(int expectedOpens = 2) =>
            WaitFor(() => _fake.OpenCount >= expectedOpens && !CameraLink.Reconnecting, "the camera to reconnect");

        private static CaptureSequence Sequence(double seconds) => new CaptureSequence { ExposureTime = seconds, ImageType = "LIGHT" };

        [Fact]
        public async Task Unplug_then_replug_reconnects_on_a_fresh_handle() {
            var camera = await ConnectedCamera(30);
            var id = camera.Id;
            var first = _fake.LastHandle;

            _fake.Unplug(first);

            // The first check after the removal starts the reconnect, and the link never reads as down
            Assert.True(camera.Connected);
            await WaitReconnected();

            Assert.True(camera.Connected);
            Assert.NotEqual(first, _fake.LastHandle);
            Assert.False(_fake.IsOpen(first)); // the dead handle was closed
            Assert.True(_fake.IsOpen(_fake.LastHandle));
            Assert.Equal(id, camera.Id);
        }

        [Fact]
        public async Task Reconnect_waits_until_the_camera_is_listed_again() {
            var camera = await ConnectedCamera(30);

            _fake.SetDevices(); // unplugged: nothing listed
            _fake.Unplug(_fake.LastHandle);
            Assert.True(camera.Connected);

            await Task.Delay(150);
            Assert.Equal(1, _fake.OpenCount);
            Assert.True(CameraLink.Reconnecting);
            Assert.True(camera.Connected);

            _fake.SetDevices(FakeSonyDriver.MakeDevice(DevicePath));
            await WaitReconnected();

            Assert.True(camera.Connected);
        }

        [Fact]
        public async Task Reconnect_finds_the_camera_again_when_its_device_id_changed() {
            var camera = await ConnectedCamera(30);
            var id = camera.Id;

            _fake.SetDevices(FakeSonyDriver.MakeDevice(@"\\?\usb#vid_054c&pid_0c03#other#{guid}"));
            _fake.Unplug(_fake.LastHandle);
            Assert.True(camera.Connected);
            await WaitReconnected();

            Assert.Equal(id, camera.Id); // the Id NINA holds is fixed at construction
        }

        [Fact]
        public async Task Window_running_out_reports_the_camera_disconnected() {
            var camera = await ConnectedCamera(1);

            _fake.SetDevices(); // never comes back
            _fake.Unplug(_fake.LastHandle);
            Assert.True(camera.Connected);

            await WaitFor(() => !camera.Connected, "the grace window to run out", 6000);

            Assert.False(CameraLink.Reconnecting);
            Assert.Equal(1, _fake.OpenCount);
            Assert.Throws<CameraUnpluggedException>(() => camera.StartExposure(Sequence(1)));
        }

        [Fact]
        public async Task A_zero_window_gives_up_straight_away() {
            var camera = await ConnectedCamera(0);

            _fake.Unplug(_fake.LastHandle);

            Assert.False(camera.Connected);
            Assert.Equal(1, _fake.OpenCount);
        }

        [Fact]
        public async Task Disconnect_during_a_reconnect_stops_it_and_does_not_resurrect_the_connection() {
            var camera = await ConnectedCamera(30);

            _fake.SetDevices();
            _fake.Unplug(_fake.LastHandle);
            Assert.True(camera.Connected);
            await WaitFor(() => CameraLink.Reconnecting, "the reconnect to start");

            camera.Disconnect();

            Assert.False(camera.Connected);
            Assert.False(CameraLink.Reconnecting);

            // The camera showing up afterwards must not be opened by the cancelled loop
            _fake.SetDevices(FakeSonyDriver.MakeDevice(DevicePath));
            await Task.Delay(200);

            Assert.Equal(1, _fake.OpenCount);
            Assert.False(camera.Connected);
        }

        [Fact]
        public async Task Connect_after_a_given_up_window_starts_clean() {
            var camera = await ConnectedCamera(0);
            _fake.Unplug(_fake.LastHandle);
            Assert.False(camera.Connected);

            camera.Disconnect();
            Assert.True(await camera.Connect(CancellationToken.None));

            Assert.True(camera.Connected);
            Assert.Equal(2, _fake.OpenCount);
        }

        [Fact]
        public async Task An_exposure_started_during_a_reconnect_begins_once_the_camera_is_back() {
            var camera = await ConnectedCamera(30);

            _fake.SetDevices();
            _fake.Unplug(_fake.LastHandle);
            Assert.True(camera.Connected);

            camera.StartExposure(Sequence(1)); // deferred: can't wait
            Assert.Equal(0, _fake.CaptureCount);

            var waiting = camera.WaitUntilExposureIsReady(CancellationToken.None);
            await Task.Delay(100);
            Assert.False(waiting.IsCompleted);
            Assert.Equal(0, _fake.CaptureCount);

            _fake.SetDevices(FakeSonyDriver.MakeDevice(DevicePath));
            await waiting.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, _fake.CaptureCount);
            Assert.Equal(_fake.LastHandle, _fake.Captures[0].Handle); // on the new handle
        }

        [Fact]
        public async Task A_deferred_exposure_fails_when_the_window_runs_out() {
            var camera = await ConnectedCamera(1);

            _fake.SetDevices();
            _fake.Unplug(_fake.LastHandle);
            Assert.True(camera.Connected);

            camera.StartExposure(Sequence(1));
            var waiting = camera.WaitUntilExposureIsReady(CancellationToken.None);

            await Assert.ThrowsAsync<CameraUnpluggedException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(6)));
            Assert.Equal(0, _fake.CaptureCount);
        }

        [Fact]
        public async Task A_deferred_exposure_can_be_cancelled_while_waiting() {
            var camera = await ConnectedCamera(30);

            _fake.SetDevices();
            _fake.Unplug(_fake.LastHandle);
            Assert.True(camera.Connected);

            camera.StartExposure(Sequence(1));
            using (var cts = new CancellationTokenSource()) {
                var waiting = camera.WaitUntilExposureIsReady(cts.Token);
                await Task.Delay(50);
                cts.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(5)));
            }

            // The cancelled exposure must not start when the camera does come back
            _fake.SetDevices(FakeSonyDriver.MakeDevice(DevicePath));
            await WaitReconnected();
            Assert.Equal(0, _fake.CaptureCount);
        }

        [Fact]
        public async Task A_gain_change_during_a_reconnect_is_applied_once_the_camera_is_back() {
            var camera = await ConnectedCamera(30);
            var first = _fake.LastHandle;

            _fake.SetDevices();
            _fake.Unplug(first);
            Assert.True(camera.Connected);

            camera.Gain = 800;

            Assert.Empty(_fake.WritesTo(first)); // not written to the dead handle

            _fake.SetDevices(FakeSonyDriver.MakeDevice(DevicePath));
            await WaitReconnected();

            Assert.Contains(_fake.WritesTo(_fake.LastHandle), w => w.Property == PropIso && w.Value == 800);
            Assert.Equal(800, camera.Gain);
        }

        [Fact]
        public async Task A_gain_change_is_dropped_when_the_window_runs_out() {
            var camera = await ConnectedCamera(1);
            var first = _fake.LastHandle;

            _fake.SetDevices();
            _fake.Unplug(first);
            Assert.True(camera.Connected);
            camera.Gain = 800;

            await WaitFor(() => !camera.Connected, "the grace window to run out", 6000);

            Assert.Empty(_fake.WritesTo(first));
            Assert.Equal(-1, camera.Gain);
        }
    }
}
