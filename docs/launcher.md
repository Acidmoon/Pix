# Pix Launcher

Pix Launcher is a Windows control plane for the local Pi Web service. It does
not duplicate chat, file browsing, model configuration, skills, or plugins;
those remain in the browser UI.

## Current scope

- Select an unused loopback port and start the built Pi Web package.
- Wait for `/api/health` before opening the UI.
- Stay as a draggable always-on-top floating icon and remember its position.
- Enforce a single launcher instance; later launches activate the existing one.
- Provide a system-tray icon with service controls and a clean exit command.
- Expand left or right according to available screen space while keeping the
  floating icon anchored at its original location.
- Expand on click into a start view or a running-service control panel.
- Show the service PID, port, active AgentSession count, provider balances, and
  subscription quota remaining.
- Open Pi Web in an Edge or Chrome `--app` window backed by a persistent,
  Pix-specific browser profile under `%LOCALAPPDATA%\Pix\BrowserProfile`.
- Request authenticated AgentSession cleanup before stopping the service.
- Place both the service and the dedicated browser in Windows Job Objects so
  their process trees are terminated if graceful shutdown times out or the
  launcher exits. This prevents stale app windows from retaining dead ports.
- Auto-restart the service after an in-place update: consume the web service's
  restart marker (`logs/pix-restart.json`), close the old owned browser window,
  start a fresh process on a new port, and open the replacement URL — no manual
  restart needed.

The shutdown endpoint exists only when the launcher supplies a random
`PI_WEB_LAUNCHER_TOKEN`. Regular CLI and development-server runs do not expose
an unauthenticated service-stop operation.

## Hot update & auto-restart

Pi Web can upgrade its pi kernel (and the whole app) from the browser UI. Because
the kernel is a Node `serverExternalPackage`, an upgrade only takes effect after
the Node process restarts — and the launcher owns the process lifecycle and port.
The flow:

1. The web service runs the upgrade (`npm install` for the kernel, `npm pack` +
   staging for the whole app), streaming progress to the UI over SSE.
2. On success it writes `logs/pix-restart.json` (a `RestartMarker`), gracefully
   stops every AgentSession, then exits.
3. The launcher's 1s status tick notices the exit, **reads and deletes** the
   marker (`PiWebProcessManager.TryConsumeRestartMarker`), and calls
   `RestartAsync()` — a fresh process on a new loopback port — then `OpenBrowser()`
   opens the replacement URL in an Edge or Chrome `--app` window.

Deleting the marker before restarting (plus the launcher's `busy` flag) prevents
restart loops. An exit **without** a marker is treated as a crash: the launcher
shows “stopped” and never auto-restarts.

For a whole-app update the marker carries an `appUpdate` payload. Before
restarting, the launcher runs `node bin/apply-update.js <stagedDir>` to swap the
new `bin/.next/public/next.config.ts/package.json` into the web root (keeping
`node_modules`, `logs`, `.env`) and sync dependencies. If the new build fails the
120s health check, the launcher restores the backup directory once
(`apply-update.js <backupDir> --no-cleanup`) and retries the restart.

The running panel currently includes two official provider adapters:

- DeepSeek calls `GET https://api.deepseek.com/user/balance` and displays total,
  topped-up, and granted balance by currency.
- MiniMax calls the China or global Coding Plan `remains` endpoint and displays
  the remaining 5-hour window plus the 7-day window when that plan enables it.

Credentials are resolved through Pi's `ModelRuntime`, used only in server-side
requests to the matching official provider domain, and never returned to the
launcher.
Provider results are cached for 60 seconds.

## Development

The launcher controls a production Pi Web build. Per repository policy, do not
run a Next.js production build while the development server is active.

```powershell
# Produce .next artifacts when no development server is running. On Windows,
# isolate the build-time profile so Pi resource discovery cannot traverse
# legacy user-profile junctions such as "My Documents".
$buildHome = Join-Path $PWD.Path ".build-home"
$env:HOME = $buildHome
$env:USERPROFILE = $env:HOME
$env:HOMEDRIVE = Split-Path -Qualifier $buildHome
$env:HOMEPATH = $buildHome.Substring(2)
npm run build

# Build and run the Windows launcher from the repository.
dotnet build .\launcher\PixLauncher.csproj
dotnet run --project .\launcher\PixLauncher.csproj

# Assemble a distributable directory containing the launcher and web runtime.
.\launcher\publish.ps1
.\dist\PixApp\PixLauncher.exe
```

The launcher first walks upward from its executable to find a packaged
`bin/pi-web.js`; this co-located runtime takes precedence over `PI_WEB_ROOT`.
The environment variable remains a development fallback for a standalone
launcher that was not produced by `publish.ps1`.

Node.js must currently be installed and available on `PATH`. `publish.ps1`
packages the built Pi Web runtime and installs production npm dependencies, but
does not redistribute Node.js itself.

Any future launcher configuration view must continue to use Pi's
`models.json`, `settings.json`, and `AuthStorage` through Pi Web APIs. The
launcher must not create a second configuration source or store API keys in its
own JSON files.
