# Pix

[中文文档](./README.zh-CN.md)

Pix is a Windows desktop companion for the [pi coding agent](https://github.com/badlogic/pi-mono). It bundles two parts:

- **pi-web** — a local web UI for pi (forked from [agegr/pi-web](https://github.com/agegr/pi-web)): session browsing, real-time chat, model configuration, skill management, and project file preview in the browser.
- **Pix Launcher** — a native Windows control plane that runs pi-web for you: it lives on your desktop as a floating ball, starts the service in one click, and shows provider balances and subscription quota in a popover panel.

![Pi Web in the browser](https://raw.githubusercontent.com/Acidmoon/Pix/dev/docs/screenshot2.png)

## Pix Launcher

The launcher is what makes Pix different from plain pi-web. It is a small WinForms app that stays out of the way until you need it.

- **Floating ball**: a draggable, always-on-top dark orb. Click it and a satellite panel fades in beside it — the ball itself never repaints or moves during the transition.
- **One-click service control**: start/stop pi-web on a random loopback port, wait for the health check, then open the UI in an isolated Edge/Chrome `--app` window.
- **Browser docking**: after the app window opens, the ball glides to its right edge and follows it magnetically. Drag the ball away to detach — it remembers where you left it.
- **Balance & quota panel**: see at a glance what your configured providers have left —
  - DeepSeek: total / topped-up / granted balance per currency
  - MiniMax Coding Plan (CN & global): remaining 5-hour window and 7-day window with reset times
- **Service insight**: PID, port, and live AgentSession count; a system tray menu mirrors the controls.
- **Quiet by design**: smooth 150–240ms animations, hidden scrollbars, a persistent dedicated browser profile (extensions and logins survive restarts instead of re-onboarding every launch), and an application icon that matches the orb.

## Requirements

- Windows 10/11
- [Node.js](https://nodejs.org/) available on `PATH`
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

Then open [http://localhost:30141](http://localhost:30141).

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
Edge/Chrome --app window (dedicated persistent profile)
```

- The two processes share no code; the launcher only talks HTTP to pi-web.
- A random `PI_WEB_LAUNCHER_TOKEN` is generated per launch. Without it, the `/api/launcher/*` endpoints return 404 — plain `pi-web` runs (CLI, npx, dev server) expose no privileged surface.
- Provider credentials are resolved through pi's own auth storage and used only in server-side requests to the matching official provider API; they are never returned to the launcher or the browser.

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
