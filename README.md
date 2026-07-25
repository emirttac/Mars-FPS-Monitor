# Mars FPS Monitor

**Mars FPS Monitor** is what happens when a modern overlay stops pretending it’s 2012.

Forget the grey boxes, comic-sans energy meters, and “pro” tools that look like they were designed in Paint between class periods. Mars is a **clean, fast, gamer-first HUD** for Windows — live FPS, frametime, temps, clocks, memory, and optional GPU overclock control — wrapped in a control panel that actually feels like a product, not a driver utility from another decade.

No game injection. No sketchy hooks. FPS comes straight from the Windows kernel (ETW / DxgKrnl Present events). Sensors come from LibreHardwareMonitor. You stay in the match; Mars stays on top.

| | |
|---|---|
| **Version** | 1.0.0 |
| **Platform** | Windows 10/11 · x64 |
| **Stack** | .NET 8 · WPF |
| **Author** | [emirttac](https://github.com/emirttac) |

---

## Why Mars (and not “yet another Afterburner clone”)

Classic overlays got the job done. They also aged like milk: dense tables, tiny fonts, zero personality, and UIs that fight you every time you want to change a color.

**Mars flips that:**

- **Brand-first, atmosphere-first UI** — graphite surfaces, Mars orange accent (`#F24C1D`), soft motion, intentional hierarchy. Not a spreadsheet wearing a dark theme.
- **One job per screen** — Overlay, Sensors, Display, Overclock, About. No kitchen-sink chaos.
- **Overlay profiles with actual style** — from invisible-minimal to neon glass to a vertical Tower stack that nods at Afterburner without copying its museum UI.
- **Math-built color picker** — real HSV wheel, no pixel sniffing, instant accent on the HUD.
- **Tray-native workflow** — splash that feels premium, panel that fades in, overlay that click-throughs when locked.

If you’ve ever opened a legacy monitor and thought “this can’t be the best we have in 2026” — yeah. That’s the gap Mars fills.

---

## AI Overclock Assistant (optional, but spicy)

Mars isn’t only “show me numbers.” It includes an **AI Overclock Assistant** that:

- Reads a hardware snapshot (GPU model, temps, vendor, current limits)
- Pulls **conservative Eco / Performance / Extreme** style recommendations
- Can use a remote GPU preset catalog and/or an optional HTTP AI endpoint
- **Always** runs suggestions through a local **safety clamp** before the UI trusts them
- Prefetches during splash so the panel can greet you with “AI önerileri hazır” instead of a cold empty box

Nothing gets slammed onto your GPU until **you** save. The AI proposes. You decide. The clamp keeps the wild numbers in the adult swimming lane.

---

## Features

### Live overlay metrics
- **FPS** from kernel present / flip / blit events (foreground process)
- **Frametime (ms)** + **1% low**
- **CPU / GPU** temperature, load, clock
- **RAM / VRAM** usage
- GPU name + live **OC status** on the HUD
- Click-through when locked · drag when unlocked · always on top

### Overlay profiles
| Profile | Vibe |
|---|---|
| Classic Minimalist | Clean text, zero chrome |
| Gamer Panel | Soft dark panel |
| Steam Deck Style | Dense deck-like card |
| Advanced Performance HUD | Multi-block layout + frametime graph |
| Compact Pill | Rounded pill HUD |
| Neon Glass | Accent-border glass |
| Tower | Vertical Afterburner-style stack |

### Appearance
- Custom accent via HSV color wheel
- Font size / family
- Position presets or free drag
- Padding + lock toggle

### GPU overclock control
- **Off** — sensors live, no writes
- **Auto** — temperature-band profiles with hysteresis / cooldown
- **Manual** — fixed curated profile
- Backends: **NVIDIA (NVAPI)** · **AMD (ADL)** · **Intel Arc (IGCL)** when available
- Create / edit / import / export your own profiles
- Fail-closed toward Safe/Off when sensors go weird or hotspot goes critical

### Localization
English, Turkish, Azerbaijani, German, Spanish, French, Portuguese, Brazilian Portuguese, Russian, Chinese (ZH).

---

## Requirements

- Windows 10+ (64-bit)
- **Administrator** rights (ETW FPS + hardware sensors — see `app.manifest`)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64)
- Visual C++ 2015–2022 Redistributable (x64)

The Inno Setup installer detects missing runtimes and installs them during setup.

> GPU overclock needs matching vendor drivers. Overlay + sensors still work without OC support.

---

## Install

1. Grab the latest setup from [Releases](https://github.com/emirttac/Mars-FPS-Monitor/releases).
2. Run `MarsFPSMonitor_Setup_v1.0.0.exe`.
3. Launch from the finish page, Start Menu, or desktop shortcut (admin via manifest).

Upgrades preserve your existing `config.json`. Setup closes a running instance before overwrite.

---

## Build from source

```powershell
dotnet build FPSOverlay.csproj -c Release

dotnet publish FPSOverlay.csproj -c Release -r win-x64 --self-contained false `
  -p:PublishReadyToRun=true -p:DebugType=none -p:DebugSymbols=false `
  -o publish\win-x64
```

### Installer (Inno Setup 6)

```powershell
.\build-installer.ps1
# → dist\MarsFPSMonitor_Setup_v1.0.0.exe
```

---

## Project layout

| Path | Role |
|---|---|
| `App.xaml(.cs)` | Startup, splash, tray, wiring |
| `OverlayWindow.*` | Always-on-top HUD |
| `ControlPanelWindow.*` | Settings (overlay, sensors, display, OC, about) |
| `FpsMonitor.cs` | ETW → FPS / frametime / 1% low |
| `HardwareMonitorManager.cs` | Sensors + overlay text |
| `OverclockManager.cs` | OC modes + thermal loop |
| `*GpuOverclockProvider.cs` | NVIDIA / AMD / Intel backends |
| `AiOc*.cs` | AI assistant client, models, safety clamp |
| `ColorPickerWindow.*` | HSV color wheel |
| `UiStrings.cs` | All UI languages |
| `AppInfo.cs` | Branding / version / links |
| `SOURCE_CODES/` | Curated core `.cs` samples |
| `installer.iss` | Inno Setup script |
| `Assets/` | Logo, fonts |

---

## How FPS works

`FpsMonitor` opens an ETW session on DXGI / D3D9 / DxgKrnl and counts present-related events for the **foreground** process. Frametimes sit in a short queue; 1% low comes from the slowest frames. No admin → ETW fails closed and the UI can show an admin hint instead of fake FPS.

---

## Configuration

`config.json` next to the exe (`OverlayConfig`). First install ships a default; upgrades don’t stomp your file. OC profiles live in `oc_profiles.json`.

---

## Safety (overclock)

OC can stress silicon. Mars uses **software ceilings**, local clamps on AI output, and fail-closed thermal logic. That still isn’t a warranty, a lab, or a substitute for decent cooling. Auto/Manual OC = your call, your risk.

---

## Links

- Repo: [emirttac/Mars-FPS-Monitor](https://github.com/emirttac/Mars-FPS-Monitor)
- GitHub: [emirttac](https://github.com/emirttac)
- Instagram: [@emirttac](https://www.instagram.com/emirttac/)
- YouTube: [@BiAltTab](https://www.youtube.com/@BiAltTab)

---

## License

See repository license (if published). Third-party libraries keep their own licenses (LibreHardwareMonitor, TraceEvent, NvAPIWrapper, etc.).
