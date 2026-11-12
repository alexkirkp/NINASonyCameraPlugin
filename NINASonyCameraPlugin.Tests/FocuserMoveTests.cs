using System;
using System.Threading;
using System.Threading.Tasks;
using NINA.RetroKiwi.Plugin.SonyCamera.Drivers;
using Sony;
using Xunit;

namespace NINA.RetroKiwi.Plugin.SonyCamera.Tests {
    /// <summary>Lens focuser moves: where the lens is recorded to be, and what NINA sees on failure.</summary>
    [Collection(CameraLinkCollection.Name)]
    public class FocuserMoveTests : IDisposable {
        private const uint Handle = 7;

        private readonly FakeSonyDriver _fake = new FakeSonyDriver();
        private FocuserDriver _focuser;

        public void Dispose() {
            try { _focuser?.Disconnect(); } catch (Exception) { }
        }

        private async Task<FocuserDriver> ConnectedFocuser() {
            var lens = new SonyLens(new LensInfo { Id = @"Tamron\a057", Manufacturer = "Tamron", Model = "150-500mm" });
            _focuser = new FocuserDriver(null, lens, _fake, () => Handle);
            Assert.True(await _focuser.Connect(CancellationToken.None));
            return _focuser;
        }

        [Fact]
        public async Task Move_records_where_the_dll_landed_not_the_request() {
            var focuser = await ConnectedFocuser();
            _fake.FocusLanding = p => p - 1; // the DLL stopped one short

            await focuser.Move(2990, CancellationToken.None, 0);

            Assert.Equal(2989, focuser.LastKnownPosition);
            Assert.Equal(2989, focuser.Position);
            Assert.False(focuser.IsMoving);
        }

        [Fact]
        public async Task A_failed_move_faults_the_task_and_keeps_the_last_position() {
            var focuser = await ConnectedFocuser();
            await focuser.Move(2975, CancellationToken.None, 0);
            _fake.FailFocusMoves = true;

            await Assert.ThrowsAsync<SonyException>(() => focuser.Move(2990, CancellationToken.None, 0));

            Assert.Equal(2975, focuser.LastKnownPosition);
            Assert.False(focuser.IsMoving);
        }

        [Fact]
        public async Task A_cancelled_move_does_not_touch_the_lens() {
            var focuser = await ConnectedFocuser();
            var movesBefore = _fake.FocusMoves.Count;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => focuser.Move(2990, new CancellationToken(true), 0));

            Assert.Equal(movesBefore, _fake.FocusMoves.Count);
            Assert.False(focuser.IsMoving);
        }

        [Fact]
        public async Task Setting_the_position_also_records_the_landing() {
            var focuser = await ConnectedFocuser();
            _fake.FocusLanding = p => p + 1;

            focuser.Position = 3000;

            Assert.Equal(3001, focuser.Position);
        }
    }
}
