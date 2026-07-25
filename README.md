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

<img width="1220" height="720" alt="Screenshot 2026-07-26 021438" src="https://github.com/user-attachments/assets/a442a21e-c42a-4395-8824-9df15ee4d727" />

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

## AI Overclock Assistant — what “AI” actually means here

Short answer: **by default it is not ChatGPT talking to your GPU.**  
“AI Overclock Assistant” is the **name of the feature** in the UI. Under the hood it is a **recommendation pipeline** that builds conservative **Eco / Performance / Extreme** suggestions, then **always** runs them through a local **safety clamp** before you see them. Nothing is written to the GPU until **you** save.

### How suggestions are produced (priority order)

1. **`gpu_presets.json` (remote catalog — primary path)**  
   Mars downloads a public JSON preset file (GitHub RAW by default: the `gpu_presets` catalog). It fuzzy-matches your detected GPU name (so a more specific key like “RTX 3060 Ti” wins over “RTX 3060”), then builds Eco / Performance / Extreme recommendations from that entry.  
   Source reported to the app: `remote_presets`.

2. **Optional HTTP AI API (only if you configure one)**  
   If remote presets did not match / failed **and** you set `AiOcApiEndpoint` in config, Mars can POST a hardware snapshot to **your** backend (optional Bearer token / chat envelope). This path is **off unless you turn it on**.  
   Source: `api`.

3. **`local-conservative-v1` (built-in offline fallback)**  
   If the network is down, the catalog misses your card, or no API is set, Mars uses a **deterministic local engine** baked into the app: `local-conservative-v1`. Same Eco / Performance / Extreme shape, deliberately shy numbers, still clamped. Works offline with no cloud dependency.  
   Source: `local_fallback` / message `local-conservative-v1`.

### What users should know

- Default install → **presets JSON first**, then **local-conservative-v1**. No mystery cloud LLM required.
- Empty `GpuPresetsUrl` in config → skip remote fetch (local / optional API only).
- Every path ends in **`AiOcSafetyClamp`** (hard software ceilings). Wild numbers get cut down.
- Splash can prefetch suggestions so the Overclock tab already says they’re ready.

Nothing gets slammed onto your GPU until **you** save. The pipeline proposes. You decide.

<img width="500" height="323" alt="Screenshot 2026-07-26 022035" src="https://github.com/user-attachments/assets/39bfd61f-f0ec-4472-996d-c38dcb0336e9" />

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

<img width="440" height="540" alt="Screenshot 2026-07-26 022511" src="https://github.com/user-attachments/assets/47f67e8d-0bb0-4cb4-aa4e-5b733d916dbd" />

### GPU overclock control
- **Off** — sensors live, no writes
- **Auto** — temperature-band profiles with hysteresis / cooldown
- **Manual** — fixed curated profile
- Backends: **NVIDIA (NVAPI)** · **AMD (ADL)** · **Intel Arc (IGCL)** when available
- Create / edit / import / export your own profiles
- Fail-closed toward Safe/Off when sensors go weird or hotspot goes critical

<img width="1220" height="720" alt="Screenshot 2026-07-26 022632" src="https://github.com/user-attachments/assets/3101afa6-57b7-446e-aaff-fa4b431374ff" />

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
| `AiOc*.cs` / `GpuRemotePreset*.cs` | Recommendation pipeline, presets fetch, safety clamp |
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

Relevant AI / preset fields:

| Field | Meaning |
|---|---|
| `GpuPresetsUrl` | URL to `gpu_presets.json` (empty = no remote catalog) |
| `GpuPresetsTimeoutSeconds` | Download timeout |
| `AiOcApiEndpoint` | Optional custom AI HTTP API (empty = unused) |
| `AiOcApiKey` | Optional Bearer token for that API |

---

## Safety (overclock)

OC can stress silicon. Mars uses **software ceilings**, local clamps on every suggestion path (`gpu_presets` / API / `local-conservative-v1`), and fail-closed thermal logic. That still isn’t a warranty, a lab, or a substitute for decent cooling. Auto/Manual OC = your call, your risk.

---

## Links

- Repo: [emirttac/Mars-FPS-Monitor](https://github.com/emirttac/Mars-FPS-Monitor)
- GitHub: [emirttac](https://github.com/emirttac)
- Instagram: [@emirttac](https://www.instagram.com/emirttac/)
- YouTube: [@BiAltTab](https://www.youtube.com/@BiAltTab)

---

## License

See repository license (if published). Third-party libraries keep their own licenses (LibreHardwareMonitor, TraceEvent, NvAPIWrapper, etc.).
