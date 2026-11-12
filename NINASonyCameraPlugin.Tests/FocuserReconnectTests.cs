using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NINA.RetroKiwi.Plugin.SonyCamera.Drivers;
using Sony;
using Xunit;
using PluginSettings = NINA.RetroKiwi.Plugin.SonyCamera.Properties.Settings;

namespace NINA.RetroKiwi.Plugin.SonyCamera.Tests {
    /// <summary>
    /// The lens focuser across a camera unplug / reconnect. The fake DLL forgets the lens position on
    /// the new handle (reads 0xFFFF), as the real one did in the 2026-11-11 live run.
    /// </summary>
    [Collection(CameraLinkCollection.Name)]
    public class FocuserReconnectTests : IDisposable {
        private const string DevicePath = @"\\?\usb#vid_054c&pid_0c03#sn1#{guid}";
        private const string LensId = @"Tamron\a057";

        private readonly FakeSonyDriver _fake = new FakeSonyDriver();
        private readonly int _savedGrace = PluginSettings.Default.ReconnectGraceSeconds;
        private CameraDriver _camera;
        private FocuserDriver _focuser;

        public void Dispose() {
            try { _focuser?.Disconnect(); } catch (Exception) { }
            try { _camera?.Disconnect(); } catch (Exception) { }
            PluginSettings.Default.ReconnectGraceSeconds = _savedGrace;
            CameraLink.Reconnecting = false;
        }

        /// <summary>The open camera handle, or 0 while the camera is closed (as SonyDriver.CameraInfo).</summary>
        private uint OpenHandle() {
            var handle = _fake.LastHandle;
            return handle != 0 && _fake.IsOpen(handle) ? handle : 0;
        }

        private async Task<FocuserDriver> ConnectedFocuser() {
            PluginSettings.Default.ReconnectGraceSeconds = 30;
            _fake.SetDevices(FakeSonyDriver.MakeDevice(DevicePath));
            _camera = new CameraDriver(null, null, FakeSonyDriver.MakeDevice(DevicePath), _fake) {
                ReconnectRetryInterval = TimeSpan.FromMilliseconds(20)
            };
            Assert.True(await _camera.Connect(CancellationToken.None));

            var lens = new SonyLens(new LensInfo { Id = LensId, Manufacturer = "Tamron", Model = "150-500mm" });
            _focuser = new FocuserDriver(null, lens, _fake, OpenHandle);
            Assert.True(await _focuser.Connect(CancellationToken.None));
            return _focuser;
        }

        private async Task UnplugAndReconnect() {
            var first = _fake.LastHandle;
            _fake.Unplug(first);
            Assert.True(_camera.Connected); // starts the reconnect

            var until = DateTime.UtcNow.AddSeconds(5);
            while (!(_fake.OpenCount >= 2 && !CameraLink.Reconnecting && _fake.LensAttaches.Any(a => a.Handle == _fake.LastHandle))) {
                if (DateTime.UtcNow > until) {
                    throw new TimeoutException("Timed out waiting for the camera and lens to reconnect");
                }
                await Task.Delay(10);
            }

            // The re-attach and restore run on the reconnect loop; give them a moment to finish
            await Task.Delay(50);
        }

        [Fact]
        public async Task Reconnect_restores_the_last_known_position_on_the_new_handle() {
            var focuser = await ConnectedFocuser();
            await focuser.Move(2975, CancellationToken.None, 0);
            Assert.Equal(2975, focuser.Position);

            await UnplugAndReconnect();
            var handle = _fake.LastHandle;

            Assert.Contains(_fake.FocusMoves, m => m.Handle == handle && m.Position == 2975);
            Assert.Equal(2975, focuser.Position);
            Assert.True(focuser.Connected);
        }

        [Fact]
        public async Task Position_tracked_from_reads_is_restored_after_reconnect() {
            var focuser = await ConnectedFocuser();
            _fake.ReportFocus(_fake.LastHandle, 1800); // e.g. moved by hand or set before connecting
            Assert.Equal(1800, focuser.Position);

            await UnplugAndReconnect();

            Assert.Contains(_fake.FocusMoves, m => m.Handle == _fake.LastHandle && m.Position == 1800);
            Assert.Equal(1800, focuser.Position);
        }

        [Fact]
        public async Task Reconnect_does_not_move_the_lens_when_the_dll_still_knows_the_position() {
            var focuser = await ConnectedFocuser();
            await focuser.Move(2000, CancellationToken.None, 0);
            var movesBefore = _fake.FocusMoves.Count;
            _fake.ForgetFocusOnAttach = false;

            await UnplugAndReconnect();

            Assert.Equal(movesBefore, _fake.FocusMoves.Count);
            Assert.Equal(2000, focuser.Position);
        }

        [Fact]
        public async Task The_dll_sentinel_is_never_reported_to_nina() {
            var focuser = await ConnectedFocuser();
            await focuser.Move(2975, CancellationToken.None, 0);

            _fake.ReportFocus(_fake.LastHandle, 0xFFFF);
            Assert.Equal(2975, focuser.Position);

            _fake.ReportFocus(_fake.LastHandle, 4000); // above the limit
            Assert.Equal(2975, focuser.Position);

            _fake.ReportFocus(_fake.LastHandle, 3000); // a real reading takes over again
            Assert.Equal(3000, focuser.Position);
        }

        [Fact]
        public async Task With_no_known_position_the_report_stays_within_the_focus_range() {
            var focuser = await ConnectedFocuser(); // attach leaves the fake DLL at 0xFFFF

            Assert.InRange(focuser.Position, 0, focuser.MaxStep);
        }
    }
}
