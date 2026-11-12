# Sony Camera Plugin

Native NINA plugin for Sony mirrorless/DSLR bodies over the Sony MTP interface, so you can control the camera directly from NINA without ASCOM.

## Features

- Supports many Sony cameras (supported list: https://github.com/dougforpres/ASCOMSonyCameraDriver/wiki/Supported-Cameras)
- Connects Sony bodies and surfaces their model name in NINA
- LiveView streaming (16-bit monochrome, where supported) plus normal captures saved as ARW through NINA's RAW converter so full metadata is preserved
- Gain dropdown populated with actual ISO values (including Auto when present); ISO is controllable via the Gain property
- Battery level reporting; exposes camera pixel size and resolution
- Camera-controlled lenses exposed as a focuser inside NINA (supported lenses: https://github.com/dougforpres/ASCOMSonyCameraDriver/wiki/Supported-Lenses)
- Handles cameras that do not expose ISO options until they are "learned" in-camera
- Notices when the camera is unplugged and reconnects when it comes back (see "Unplugging the camera")
- Optional astro shooting setup applied on connect, and an optional electronic shutter for snapshot exposures (see "Options")
- Each camera gets its own Id (model plus USB instance), so several Sony bodies can be told apart

## Requirements

- NINA 3.2 or newer (built against `NINA.Plugin` 3.2.x)
- Windows 10 1809 or newer (.NET 8, WPF)
- Sony body set to PC Remote/MTP mode
- This plugin uses the core (non-ASCOM) files from the Sony ASCOM driver v1.0.1.21 or later (`SonyMTPCamera.dll`); install that driver from https://github.com/dougforpres/ASCOMSonyCameraDriver/releases so the shared components are present. The plugin refuses to start with an older DLL. 1.0.1.21 is the version unplug detection (`IsCameraRemoved`), reconnect and focuser error reporting were developed and tested against; 1.0.1.17 is no longer enough. A stale older copy in `C:\Windows\System32` can be picked up instead, so check the version in the NINA log ("Using Sony driver dll @ ... with version ...").

## Installation

Install via the N.I.N.A. plugin manager or manually download and copy the DLL file into `%LOCALAPPDATA%\\NINA\\Plugins\\<Major>.<Minor>.<Hotfix>\\Sony Camera Plugin\\` from:
https://github.com/dougforpres/NINASonyCameraPlugin/releases

## Usage notes

- ISO/gain list may be empty until the camera has learned ISO options; set an ISO on-camera once if needed.
- Temperature is not reported (the plugin does not request processed ARW metadata).
- Binning and sub-sampling are not supported; captures use the full sensor frame.
- For exposures <= 30s the driver chooses the nearest built-in shutter speed; longer exposures fall back to Bulb.
- If the focuser (lens) cannot be read, the plugin reports an error instead of a position.

## Options

Found in NINA under Options > Plugins > Sony Camera Plugin. Options marked "applied on connect" are written to the camera each time it connects (and again after a reconnect); only properties the camera lists are written, and a refused write shows a NINA warning instead of failing the connect. Unticked options leave the camera alone.

- **Reconnect after unplug for up to (seconds, 0 = off)**: how long to keep trying to reconnect after an unplug. Default 60; 0 turns reconnecting off.
- **Manual exposure mode**: takes PC control from the mode dial and sets M. The camera stays in M after disconnect. Default off.
- **Single-shot drive**: needed for BULB. Default off.
- **Manual focus**: sets the camera to manual focus. Default off.
- **Widest aperture the lens reports**: sets the lowest f-number the lens offers; skipped for manual lenses that report no aperture. Default off.
- **Electronic shutter for snapshot exposures**: uses the electronic shutter for short snapshots such as autofocus and plate solving. There is no BULB with it, so 30 s at most. Default off.
- **Save images to** (0 = leave, 1 = PC only, 2 = PC + card): where the camera stores images so they reach the PC. Default 0 (leave as is).

## Unplugging the camera

If the camera is unplugged while connected, the plugin notices it (using `IsCameraRemoved` in the driver DLL, so DLL 1.0.1.21 or later) and tries to reconnect for the number of seconds set in Options. The lens focuser stays connected through this. If the camera comes back within the window, NINA shows a warning with how long it took, the shooting setup is applied again, and exposures held during the gap continue; a frame in progress at the moment of the unplug is lost. If it does not come back in time, NINA shows an error and you need to reconnect the camera manually.

Known limitation: with the camera connected, unplugging can still crash NINA on some systems inside Windows' MTP stack (a PortableDeviceApi.dll access violation seen on Windows 11 with DLL 1.0.1.21). This is in the driver DLL / Windows, not the plugin. If your sessions are not unattended, disconnect in NINA before unplugging.

## Support

- Best contact is email via the Homepage link or the ASCOM driver troubleshooting page footer.
- You may be asked for a driver log when reporting bugs: https://github.com/dougforpres/ASCOMSonyCameraDriver/wiki/Troubleshooting#the-driver-dll-log
- Help links:
  - My camera is not supported (https://github.com/dougforpres/ASCOMSonyCameraDriver/wiki/Installation#if-you-dont-have-a-supported-camera-model);
  - Supported cameras (https://github.com/dougforpres/ASCOMSonyCameraDriver/wiki/Supported-Cameras);
  - Other known issues (https://github.com/dougforpres/ASCOMSonyCameraDriver/wiki/Troubleshooting)

## Metadata

- Version: 1.0.0.5
- Author: Doug Henderson
- Contributors: Lucas Lepski [@ShurkanTwo](https://github.com/shurkanTwo)
- Minimum NINA version: 3.2.0.3001
- License: MPL-2.0 (https://www.mozilla.org/en-US/MPL/2.0/)
- Homepage: https://retro.kiwi
- Changelog: https://github.com/dougforpres/NINASonyCameraPlugin/blob/master/CHANGELOG.md
