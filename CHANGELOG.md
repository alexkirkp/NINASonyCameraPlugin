# Sony Camera Plugin

## 1.0.0.5
* Requires the Sony driver DLL 1.0.1.21 or later (was 1.0.1.17). The version check now compares the parts in the right order.
* Notices a camera unplug and reconnects automatically for a configurable time (default 60 s); the lens focuser stays connected and exposures wait for the post-reconnect setup.
* New option: astro shooting setup applied on connect (manual exposure mode, single-shot drive, manual focus, widest aperture, save destination).
* New option: electronic shutter for snapshot exposures.
* The shutter speed is set from the exposure time and the list settles after a shutter type change.
* A failed or cancelled exposure no longer returns a stale image.
* After a camera reconnect the lens focuser moves back to its last known position, since the driver DLL forgets it on the new handle; the focuser never reports the DLL's 65535 "unknown" value, which had sent AutoFocus to the far focus limit.
* Focuser read errors are reported as errors instead of a position of 6; the focuser no longer throws from `HasSetupDialog` or `Halt`.
* AUTO ISO is selectable and the gain range is correct.
* Each camera now has its own Id (model plus USB instance), so several Sony bodies can be told apart. Existing profiles need the camera selected once more.
* Small driver fixes: exposure while unconnected, a buffer leak, a held image, log spam.

## 1.0.0.4
* Added the `UpdateSubSampleArea` implementation and bumped the minimum NINA version so the plugin loads in 3.2.
* Restored the ISO/Gain UI by notifying NINA whenever the camera connection updates ISO data and by handling cameras without ISO option lists.
* Probed the registry ISO property (`0xFFFE`) so older Sony bodies still populate the gain dropdown.
* Logged clearer errors when gain min/max cannot be determined and improved the GitHub workflow to publish the release DLL artifact.

## 1.0.0.3
Updated to support new device property "DisplayName" required in NINA 3

## 1.0.0.2
Rebuilt using .NET Core 8 for NINA 3

## 1.0.0.1
Initial release - Supports all functionality provided by the ASCOM version, plus:
* LiveView support
* Actual ISO values displayed in Gain drop-down vs a numeric index (1, 2, 3, etc)

Note that NINA saves the raw ARW files and does not generate FITS files for DSLRs (per the note in NINA: Options.Imaging).
