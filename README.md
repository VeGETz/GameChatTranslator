# GameChatTranslator

A Windows desktop overlay that reads foreign-language chat from your game via screen OCR and translates it on the fly. Drag a bounding box over the in-game chat area, lock it, and a small panel below the box shows the translated messages — one line per message, newest at the bottom.

> **This project was built entirely by [Claude Code](https://claude.com/claude-code) (Anthropic).**
> The entire codebase — architecture, WPF UI, OCR/translation pipeline, settings, packaging, CI — was generated, debugged, and iterated on through a conversational session with Claude. No human-authored code is in this repo.

---

## ⚠️ Anti-cheat safety & "use at your own risk"

- **No game process is touched.** This app does **not** read, write, or attach to any game's memory, DLLs, network traffic, or window handles. It does not inject, it does not hook, it does not call `OpenProcess` / `ReadProcessMemory` / `WriteProcessMemory` / `SetWindowsHookEx` / anything similar. You can verify: search the source tree for any of those — you will find none.
- **All it does is: grab pixels from your own desktop (GDI `BitBlt` on the desktop DC), run them through the built-in Windows OCR engine, and send the resulting text to a translation API.** From the game's perspective, this is indistinguishable from running any screen-recording, screenshot, or streaming tool.
- **That said, anti-cheat systems are proprietary, opaque, and constantly changing.** No one can guarantee that any third-party software running alongside a game will not be flagged. **You use this software entirely at your own risk.** The author(s) accept no responsibility for bans, warnings, account actions, performance issues, data loss, or any other consequence of using it. If you are not comfortable with that, do not use it.

---

## Features

- **Resizable, always-on-top overlay** you drag over the game's chat box. Lock it to make the capture region click-through so it doesn't block the game.
- **Windows OCR** (built-in, per-language). No Tesseract, no model downloads.
  - **In-app language installer**: pick a language (Thai, Japanese, Korean, Chinese, Russian, etc.) → click Install → the app launches an elevated PowerShell that runs `Add-WindowsCapability` for you. UAC handled, status displayed, auto-refresh when done. No keyboard layout added, no display language changed, no reboot.
- **Translators** — dropdown picker with dynamic UI (only the selected translator's settings are shown):
  - **Google Translate** (free, unofficial endpoint — no API key, may be rate-limited)
  - **Google Cloud Translation v2** (official, API key, 500k chars/mo free tier)
  - **LLM** — unified OpenAI-shape client covering four provider modes:
    - **OpenAI-compatible Chat Completions** — OpenAI, Ollama, LM Studio, OpenRouter, Groq, Together.ai, xAI, Mistral, LocalAI, vLLM, and anything else that speaks OpenAI's `/chat/completions`.
    - **OpenAI Responses** — OpenAI's newer `/v1/responses` endpoint.
    - **Azure Chat Completions** (legacy) — `{resource}.openai.azure.com/openai/deployments/{name}/chat/completions` with `api-key` header.
    - **Azure Responses** (modern, required for GPT-5 family) — `{resource}.cognitiveservices.azure.com/openai/responses` with `Authorization: Bearer`.
  - **Quick presets** for common providers (OpenAI, OpenAI Responses, Azure Responses, Azure legacy, Ollama, LM Studio, OpenRouter, Groq) — one click fills in base URL and model.
  - API keys are DPAPI-encrypted on disk (Windows CurrentUser scope).
- **Low latency**:
  - Per-frame pixel hashing (xxHash64) — unchanged frames skip OCR and translation entirely.
  - Per-line OCR-text equality check — unchanged lines skip translation.
  - LRU translation cache — repeated phrases (common in chat) cost zero API calls.
- **Sticky messages**: fast-scrolling chat stays readable. Each parsed line lingers on the overlay for a configurable TTL (default 3s) after it stops appearing in the capture region.
- **Per-player-line parsing**: a configurable regex pulls `name` and `text` groups out of each OCR line. Only `text` is translated; `[PlayerName]:` is preserved verbatim.
- **Blocked-phrase filter**: a list of case-insensitive substrings; any OCR line containing one is dropped before translation. OCR-error tolerant (unlike a strict regex filter), perfect for hiding voice-line captions, hero-change notifications, killfeed, or "joined channel" spam.
- **Global hotkey** to toggle translation on/off without alt-tabbing.

---

## Requirements

- **Windows 10/11 x64**.
- **.NET 10 Desktop Runtime** (only if you use the framework-dependent build; the self-contained release exe includes everything).
- **At least one Windows OCR language** installed for the language you want to read. The app's built-in installer can set this up for you with one click.

## Download

Pre-built releases: https://github.com/VeGETz/GameChatTranslator/releases

Download `ScreenTranslator.exe`, put it anywhere, double-click. Settings are stored at `%APPDATA%\ScreenTranslator\settings.json`.

---

## Installing Windows OCR languages

### Option A — from inside the app (recommended)

1. Click **Add OCR language...** next to the OCR source language dropdown.
2. Pick a language (rows show "Installed" or "Not installed").
3. Click **Install selected**. A UAC prompt appears → approve.
4. An elevated PowerShell window opens, runs `Add-WindowsCapability`, shows progress, then asks you to press Enter to close.
5. The dialog auto-refreshes and the selected language now shows "Installed" in green.

No keyboard layout is added, no display language is changed, no reboot is required.

### Option B — manual PowerShell (if you prefer)

Run PowerShell **as Administrator**:

```powershell
# Thai
Add-WindowsCapability -Online -Name "Language.OCR~~~th-TH~0.0.1.0"

# Japanese / Korean / Chinese (Simplified / Traditional)
Add-WindowsCapability -Online -Name "Language.OCR~~~ja-JP~0.0.1.0"
Add-WindowsCapability -Online -Name "Language.OCR~~~ko-KR~0.0.1.0"
Add-WindowsCapability -Online -Name "Language.OCR~~~zh-CN~0.0.1.0"
Add-WindowsCapability -Online -Name "Language.OCR~~~zh-TW~0.0.1.0"

# Russian / Vietnamese / Spanish / French / German / Italian / etc.
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
   - Pick the **OCR source language** (e.g. `th-TH` for Thai, `ja-JP` for Japanese). If it's not listed, click **Add OCR language...** and install it.
   - Set the **Target language** (e.g. `en`).
   - Pick a **Translator** (Google free works out of the box; for LLM, click a preset button then fill in your API key).
   - Tune **Interval (ms)** — lower = more responsive, higher = less CPU / fewer API calls.
4. Check **Enabled**, then **Lock overlay (click-through)**. The capture region becomes transparent to mouse clicks so the game isn't blocked.
5. Translated messages appear in the bottom panel, newest at the bottom, each on its own line. They linger for a few seconds after scrolling out so you have time to read them.
6. Press the global **Hotkey** (default `Ctrl+Alt+T`) to toggle translation on/off without alt-tabbing.

### Chat parsing (Overwatch, League, etc.)

- **Chat line regex** (default): `^\[(?<name>[^\]]+)\]\s*:\s*(?<text>.*)$` — matches `[PlayerName]: message`.
- **Skip player names** (on by default): only the `text` group is translated; `[Name]:` is preserved.
- **Do not translate when line contains**: a newline-separated list of substrings. Any OCR line containing one (case-insensitive) is dropped. Ideal for filtering voice lines, hero changes, and killfeed that share the chat area with real chat.

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

### LLM translator cheat sheet

The LLM mode covers essentially every OpenAI-shape endpoint in existence. Pick a preset, then override the model if you want something else.

| Provider | Preset | Base URL | Model field |
|---|---|---|---|
| OpenAI | `OpenAI` | `https://api.openai.com/v1` | `gpt-4o-mini`, `gpt-4o`, etc. |
| OpenAI (Responses API) | `OpenAI (Responses)` | `https://api.openai.com/v1` | same |
| Azure OpenAI (modern, GPT-5) | `Azure (Responses)` | `https://{resource}.cognitiveservices.azure.com` | deployment name |
| Azure OpenAI (legacy) | `Azure (legacy)` | `https://{resource}.openai.azure.com` | deployment name |
| Ollama (local) | `Ollama` | `http://localhost:11434/v1` | `llama3.2`, `qwen2.5`, etc. |
| LM Studio (local) | `LM Studio` | `http://localhost:1234/v1` | whatever you loaded |
| OpenRouter | `OpenRouter` | `https://openrouter.ai/api/v1` | `anthropic/claude-3.5-sonnet`, etc. |
| Groq | `Groq` | `https://api.groq.com/openai/v1` | `llama-3.3-70b-versatile`, etc. |

Anthropic's native Messages API isn't OpenAI-shape, so it's not directly supported. Use OpenRouter with `anthropic/claude-*` models to route Claude through the LLM translator.

For local models (Ollama / LM Studio) the API key can be left blank.

The default system prompt is tuned for live gaming chat — preserves proper nouns, hero/item names, outputs only the translation with no prose. You can edit it freely; use `{source}` and `{target}` placeholders for the language pair.

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

Output:
`src/ScreenTranslator/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/ScreenTranslator.exe`

### Automated releases

Pushing a tag that matches `v*.*.*` triggers the GitHub Actions release workflow (`.github/workflows/release.yml`) which publishes a single-file exe and creates a GitHub Release with the asset attached:

```bash
git tag v0.3.0
git push origin v0.3.0
```

---

## Tech

- **.NET 10**, WPF (`net10.0-windows10.0.19041.0` TFM for WinRT interop)
- **`Windows.Media.Ocr`** for OCR, **GDI `BitBlt`** for screen capture
- **`Windows.Security.Cryptography.DataProtection`** (DPAPI) for API key storage
- **`CommunityToolkit.Mvvm`** for MVVM, **`Microsoft.Extensions.DependencyInjection`** for wiring
- **`RegisterHotKey`** / `WM_HOTKEY` for global hotkeys
- **`xxHash64`** (System.IO.Hashing) for fast frame-change detection
- Elevated child processes via `Verb = "runas"` for in-app OCR language installation
- GitHub Actions (`windows-latest` runner) for tagged-release automation

---

## License

MIT. See [LICENSE](LICENSE).

---

## Disclaimer (repeated because it matters)

> **Built by an AI. Use at your own risk.** The software does not touch game processes, but no one can guarantee how any anti-cheat system will classify third-party software running alongside a game. Nothing here is a legal or technical guarantee. If using this could put something you care about at risk (your account, your rank, your hardware), don't use it.
