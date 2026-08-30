# ASUS Hardware Service

A lightweight Windows service for ASUS laptops that applies a battery charge limit, listens for ASUS hotkey events, adjusts display and keyboard backlight brightness, sets ASUS Splendid color settings, toggles microphone mute, switches between performance modes, and applies selected laptop display settings.
## What it does

The service runs in the background and handles a small set of ASUS-specific hardware features:

- applies a configured battery charge limit at startup
- listens for ASUS HID hotkey events
- adjusts built-in display brightness
- adjusts keyboard backlight brightness
- applies ASUS Splendid display color settings
- toggles the mute state of the microphone
- switches between CPU and GPU performance modes
- applies configured laptop screen refresh-rate and overdrive settings
- applies configured MiniLED zone settings
## Build and publish

Build and publish the project for Windows x64:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```
## Install and run

After publishing, install the executable with `sc.exe` and start the service.

Replace `AsusHardwareService.exe` below with the actual published executable name from your project.

```powershell
sc.exe create "ASUS Hardware Service" binPath= "C:\Path\To\AsusHardwareService.exe" start= auto
sc.exe config "ASUS Hardware Service" DisplayName= "ASUS Hardware Service"
sc.exe start "ASUS Hardware Service"
```
# Contributing

Contributions are welcome! Please fork the repository and submit pull requests.

# License

This project is licensed under the MIT License.

# Acknowledgements

NHL Stenden: For providing the foundational code and utilities. Martin Bosgra: Author and primary maintainer of the project.
