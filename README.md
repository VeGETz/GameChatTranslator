# GameChatTranslator

A Windows desktop overlay that reads foreign-language chat from your game via screen OCR and translates it on the fly. Drag a bounding box over the in-game chat area, lock it, and a small panel below the box shows the translated messages.

> **This project was built entirely by [Claude Code](https://claude.com/claude-code) (Anthropic).**
> The entire codebase — architecture, WPF UI, OCR/translation pipeline, settings, packaging — was generated, debugged, and iterated on through a conversational session with Claude. No human-authored code is in this repo.

---

## ⚠️ Anti-cheat safety & "use at your own risk"

- **No game process is touched.** This app does **not** read, write, or attach to any game's memory, DLLs, network traffic, or window handles. It does not inject, it does not hook, it does not call `OpenProcess` / `ReadProcessMemory` / `WriteProcessMemory` / `SetWindowsHookEx` / anything similar. You can verify: search the source tree for any of those — you will find none.
- **All it does is: grab pixels from your own desktop (GDI `BitBlt` on the desktop DC), run them through the built-in Windows OCR engine, and send the resulting text to a translation API.** From the game's perspective, this is indistinguishable from running any screen-recording, screenshot, or streaming tool.
- **That said, anti-cheat systems are proprietary, opaque, and constantly changing.** No one can guarantee that any third-party software running alongside a game will not be flagged. **You use this software entirely at your own risk.** The author(s) accept no responsibility for bans, warnings, account actions, performance issues, data loss, or any other consequence of using it. If you are not comfortable with that, do not use it.

---

## Features

- **Resizable, always-on-top overlay** you drag over the game's chat box. Lock it to make the capture region click-through so it doesn't block the game.
- **Windows OCR** (built-in, per-language). No Tesseract, no model downloads. Add languages with a single `Add-WindowsCapability` PowerShell command — no keyboard layout added, no display language changed.
- **Translators**:
  - Google Translate (free, unofficial endpoint — no API key, may be rate-limited)
  - Google Cloud Translation v2 (official, API key, 500k chars/mo free tier)
  - API keys are DPAPI-encrypted on disk (Windows CurrentUser scope).
- **Low latency**:
  - Per-frame pixel hashing (xxHash64) — unchanged frames skip OCR and translation entirely.
  - Per-line OCR-text equality check — unchanged lines skip translation.
  - LRU translation cache — repeated phrases (common in chat) cost zero API calls.
- **Sticky messages**: fast-scrolling chat stays readable. Each parsed line lingers on the overlay for a configurable TTL (default 3s) after it stops appearing in the capture region.
- **Per-player-line parsing**: a configurable regex pulls `name` and `text` groups out of each OCR line. Only `text` is translated; `[PlayerName]:` is preserved verbatim.
- **Line filter**: optionally drop any OCR line that doesn't match the regex — useful for hiding voice-line captions, hero-change notifications, killfeed, etc. that share the chat area.
- **Global hotkey** to toggle translation on/off without alt-tabbing.

---

## Requirements

- **Windows 10/11 x64**.
- **.NET 10 Desktop Runtime** (only if you use the framework-dependent build; the self-contained release exe includes everything).
- **At least one Windows OCR language** installed for the language you want to read. See below.

## Download

Pre-built releases: https://github.com/VeGETz/GameChatTranslator/releases

Download `ScreenTranslator.exe`, put it anywhere, double-click. Settings are stored at `%APPDATA%\ScreenTranslator\settings.json`.

---

## Installing Windows OCR languages

Run PowerShell **as Administrator**. This adds the OCR capability **only** — no keyboard layout, no display language change, no reboot required.

```powershell
# Thai
Add-WindowsCapability -Online -Name "Language.OCR~~~th-TH~0.0.1.0"

# Japanese
Add-WindowsCapability -Online -Name "Language.OCR~~~ja-JP~0.0.1.0"

# Korean
Add-WindowsCapability -Online -Name "Language.OCR~~~ko-KR~0.0.1.0"

# Chinese (Simplified / Traditional)
Add-WindowsCapability -Online -Name "Language.OCR~~~zh-CN~0.0.1.0"
Add-WindowsCapability -Online -Name "Language.OCR~~~zh-TW~0.0.1.0"

# Russian
Add-WindowsCapability -Online -Name "Language.OCR~~~ru-RU~0.0.1.0"
```

List what's installed / available:
```powershell
Get-WindowsCapability -Online | Where-Object Name -Like "Language.OCR*"
```

Remove one:
```powershell
Remove-WindowsCapability -Online -Name "Language.OCR~~~ko-KR~0.0.1.0"
```

After installing, click **Refresh** next to the OCR language dropdown in the app — no restart needed.

---

## How to use

1. Launch the app. Two windows appear: the **Control Panel** and a small transparent **Overlay** with a red dashed border.
2. Drag the overlay over your game's chat area. Drag the bottom-right red handle to resize. Drag the bottom edge of the capture region to change the split between capture area and translation panel.
3. In the Control Panel:
   - Pick the **OCR source language** (e.g. `th-TH` for Thai, `ja-JP` for Japanese).
   - Set the **Target language** (e.g. `en`).
   - Pick a **Translator** (Google free works out of the box).
   - Tune **Interval (ms)** — lower = more responsive, higher = less CPU / fewer API calls.
4. Check **Enabled**, then **Lock overlay (click-through)**. The capture region becomes transparent to mouse clicks so the game isn't blocked.
5. Translated messages appear in the bottom panel, newest at the bottom, each on its own line.
6. Press the global **Hotkey** (default `Ctrl+Alt+T`) to toggle translation on/off without alt-tabbing.

### Chat parsing (Overwatch, League, etc.)

- **Chat line regex** (default): `^\[(?<name>[^\]]+)\]\s*:\s*(?<text>.*)$` — matches `[PlayerName]: message`.
- **Skip player names** (on by default): only `text` is translated; `[Name]:` is preserved.
- **Only translate lines matching the regex** (off by default): drop everything that doesn't look like a chat message. Recommended for Overwatch to filter out voice-line captions, hero changes, killfeed, etc.

Common regex variants:
```
# Default
^\[(?<name>[^\]]+)\]\s*:\s*(?<text>.*)$

# With an all-chat / team-chat prefix: [ALL] [Name]: text
^(?:\[(?:ALL|TEAM|MATCH)\]\s*)?\[(?<name>[^\]]+)\]\s*:\s*(?<text>.*)$

# PlayerName: text (no brackets)
^(?<name>[^:]+):\s*(?<text>.*)$

# <PlayerName> text
^<(?<name>[^>]+)>\s*(?<text>.*)$
```

---

## Build from source

```bash
git clone https://github.com/VeGETz/GameChatTranslator.git
cd GameChatTranslator
dotnet build
dotnet run --project src/ScreenTranslator
```

### Publish a single-file self-contained exe

```bash
dotnet publish src/ScreenTranslator -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

Output at:
`src/ScreenTranslator/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/ScreenTranslator.exe`

---

## Tech

- **.NET 10**, WPF (`net10.0-windows10.0.19041.0` TFM for WinRT interop)
- **`Windows.Media.Ocr`** for OCR, **GDI `BitBlt`** for screen capture
- **`Windows.Security.Cryptography.DataProtection`** (DPAPI) for API key storage
- **`CommunityToolkit.Mvvm`** for MVVM, **`Microsoft.Extensions.DependencyInjection`** for wiring
- **`RegisterHotKey`** / `WM_HOTKEY` for global hotkeys
- **`xxHash64`** (System.IO.Hashing) for fast frame-change detection

---

## License

MIT. See [LICENSE](LICENSE).

---

## Disclaimer (repeated because it matters)

> **Built by an AI. Use at your own risk.** The software does not touch game processes, but no one can guarantee how any anti-cheat system will classify third-party software running alongside a game. Nothing here is a legal or technical guarantee. If using this could put something you care about at risk (your account, your rank, your hardware), don't use it.
