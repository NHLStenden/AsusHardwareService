# ASUS Hardware Service

A lightweight Windows service for ASUS laptops that applies a battery charge limit, listens for ASUS hotkey events, adjusts display brightness, and sets ASUS Splendid colour settings.

## What it does

The service runs in the background and handles a small set of ASUS-specific hardware features:

- applies a configured battery charge limit at startup
- listens for ASUS HID hotkey events
- adjusts built-in display brightness
- applies ASUS Splendid display colour settings

This project is intended for ASUS laptops on Windows. It relies on ASUS-specific drivers and utilities such as `ATKWMIACPIIO`, `\\.\ATKACPI`, and `AsusSplendid.exe`.

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
