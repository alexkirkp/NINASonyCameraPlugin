using System;
using System.Collections.Generic;
using System.Linq;
using Sony;

namespace NINA.RetroKiwi.Plugin.SonyCamera.Tests {
    /// <summary>
    /// A scriptable stand-in for the native driver. Thread-safe, because the camera driver calls it
    /// from its reconnect loop as well as from the test thread.
    /// </summary>
    internal class FakeSonyDriver : ISonyDriver {
        private readonly object _lock = new object();
        private readonly HashSet<uint> _removed = new HashSet<uint>();
        private readonly HashSet<uint> _open = new HashSet<uint>();
        private readonly Dictionary<uint, uint> _properties = new Dictionary<uint, uint>();
        private uint _nextHandle = 1;

        public List<SonyDevice> Devices { get; } = new List<SonyDevice>();
        public List<string> Calls { get; } = new List<string>();
        public List<uint> OpenedHandles { get; } = new List<uint>();
        public List<uint> ClosedHandles { get; } = new List<uint>();
        public List<(uint Handle, uint Property, uint Value)> Writes { get; } = new List<(uint, uint, uint)>();
        public List<(uint Handle, float Seconds)> Captures { get; } = new List<(uint, float)>();

        public uint CaptureStatus { get; set; }

        public static SonyDevice MakeDevice(string path, string model = "ILCE-6700") =>
            new SonyDevice(new PortableDeviceInfo { id = path, manufacturer = "Sony", model = model, devicePath = path });

        public uint LastHandle { get { lock (_lock) { return OpenedHandles.Count == 0 ? 0 : OpenedHandles[OpenedHandles.Count - 1]; } } }

        public int OpenCount { get { lock (_lock) { return OpenedHandles.Count; } } }

        /// <summary>Simulates pulling the cable: the handle reports removed from now on.</summary>
        public void Unplug(uint handle) {
            lock (_lock) { _removed.Add(handle); }
        }

        public void SetDevices(params SonyDevice[] devices) {
            lock (_lock) {
                Devices.Clear();
                Devices.AddRange(devices);
            }
        }

        public bool IsOpen(uint handle) {
            lock (_lock) { return _open.Contains(handle); }
        }

        public IReadOnlyList<(uint Handle, uint Property, uint Value)> WritesTo(uint handle) {
            lock (_lock) { return Writes.Where(w => w.Handle == handle).ToList(); }
        }

        public int CaptureCount { get { lock (_lock) { return Captures.Count; } } }

        public IEnumerable<SonyDevice> Cameras() {
            lock (_lock) { return Devices.ToList(); }
        }

        public SonyCameraInfo OpenCamera(string path) {
            lock (_lock) {
                if (!Devices.Any(d => d.Id == path)) {
                    throw new SonyException("No such device " + path);
                }

                var handle = _nextHandle++;
                _open.Add(handle);
                OpenedHandles.Add(handle);
                CaptureStatus = 0;

                return new SonyCameraInfo(handle, new DeviceInfo { Model = "ILCE-6700" }, new CameraInfo(), new List<PropertyInfo>());
            }
        }

        public void CloseCamera(uint handle) {
            lock (_lock) {
                _open.Remove(handle);
                ClosedHandles.Add(handle);
            }
        }

        public PropertyValue GetProperty(uint handle, uint propertyId) {
            lock (_lock) {
                _properties.TryGetValue(propertyId, out var value);
                return new PropertyValue { Id = propertyId, Value = value, Text = "" };
            }
        }

        public void SetProperty(uint handle, uint propertyId, uint value) {
            lock (_lock) {
                Writes.Add((handle, propertyId, value));
                _properties[propertyId] = value;
            }
        }

        public byte[] GetLiveView(uint handle) => throw new NotSupportedException();

        public uint GetCaptureStatus(uint handle) {
            lock (_lock) { return CaptureStatus; }
        }

        public void SetExposureTime(uint handle, float exposureTime) { }

        public void StartCapture(uint handle, float exposureTime) {
            lock (_lock) {
                Captures.Add((handle, exposureTime));
                CaptureStatus = 4; // complete
            }
        }

        public void CancelCapture(uint handle) { }

        public bool IsCameraRemoved(uint handle) {
            lock (_lock) { return _removed.Contains(handle); }
        }

        public byte[] GetLastImage() => null;

        public void ReleaseLastImage() { }

        // Focus model, as the real DLL behaves: attaching the lens to a handle forgets the position
        // (it reads 0xFFFF) until the first move on that handle.
        private readonly Dictionary<uint, uint> _focus = new Dictionary<uint, uint>();
        private uint _lastFocus = 0xFFFF;

        public uint FocusLimit { get; set; } = 3120;
        public List<(uint Handle, uint Position)> FocusMoves { get; } = new List<(uint, uint)>();
        public List<(uint Handle, string LensId)> LensAttaches { get; } = new List<(uint, string)>();

        /// <summary>False models a DLL that carries the position over to the new handle.</summary>
        public bool ForgetFocusOnAttach { get; set; } = true;

        public void SetAttachedLens(uint handle, string path) {
            lock (_lock) {
                LensAttaches.Add((handle, path));
                _focus[handle] = ForgetFocusOnAttach ? 0xFFFFu : _lastFocus;
            }
        }

        /// <summary>Where a move to a position lands; the real DLL can stop short of the request.</summary>
        public Func<uint, uint> FocusLanding { get; set; } = p => p;

        /// <summary>When set, focus moves fail as the DLL does with a bad handle.</summary>
        public bool FailFocusMoves { get; set; }

        public uint SetFocusPosition(uint handle, uint position) {
            lock (_lock) {
                FocusMoves.Add((handle, position));

                if (FailFocusMoves) {
                    throw new SonyException($"Problem setting focus position to {position}, error 6");
                }

                var reached = FocusLanding(position);
                _focus[handle] = reached;
                _lastFocus = reached;
                return reached;
            }
        }

        public uint GetFocusPosition(uint handle) {
            lock (_lock) { return _focus.TryGetValue(handle, out var p) ? p : 0xFFFF; }
        }

        public uint GetFocusLimit(uint handle) {
            lock (_lock) { return FocusLimit; }
        }

        /// <summary>Makes the DLL report a given raw position on a handle, as if it had read it.</summary>
        public void ReportFocus(uint handle, uint position) {
            lock (_lock) { _focus[handle] = position; _lastFocus = position; }
        }
    }
}
