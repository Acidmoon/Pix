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
- Open Edge or Chrome in `--app` mode with an isolated temporary profile.
- Request authenticated AgentSession cleanup before stopping the service.
- Place the browser and service in Windows Job Objects so their process trees
  are terminated if graceful shutdown times out or the launcher exits.

The shutdown endpoint exists only when the launcher supplies a random
`PI_WEB_LAUNCHER_TOKEN`. Regular CLI and development-server runs do not expose
an unauthenticated service-stop operation.

The running panel currently includes two official provider adapters:

- DeepSeek calls `GET https://api.deepseek.com/user/balance` and displays total,
  topped-up, and granted balance by currency.
- MiniMax calls the China or global Coding Plan `remains` endpoint and displays
  the remaining 5-hour window plus the 7-day window when that plan enables it.

Credentials are read from Pi's `AuthStorage`, used only in server-side requests
to the matching official provider domain, and never returned to the launcher.
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

# Build and run the Windows launcher.
dotnet build .\launcher\PixLauncher.csproj
dotnet run --project .\launcher\PixLauncher.csproj
```

By default, the launcher walks upward from its executable and current working
directory to find `bin/pi-web.js`. Set `PI_WEB_ROOT` when the launcher binary is
stored elsewhere.

Node.js must currently be installed and available on `PATH`. Packaging a fixed
Node runtime and the Pi Web build into one installer is intentionally deferred
to the distribution phase.

Any future launcher configuration view must continue to use Pi's
`models.json`, `settings.json`, and `AuthStorage` through Pi Web APIs. The
launcher must not create a second configuration source or store API keys in its
own JSON files.
