# Pix

[中文文档](./README.zh-CN.md) | [日本語](./README.ja.md)

Pix is a Windows desktop companion for the [pi coding agent](https://github.com/badlogic/pi-mono). It bundles two parts:

- **pi-web** — a local web UI for pi (forked from [agegr/pi-web](https://github.com/agegr/pi-web)): session browsing, real-time chat, model configuration, skill management, and project file preview in the browser.
- **Pix Launcher** — a native Windows control plane that runs pi-web for you: it lives on your desktop as a floating ball, starts the service in one click, and shows provider balances and subscription quota in a popover panel.

![Pi Web in the browser](https://raw.githubusercontent.com/Acidmoon/Pix/dev/docs/screenshot2.png)

## Features

- **Pick work back up**: browse previous pi conversations by project without digging through terminal history or session paths.
- **Try different directions safely**: continue from an earlier message or fork a session into a separate route.
- **Work across branches**: switch Git worktrees from the sidebar so new sessions and the Explorer follow the checkout you choose.
- **Chat beside the project**: browse files on the left and preview source, docs, images, audio, and PDFs on the right while the agent works.
- **See session state clearly**: context usage, cost, compaction state, and system prompt details are visible from the top bar.
- **Configure less from the terminal**: manage models, login/API keys, model tests, and skill switches from the web UI.
- **Use the interface in your language**: switch between the supported UI languages from the top bar.

## Pix Launcher

The launcher is what makes Pix different from plain pi-web. It is a small WinForms app that stays out of the way until you need it.

- **Floating ball**: a draggable, always-on-top dark orb. Click it and a satellite panel fades in beside it — the ball itself never repaints or moves during the transition.
- **One-click service control**: start/stop pi-web on a random loopback port, wait for the health check, then open the UI in an isolated Edge/Chrome `--app` window.
- **Browser docking**: after the app window opens, the ball glides to its right edge and follows it magnetically. Drag the ball away to detach — it remembers where you left it.
- **Balance & quota panel**: see at a glance what your configured providers have left —
  - DeepSeek: total / topped-up / granted balance per currency
  - MiniMax Coding Plan (CN & global): remaining 5-hour window and 7-day window with reset times
  - Kimi: remaining 5-hour / 7-day Code quota, plus subscription & gift balance in the detail line
- **Service insight**: PID, port, and live AgentSession count; a system tray menu mirrors the controls.
- **Quiet by design**: smooth 150–240ms animations, hidden scrollbars, a persistent dedicated browser profile (extensions and logins survive restarts instead of re-onboarding every launch), and an application icon that matches the orb.

## Voice input

Pix ships on-device speech-to-text ([SenseVoice](https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17) via sherpa-onnx — fully local, no cloud, no API key). Chinese, English, Japanese, Korean and Cantonese are auto-detected.

- **In the web UI**: click the mic button in the chat input bar, speak, click again to stop — the transcript is inserted at the cursor.
- **System-wide**: toggle "语音输入" in the launcher panel (or the tray menu), then hold **Right Ctrl** in any app, speak, and release — the text is typed at the cursor. Right Alt is a second binding. Long transcripts fall back to clipboard paste.

### Model setup (one-time)

The model (~240 MB) is not bundled and is git-ignored. Download the int8 build of SenseVoice and place these two files under `models/sensevoice-small-int8/` next to the app:

- `model.int8.onnx`
- `tokens.txt`

Sources: [Hugging Face](https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17) · [ModelScope mirror](https://www.modelscope.cn/models/danieldong/sensevoice-small-onnx-quant). To keep the model elsewhere, point the `PIX_VOICE_MODEL_DIR` environment variable at its folder. If the model is missing, the mic button turns amber — click it to see these instructions.

## Requirements

- Windows 10/11
- [Node.js](https://nodejs.org/) 22.19.0 or newer, available on `PATH`
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download) (only for the launcher; building from source needs the .NET 8 SDK instead)

## Getting started

### Option A: just use pi-web

pi-web works standalone on any platform, no launcher required:

```bash
npx @agegr/pi-web@latest
# or
npm install -g @agegr/pi-web
pi-web
```

Then open [http://127.0.0.1:30141](http://127.0.0.1:30141). The CLI will try to open the browser automatically after the server is ready. Pi Web listens on `127.0.0.1` by default.

**Options:**

```bash
pi-web --port 8080              # custom port
pi-web --hostname 0.0.0.0       # expose on a trusted network
pi-web -p 8080 -H 0.0.0.0       # combine options
pi-web --no-open                # do not open the browser automatically

PORT=8080 pi-web                # environment variable is also supported
PI_WEB_HOSTNAME=0.0.0.0 pi-web  # explicit network exposure
PI_WEB_ALLOWED_HOSTS=pi-web.internal pi-web  # allow an exact proxy/custom hostname
PI_WEB_NO_OPEN=1 pi-web         # useful when running as a background service
```

Pi Web has no application-level authentication and can invoke a high-privilege agent. Do not expose it to the internet; only use non-loopback bindings on a trusted network.
API requests accept loopback names, IP literals, the selected bind hostname, and exact comma-separated names in `PI_WEB_ALLOWED_HOSTS`. Configure that variable when a trusted reverse proxy uses a different external hostname.

#### HTTP Proxy

Pi Web reads the standard `HTTP_PROXY`, `HTTPS_PROXY`, and `NO_PROXY` environment variables for server-side model and API requests.

On macOS or Linux:

```bash
HTTP_PROXY=http://127.0.0.1:7890 \
HTTPS_PROXY=http://127.0.0.1:7890 \
NO_PROXY=localhost,127.0.0.1 \
npx @agegr/pi-web@latest
```

On Windows PowerShell:

```powershell
$env:HTTP_PROXY = "http://127.0.0.1:7890"
$env:HTTPS_PROXY = "http://127.0.0.1:7890"
$env:NO_PROXY = "localhost,127.0.0.1"
npx @agegr/pi-web@latest
```

### Option B: build Pix (pi-web + launcher) from source

```powershell
# 1. Install dependencies and build pi-web (production build)
npm install
npm run build

# 2. Build the launcher
dotnet build .\launcher\PixLauncher.csproj -c Release

# 3. Run it — it finds bin\pi-web.js by walking upward from its location
.\launcher\bin\Release\net8.0-windows\PixLauncher.exe
```

If the launcher sits outside the repository, point it at the pi-web directory:

```powershell
$env:PI_WEB_ROOT = "E:\Pix"
```

Prebuilt launcher archives are attached to [GitHub Releases](https://github.com/Acidmoon/Pix/releases). They contain only the launcher — you still need a pi-web build (`npm run build`) for it to drive.

## How it fits together

```
PixLauncher.exe (WinForms, control plane)
   │  loopback HTTP only
   │   GET  /api/health              (readiness, no auth)
   │   GET  /api/launcher/status     (balances & quota, Bearer token)
   │   POST /api/launcher/shutdown   (graceful stop, Bearer token)
   ▼
node bin/pi-web.js (Next.js service)
   ▼
Edge/Chrome --app window (user's existing Default profile)
```

- The two processes share no code; the launcher only talks HTTP to pi-web.
- A random `PI_WEB_LAUNCHER_TOKEN` is generated per launch. Without it, the `/api/launcher/*` endpoints return 404 — plain `pi-web` runs (CLI, npx, dev server) expose no privileged surface.
- Provider credentials are resolved through pi's own auth storage and used only in server-side requests to the matching official provider API; they are never returned to the launcher or the browser.

## Notes

- **Data directory**: Pi Web reads `~/.pi/agent/sessions` by default. Set `PI_CODING_AGENT_DIR` to point at another pi agent directory.
- **Session files**: files are stored as `~/.pi/agent/sessions/<encoded-cwd>/<timestamp>_<uuid>.jsonl`.
- **Model config**: the Models panel reads and writes `models.json` in the pi agent directory. Model lists and defaults come from pi's config.
- **File access**: file browsing and preview are scoped to the selected project directory and working directories that appear in sessions.
- **Git worktrees**: see [Worktrees in Pi Web](./docs/worktrees.md) for when the switcher appears, how new worktrees are created, and what removal does.
- **Forks vs in-session branches**: Fork creates a new `.jsonl` file. "Edit from here" creates another branch inside the same session file.
- **Internationalization**: UI text lives in `lib/locales/zh.json` / `lib/locales/en.json` (keys must stay in sync) and is consumed via `useT()` from `lib/i18n.tsx`.

## Development

```bash
npm install
npm run dev          # pi-web at http://localhost:30141

dotnet build .\launcher\PixLauncher.csproj
dotnet run --project .\launcher\PixLauncher.csproj
```

Common checks:

```bash
node_modules/.bin/tsc --noEmit
npm run lint
```

Do not run `next build` while the dev server is up — it pollutes `.next/`. For a production build on Windows, isolate the build-time profile first (see [docs/launcher.md](./docs/launcher.md)).

## Documentation

- [Pix Launcher details](./docs/launcher.md) — scope, security model, provider adapters
- [Worktrees in pi-web](./docs/worktrees.md) — branch switching in the sidebar
- [AGENTS.md](./AGENTS.md) — architecture notes and development conventions

## License

MIT, same as upstream [pi-web](https://github.com/agegr/pi-web).
