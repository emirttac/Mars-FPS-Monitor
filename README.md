# Mars FPS Monitor

<p align="center">
  <img src="Assets/mars-logo.png" alt="Mars FPS Monitor" width="128" height="128" />
</p>

<p align="center">
  Windows overlay and control panel for FPS, frametime, temperatures, a game library, optional GPU overclock, and fan control.
</p>

<p align="center">
  <a href="https://github.com/emirttac/Mars-FPS-Monitor/releases"><img src="https://img.shields.io/github/v/release/emirttac/Mars-FPS-Monitor?style=for-the-badge&color=F24C1D" alt="Latest release" /></a>
  <a href="https://dotnet.microsoft.com/download/dotnet/8.0"><img src="https://img.shields.io/badge/.NET-8.0%20WPF-512BD4?style=for-the-badge&logo=dotnet" alt=".NET 8 WPF" /></a>
  <a href="https://learn.microsoft.com/windows/win32/"><img src="https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20x64-0078D6?style=for-the-badge&logo=windows" alt="Windows 10 and 11 x64" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-green?style=for-the-badge" alt="MIT License" /></a>
</p>

**Version 3.0** measures frame rate from Windows graphics events. It does not inject a DLL into the game. Windowed and borderless games use a click-through WPF HUD. Exclusive fullscreen uses [RivaTuner Statistics Server](https://www.guru3d.com/) through shared memory.

The app requires administrator rights. ETW kernel sessions and hardware sensors do not work without them.

## What you can do

| Area | What v3.0 provides |
| :--- | :--- |
| **Home** | Live CPU, GPU, and RAM gauges. |
| **Overlay** | FPS, frametime, 1% low, clocks, load, temperatures, RAM, VRAM, and optional OC or fan status. Seven HUD layouts, color, size, and nine anchor positions. |
| **Library** | Launch games from Steam, Epic, GOG, EA App, and Ubisoft Connect, or add an executable yourself. The selected game shows average FPS, 1% low FPS, and maximum and average CPU/GPU temperatures for tracked play time. |
| **Overclock** | Off, automatic thermal profiles, or a fixed profile. NVIDIA (NVAPI), AMD (ADL), and Intel Arc (IGCL). Suggestions from the optional AI assistant are clamped before you can save them. |
| **Fans** | Public beta. Profile curves or a fixed PWM value, where the hardware allows a write. Laptops are often limited to vendor thermal profiles, or to read-only RPM. |
| **Languages** | English, Türkçe, Azərbaycan, Deutsch, Español, Français, Português, Português (Brasil), Русский, and 简体中文. A new install follows the Windows UI language. |

The first launch after install, and the first launch of a version you have not acknowledged, opens the release notes. Check **I have read and understood**, then **Continue**. Later launches go straight to the app. Starting the executable again while Mars is open brings the existing window forward.

## How frame rate is measured

`FpsMonitor` opens an ETW session named `Mars_FPS_Monitor_Session` and listens to three graphics providers. A frame is counted only for the foreground process, and only for a Present event:

| Provider | Event | Used for |
| :--- | :--- | :--- |
| DXGI | 42 | Direct3D 10, 11, and 12 |
| Direct3D 9 | 1 | Direct3D 9 |
| DxgKrnl | 184 | Kernel Present |

Flip and Blit events are ignored, because they accompany Present and would double-count. If more than one provider reports the same frame, Direct3D 9 is preferred, then DXGI, then DxgKrnl. Switching to a higher-priority source clears the current sample window.

FPS is the number of accepted presents in the current one-second window. Frametimes between 0 and 1000 ms are kept in a rolling queue of 100 samples. 1% low is computed once that queue has at least 10 samples: the slowest 1% of those frametimes, expressed as FPS. If the ETW session cannot start, FPS is reported as `-1` and the UI asks for administrator rights.

## Overlay

<img width="710" height="499" alt="image" src="https://github.com/user-attachments/assets/5076fc10-cc92-4b7e-8633-f230b99d472a" />

Desktop, windowed, and borderless fullscreen use `OverlayWindow`: a layered, click-through, non-activating tool window. You can lock it to a screen anchor or drag it. The built-in layouts are Classic Minimalist, Gamer Panel, Steam Deck Style, Advanced Performance HUD, Compact Pill, Neon Glass, and Tower.

Exclusive fullscreen bypasses the desktop compositor, so the WPF HUD is not visible there. Mars writes the same readout into the `RTSSSharedMemoryV2` map under the owner name `MarsFPSMonitor` and hides the RTSS interface. Setup installs RTSS 7.3.7 silently when it is not already present. If that install fails, Mars still installs; exclusive-fullscreen OSD stays unavailable until RTSS is installed.

## Sensors

<img width="682" height="621" alt="image" src="https://github.com/user-attachments/assets/2eb03aa1-eb29-429c-a69c-cdfe15483f29" />

`HardwareMonitorManager` reads CPU, GPU, RAM, and fans through LibreHardwareMonitor 0.9.6. On modern AMD and Intel CPUs, package and core temperatures usually require the [PawnIO](https://pawnio.eu/) driver. If those readings are missing, Mars falls back to Windows ACPI thermal zones (`MSAcpi_ThermalZoneTemperature` and the thermal-zone performance counters). Raw ACPI values are treated as kelvin, or tenths of kelvin when the number is 1000 or higher, and are accepted only between 10 °C and 120 °C.

Displayed temperatures pass through a five-sample average taken about once per second. Readings at or below 0 °C are dropped, so a missed poll does not flash the HUD to zero. The first samples are retried sooner, because the first LibreHardwareMonitor open often returns empty CPU temperatures.

On a machine with more than one GPU, an empty or stale selection prefers a discrete adapter: NVIDIA, then AMD, then Intel. You can still pick the adapter explicitly. Overclock and fan writes follow that selection, and a model name is applied only when it matches one adapter exactly. Ti, Super, and XT are different models.

## Overclock

<img width="952" height="629" alt="image" src="https://github.com/user-attachments/assets/8fac29b7-6511-4c5a-b6a6-9818c26f0ce5" />

Three modes:

- **Off** restores driver defaults.
- **Auto** applies a temperature band only while a game is detected.
- **Manual** holds one saved profile until you change it or exit.

A game is detected when the foreground process is not a browser, launcher, or desktop app, the window covers the screen, and GPU 3D load is above 30%. Leaving the game waits 10 seconds before clocks drop, so an Alt-Tab does not thrash the profile. A toast is shown when a session starts and when it ends.

The default thermal bands, which you can edit, are:

| Profile | Core temperature | Core offset | Memory offset |
| :--- | :--- | ---: | ---: |
| Extreme | 0–74 °C | +50 MHz | +50 MHz |
| Performance | 75–81 °C | +25 MHz | +25 MHz |
| Eco | 82–100 °C | 0 | 0 |

Moving to a more aggressive band requires the temperature to sit at least 4 °C under that band’s ceiling. Any profile change then waits 5 seconds. A safer band can be selected without that upgrade gate, still subject to the cooldown.

Nothing is written outside these limits, including imported profiles and AI suggestions:

| Setting | Allowed range |
| :--- | :--- |
| Core offset | 0 to +100 MHz |
| Memory offset | 0 to +300 MHz |
| Power limit | 80% to 110% |

AI mode caps are tighter: Eco stays at stock and 100% power, Performance at +50 / +100 MHz and 105%, Extreme at the absolute ceiling. The assistant never applies a result by itself. You review it and save it.

Fail-closed behavior:

- Invalid or missing core temperature selects **Safe / Off**.
- A hotspot of 95 °C or higher does the same. Manual mode stays selected, and clocks return after the hotspot falls and the sensor is valid again.
- Exit, crash, or Windows session end restores driver clocks and releases software fan control.
- If the process is killed before that restore, the next launch clears leftover offsets and PWM overrides before applying anything new. A failed restore is retried about once a second.

## Fans

<img width="968" height="566" alt="image" src="https://github.com/user-attachments/assets/e6f3988e-6e1e-4e06-9248-c798b1a30075" />

Fan control is a **public beta**. Coverage depends on the chipset, BIOS, embedded controller, and vendor driver. On many laptops the EC does not expose a writable PWM register; Mars can then show RPM only, or switch among the vendor’s thermal profiles.

Write paths, when the hardware exposes them:

- Motherboard Super I/O through LibreHardwareMonitor, more reliable with PawnIO on desktops.
- NVIDIA coolers through NVAPI.
- AMD coolers through ADL.
- Selected OEM laptop WMI interfaces: ASUS ROG/TUF, Lenovo Legion, HP Omen/Victus, Dell/Alienware, and MSI.

GPU software PWM is not set below 30%. If a writable fan that reports RPM stays at or below 200 RPM while CPU or GPU temperature is at least 85 °C for 5 seconds, Mars drops back to BIOS/EC control. A writable channel that cannot report RPM is not treated as stalled; if its related temperature stays at least 90 °C for 5 seconds, control is released the same way.

## Library



The scanner reads installed games without keeping the store clients running:

- Steam, from `libraryfolders.vdf`, skipping redistributables and SteamVR.
- Epic Games, from launcher manifests.
- GOG Galaxy, from the registry.
- EA App, from the installed-app manifest.
- Ubisoft Connect, from the registry.

Covers come from the Steam CDN when an app id is known, then from the Steam store search, then from SteamGridDB if you save an API key. Keys are stored with Windows DPAPI for the current user (`dpapi:` in `config.json`). A failed encryption keeps the previous protected value.

Session stats ignore the first 8 seconds and ignore samples taken while the game is only held by the Alt-Tab timer. Averages are weighted by sample seconds. The open session is saved periodically so a crash does not drop the whole run.

## Network and privacy

Mars does not upload hardware identifiers, sensor logs, or usage analytics.

It does make these requests:

- A GitHub Releases check about 60 seconds after the control panel opens, then about every 30 minutes, and only while no game session is active. You can also check from **About**.
- Cover art while the library is loading or refreshing.
- The official GPU preset catalog, or a URL you set, when you ask for suggestions.
- An AI endpoint only if you configure one.

Crash reports are not sent automatically. **About** can open a prefilled GitHub issue. Paths, `dpapi:` blobs, and bearer tokens are redacted first. The debug log stays on disk at `%LocalAppData%\Mars FPS Monitor\oc_debug.log`.

## Requirements

| Component | Required | Notes |
| :--- | :--- | :--- |
| Windows | 10 or 11, 64-bit, build 19041 or newer | Setup targets x64. |
| Rights | Administrator | Requested by `app.manifest`. |
| .NET | 8 Desktop Runtime, x64 | Setup installs it silently if it is missing. |
| Visual C++ | 2015–2022 redistributable, x64 | Setup installs it silently if it is missing. |
| PawnIO | Optional | Needed for MSR CPU temperatures and desktop Super I/O fan writes. ACPI is the fallback for CPU temperature. |
| RTSS 7.3.7 or newer | Optional | Needed for exclusive-fullscreen OSD. Setup downloads and installs it silently when it is absent. |

## Installation

The Inno Setup 6 installer is `MarsFPSMonitor_Setup_v3.0.0.exe`. It asks for administrator rights, then:

1. Closes `FPSOverlay.exe` if it is running.
2. Stops and deletes a Mars background service if one exists (`R0FPSOverlay`, `FPSOverlay`, `MarsFPSMonitor`, or a service whose binary is this app). PawnIO and RTSS are left installed.
3. Removes the previous program folder.
4. Installs .NET 8 Desktop Runtime, the VC++ redistributable, and RTSS only when they are missing.
5. Copies the app to `Program Files\Mars FPS Monitor` and can add a desktop shortcut.

Settings, profiles, and the library cache live under `%LocalAppData%\Mars FPS Monitor` and are not deleted by this wipe. On first launch, copies of those files that still sit beside `FPSOverlay.exe` are moved there after a successful copy.

After setup, the app starts unless you clear the finish checkbox. The release-notes window is that first launch.

## Files

| File | Role |
| :--- | :--- |
| `config.json` | Overlay, language, selected GPU, OC mode, fan mode, and protected API keys. |
| `oc_profiles.json` | Thermal bands and manual profiles. |
| `fan_curves.json` | Silent, Balanced, Performance, and custom curves. |
| `library_cache.json` | Discovered games. |
| `library_stats.json` | Per-game FPS and temperature history. |
| `oc_debug.log` | Sensor, OC, and fan diagnostics. |
| `show-whats-new` | Written by Setup. Removed after the notes are accepted. |

`ReleaseNotesSeenVersion` inside `config.json` records the notes you already accepted.

## Build

```powershell
dotnet build FPSOverlay.sln -c Release
dotnet test FPSOverlay.sln -c Release
```

Publish the framework-dependent payload, then compile the installer with Inno Setup 6:

```powershell
dotnet publish FPSOverlay.csproj -c Release -r win-x64 --self-contained false `
  -p:PublishReadyToRun=true -p:DebugType=none -p:DebugSymbols=false `
  -o publish\win-x64

.\build-installer.ps1
```

The script writes `dist\MarsFPSMonitor_Setup_v3.0.0.exe`. Authenticode signing runs only when `MARS_SIGN_TOOL` and `MARS_SIGN_CERT` are set. `MARS_SIGN_PASSWORD` is optional. The default timestamp server is DigiCert.

Keep `AppInfo.Version`, `FPSOverlay.csproj` `<Version>`, and `MyAppVersion` in `installer.iss` on the same number.

## Tests

`FPSOverlay.Tests` covers the behavior that is unsafe to guess at:

- Overclock band changes, the 4 °C upgrade gate, the 5-second cooldown, and fail-closed hotspot handling.
- Clock and fan restore on startup, exact GPU name matching, and the 30% GPU PWM floor.
- Fan stall and unverified-RPM watchdog rules.
- Game detection and the 10-second exit hold.
- AI clamp ranges.
- Library session averages and the 8-second warmup.
- DPAPI round-trip, crash-report redaction, and LocalAppData migration.
- The same UI string keys in every language, version consistency, and v3.0 release-note copy.

## Troubleshooting

| What you see | What to check |
| :--- | :--- |
| FPS is `-1` or the UI says administrator is required | Start the installed shortcut, or run `FPSOverlay.exe` as administrator. |
| No OSD in exclusive fullscreen | RTSS is missing or not running. Run Setup again, or install RTSS yourself. Borderless and windowed games use the Mars HUD and do not need RTSS. |
| CPU temperature stays at 0 °C | Install PawnIO and restart Mars. If PawnIO cannot load, ACPI zones are the fallback and some boards do not expose a useful CPU zone. |
| GPU temperature or overclock targets the wrong adapter | On **Display**, select the discrete GPU. Laptops with an iGPU and a dGPU default to NVIDIA, then AMD, then Intel, when the saved name is empty or no longer present. |
| Intel Arc power limit looks wrong | v3.0 reads the limit from the card instead of showing 150 W for every Arc GPU. A driver that does not expose the limit can still omit it. |
| AMD offset does not match what you set | Offsets are applied from the driver default, not stacked on the last write. Confirm the selected GPU is the AMD adapter you intend to tune. |
| Fan sliders are disabled | The board or laptop EC did not expose a writable channel. Desktops often need PawnIO for Super I/O. Locked laptops stay read-only. |
| A second launch used to open another window | v3.0 activates the running instance, including when it is in the tray. |
| You need a log | **About → Open debug log**, or open `%LocalAppData%\Mars FPS Monitor`. |

Report a sensor or overclock problem on [GitHub Issues](https://github.com/emirttac/Mars-FPS-Monitor/issues) and include the GPU, CPU, laptop or desktop model, and whether PawnIO is installed.

## Languages

| Code | Language | Code | Language |
| :---: | :--- | :---: | :--- |
| `EN` | English | `FR` | Français |
| `TR` | Türkçe | `PT` | Português |
| `AZ` | Azərbaycan | `BR` | Português (Brasil) |
| `DE` | Deutsch | `RU` | Русский |
| `ES` | Español | `ZH` | 简体中文 |

The release-notes window has the same list. The language you pick there is saved and used for the rest of the app.

## License and credits

Mars FPS Monitor is released under the [MIT License](LICENSE). Copyright (c) 2026 [emirttac](https://github.com/emirttac).

- [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL 2.0) for sensors and Super I/O.
- [TraceEvent](https://github.com/microsoft/perfview) (MIT) for ETW.
- [NvAPIWrapper.Net](https://github.com/falahati/NvAPIWrapper) (MIT) for NVIDIA.
- [Windows Community Toolkit](https://github.com/CommunityToolkit/WindowsCommunityToolkit) notifications (MIT).
- [PawnIO](https://pawnio.eu/) for ring-0 MSR and Super I/O access.
- [RivaTuner Statistics Server](https://www.guru3d.com/) by Unwinder for exclusive-fullscreen OSD.

Issues: [github.com/emirttac/Mars-FPS-Monitor/issues](https://github.com/emirttac/Mars-FPS-Monitor/issues)  
YouTube: [@BiAltTab](https://www.youtube.com/@BiAltTab)  
Instagram: [@emirttac](https://www.instagram.com/emirttac/)
