# Pi Web - Development Notes

## Quick Start

```bash
npm run dev   # port 30141
```

Typecheck: `node_modules/.bin/tsc --noEmit`  
Lint: `npm run lint`  
**Never run `next build` during dev** — pollutes `.next/` and breaks `npm run dev`.

---

## Architecture

```
Browser                Next.js Server              AgentSession (in-process)
  │                        │                               │
  ├─ GET /api/sessions ────▶ reads ~/.pi/agent/sessions/   │
  ├─ GET /api/sessions/[id] reads .jsonl file directly     │
  ├─ GET /api/agent/running/events ───▶ running id SSE     │
  │                        │                               │
  ├─ send message ─────────▶ POST /api/agent/[id]          │
  │                        │   startRpcSession() ─────────▶│ createAgentSession()
  │                        │   session.send(cmd) ─────────▶│ session.prompt()
  │                        │                               │
  ├─ SSE connect ──────────▶ GET /api/agent/[id]/events    │
  │                        │   session.onEvent() ◀─────────│ session.subscribe()
  │◀── data: {...} ─────────│                               │
```

**Session browsing** (read-only): reads `.jsonl` files through SDK `SessionManager` helpers and `lib/session-reader.ts` — no AgentSession created.  
**Sending a message**: `startRpcSession()` in `lib/rpc-manager.ts` creates an AgentSession in-process.

---

## File Map

```
app/api/
  sessions/route.ts               GET  list all sessions
  sessions/[id]/route.ts          GET/PATCH/DELETE session
  sessions/[id]/context/route.ts  GET ?leafId= — context for a specific leaf
  sessions/[id]/export/route.ts   GET exported HTML for a session
  agent/new/route.ts              POST { cwd, message, toolNames?, provider?, modelId? }
  agent/[id]/route.ts             GET state | POST any command
  agent/[id]/events/route.ts      GET SSE stream
  agent/running/events/route.ts   GET SSE stream of currently-running session ids
  auth/all-providers/route.ts     GET API-key provider list
  auth/api-key/[provider]/route.ts GET/POST/DELETE provider API key status/storage
  auth/login/[provider]/route.ts  GET OAuth/device-code SSE | POST manual code
  auth/logout/[provider]/route.ts POST OAuth logout
  auth/providers/route.ts         GET OAuth provider list
  cwd/browse/route.ts             GET browsable server directory listing (DirectoryPicker)
  cwd/validate/route.ts           POST validate/select a cwd
  default-cwd/route.ts            POST create ~/pi-cwd-YYYYMMDD
  file-index/route.ts             GET file index for @-mention fuzzy search
  files/[...path]/route.ts        GET file contents for viewer
  git/diff/route.ts               GET git diff for changed files
  git/status/route.ts             GET git status for the Explorer badge
  health/route.ts                 GET readiness probe (no auth, used by launcher)
  home/route.ts                   GET user home directory
  launcher/status/route.ts        GET balances & quota (Bearer PI_WEB_LAUNCHER_TOKEN)
  launcher/shutdown/route.ts      POST graceful stop (Bearer PI_WEB_LAUNCHER_TOKEN)
  models/route.ts                 GET { models, modelList, defaultModel }
  models-config/route.ts          GET/PUT — read/write ~/.pi/agent/models.json
  models-config/catalog/route.ts  GET models.dev pricing presets
  models-config/discover/route.ts POST fetch a configured provider's upstream model list
  models-config/test/route.ts     POST test a configured model/provider
  plugins/route.ts                GET/POST package plugin management
  quotas/route.ts                 GET browser-side provider balances/quota windows (loopback, no launcher token)
  sessions/[id]/auto-name/route.ts POST auto-generate session title
  sessions/[id]/state/route.ts    GET lightweight live state
  skills/route.ts                 GET/PATCH loaded skills and disable-model-invocation
  skills/check/route.ts           GET check skill updates
  skills/install/route.ts         POST install skills through npx skills add
  skills/search/route.ts          GET/POST skills.sh search
  skills/update/route.ts          POST update an installed skill
  worktrees/route.ts              GET/POST/DELETE git worktrees
  voice/status/route.ts           GET voice model availability
  voice/transcribe/route.ts       POST raw Float32 PCM (X-Sample-Rate header) → { text }
  update/status/route.ts          GET current/latest versions + updateAvailable (npm registry)
  update/upgrade/route.ts         POST SSE — upgrade kernel/app, then trigger launcher restart

lib/
  agent-client.ts      typed fetch helper for /api/agent commands
  directory-browser.ts directory normalization + safe listing for cwd/browse
  draft-store.ts       local draft persistence helpers
  file-access.ts       allowed file roots for /api/files and worktrees
  file-paths.ts        client/server path encoding helpers
  http-dispatcher.ts   HTTP(S) proxy dispatcher for server-side fetch (HTTP_PROXY/NO_PROXY)
  markdown.ts          shared markdown helpers
  npx.ts               npx runner used by skill install
  path-security.ts     path traversal/symlink guards shared by file routes
  provider-quotas.ts   厂商额度查询注册表（DeepSeek/MiniMax/Kimi，配置走 QUOTA_* env）
  pi-types.ts          local structural types for pi SDK objects
  request-security.ts  LAN origin/host validation for non-loopback bindings
  rpc-manager.ts      AgentSessionWrapper + registry + startRpcSession
  session-reader.ts   SessionManager wrappers + path cache + buildSessionContext adapter
  tool-presets.ts     PRESET_NONE/DEFAULT/FULL + getPresetFromTools()
  types.ts            shared TypeScript types
  voice.ts            SenseVoice 识别器单例（globalThis 抗热重载）+ transcribePcm
  normalize.ts        normalizeToolCalls() — field name mismatch between file format and our types
  worktree.ts         project/worktree resolution and git worktree operations
  restart.ts          restart marker (logs/pix-restart.json) + requestRestart() for hot update
  update-check.ts     runtime version read + npm registry latest + semver compare + 10min cache
  updater.ts          cross-platform npm runner + kernel upgrade + app staging (npm pack)

bin/
  pi-web.js           node entry that runs `next start` (spawned by the launcher)
  pi-web-options.js   CLI/env option parsing (--port/--hostname/--no-open, default 127.0.0.1)
  node-version.js     Node >=22.19 preflight check
  apply-update.js     whole-app file swap + dep sync, run by the launcher before restart

components/
  AppShell.tsx        layout + URL state + tab management
  SessionSidebar.tsx  session tree + FileExplorer
  DirectoryPicker.tsx browsable/editable working-directory picker (backed by cwd/browse)
  ChatWindow.tsx      chat composition + completion sound wrapper
  ChatInput.tsx       input bar + model/thinking/tools/compact controls
  MessageView.tsx     renders one message (user/assistant/toolCall/toolResult)
  BranchNavigator.tsx in-session branch switcher
  ChatMinimap.tsx     scroll minimap alongside the message list
  MarkdownBody.tsx    markdown renderer
  MermaidBlock.tsx    mermaid diagram block used by MarkdownBody
  ModelsConfig.tsx    modal for editing models.json (opened from sidebar bottom)
  SettingsModal.tsx   localized app settings modal
  PluginsConfig.tsx   modal for installed package plugins
  SkillsConfig.tsx    modal for loaded/search/installable skills
  FileExplorer.tsx    file tree inside sidebar
  FileIcons.tsx       file icon helpers
  FileViewer.tsx      file content in a tab
  TabBar.tsx          tab bar (Chat + open file tabs)
  UpdateConfig.tsx    modal for kernel/app upgrade (SSE progress, opened from sidebar bottom)

hooks/
  useAgentSession.ts  messages + streaming + SSE + fork/navigate/reconciliation logic
  useAudio.ts         completion sound + browser AudioContext unlock
  useDragDrop.ts      shared drag/drop state
  useIsMobile.ts      responsive breakpoint hook
  useTheme.ts         theme state
  useVoiceInput.ts    麦克风 PCM 采集 + /api/voice/transcribe 上传转写
```

语音输入：ChatInput 工具栏麦克风按钮 → `useVoiceInput` 录音 → `/api/voice/transcribe`
（sherpa-onnx-node + `models/sensevoice-small-int8`，可用 `PIX_VOICE_MODEL_DIR` 覆盖）
→ `insertText` 插入光标处。启动器侧另有独立的系统级语音输入
（`launcher/VoiceInputService.cs`，悬浮球/托盘菜单开关，全局右 Ctrl 按住说话）。

---

## Key Design Decisions & Traps

### i18n（`lib/i18n.tsx`）
- 所有用户可见文案走 `useT()`，词条在 `lib/locales/zh.json` / `en.json`，两文件 key 必须保持一一对应
- `useT` 必须在 `PixI18nProvider` 内使用——node --test 渲染组件时要包 Provider，并用 `@/lib/i18n` 别名导入（相对路径会产生第二个 context 实例）
- 工具栏模型按钮右侧内联显示当前模型厂商的余额/配额：数据来自 `/api/quotas`（与 `/api/launcher/status` 共用 `getProviderAccountSummaries()` 60s 缓存），余额 <¥5 或配额 <20% 显示红色

### AgentSession lifecycle (`lib/rpc-manager.ts`)
- One `AgentSessionWrapper` per session id, keyed in `globalThis.__piSessions`
- `globalThis` survives Next.js hot-reload; plain module-level Map does not
- Idle timeout: 10 minutes. Concurrent `startRpcSession()` calls share a single start Promise (`globalThis.__piStartLocks`)

### Fork must destroy the wrapper immediately
`AgentSession.fork()` **mutates the wrapper's inner state in-place** — after fork, `inner.sessionId` is the *new* session's id. If the wrapper stays alive in the registry under the old id, the next request gets the already-forked state and subsequent forks produce a corrupt `parentSession` chain.

**Fix**: `send("fork")` captures `newSessionId`, then calls `this.destroy()` before returning. The next request for the original session reloads a clean AgentSession from the original file.

### Two kinds of branching — don't confuse them
- **Fork** (Fork button on user message): creates a new independent `.jsonl` file. Shown as a child in the sidebar tree via `parentSession` header field.
- **In-session branch** (Continue button / BranchNavigator): calls `navigate_tree` within the same file. Multiple entries share the same `parentId`. Switching between them calls `/api/sessions/[id]/context?leafId=`.

### Session files can be fully rewritten
`parentSession` in the header is **display metadata only** — has zero effect on chat content. Safe to `writeFileSync` the entire file (pi does this itself during migrations). Used when cascade-reparenting children on delete.

### ToolCall field normalization
Pi stores toolCall blocks as `{type:"toolCall", id, name, arguments}` but `ToolCallContent` uses `{toolCallId, toolName, input}`. `normalizeToolCalls()` in `lib/normalize.ts` handles this — called in both `session-reader.ts` (file load) and `ChatWindow.handleAgentEvent()` (streaming).

### New session tool preset
Tool names are passed at session creation (`POST /api/agent/new` → `toolNames[]`). For existing sessions, the active preset is inferred on mount via `get_tools` → `getPresetFromTools()`. When tools are fully disabled (`toolNames = []`), `rpc-manager.ts` passes an empty tool allow-list and forces `agent.state.systemPrompt = ""` after startup/reload/resource discovery.

### Model defaults for new sessions
`GET /api/models` returns `defaultModel` read from `~/.pi/agent/settings.json`. `ChatWindow` pre-selects this on mount for new sessions.

### SSE reconnect on page refresh mid-stream
On `ChatWindow` mount, `GET /api/agent/[id]` is called. If `state.isStreaming === true`, SSE is reconnected automatically. `thinkingLevel` and `isCompacting` are also synced from this response.

### Compaction SSE events
Newer pi emits `compaction_start` / `compaction_end`; older versions emitted `auto_compaction_start` / `auto_compaction_end`. `handleAgentEvent` accepts both sets to keep `isCompacting` in sync. Manual compact is a blocking POST — the button stays disabled until the response returns.

### Running state SSE + reconciliation
- The sidebar listens to `/api/agent/running/events`, backed by `subscribeRunningSessions()` in `lib/rpc-manager.ts`, so running badges update without polling.
- `useAgentSession` still treats per-session SSE as primary for chat events, but while a run is active it periodically calls `GET /api/agent/[id]` and also reconciles on `visibilitychange`/`online`. This fixes missed `agent_end` events from background tabs or half-open connections.
- Prompt runs use a monotonic run id; late SSE or slow reconciliation responses from an old run must be ignored so they cannot resurrect stale streaming bubbles.

### Worktrees and project grouping
- `lib/worktree.ts` resolves linked worktree top-levels back to the main repo `projectRoot`; `listAllSessions()` attaches that to each `SessionInfo` so all worktrees for one repo are grouped together in the sidebar.
- Worktree operations are served by `/api/worktrees` and guarded by the same allowed-root rules as `/api/files`.
- New worktrees are created under `<repoRoot>-worktrees/<sanitized-branch>`. Existing branches are reused; otherwise `git worktree add -b` creates the branch.
- Removing a dirty worktree returns `409` with `{ dirty: true }` so the UI can ask before retrying with `force`.
- Sessions whose cwd points at a removed worktree are inferred back into the main project instead of becoming a phantom project row.

### File access allow-list
- `/api/files` is intentionally not a general filesystem browser. Allowed roots come from session cwds, their resolved project roots, `~/pi-cwd-*`, and roots explicitly added with `allowFileRoot()`.
- `/api/cwd/validate`, `/api/default-cwd`, and `/api/worktrees` call `allowFileRoot()` when they make a new location browsable.

### Plugins and skills
- `/api/plugins` uses pi's `SettingsManager` + `DefaultPackageManager` for global/project package install, remove, update, enable, and disable. Disabling writes empty `extensions/skills/prompts/themes` arrays for that package entry.
- `/api/skills` uses `DefaultResourceLoader` so settings paths, package skills, and project `.agents/skills` are listed the same way the runtime sees them.
- Skill toggling edits only the `disable-model-invocation` frontmatter key on the target `SKILL.md`; keep that surgical so user formatting survives.
- `/api/skills/install` shells through `npx skills add ... --agent pi`; project installs run with the selected cwd.

### Auth and model config
- `ModelsConfig` combines models from `~/.pi/agent/models.json` with provider auth status from pi's `AuthStorage`/`ModelRegistry`.
- OAuth/device-code/manual-code flows are streamed by `GET /api/auth/login/[provider]`; manual code responses POST back with a short-lived token stored in `globalThis.__piLoginCallbacks`.
- API-key routes store and remove keys through `AuthStorage`. Status endpoints must never return the raw key.
- The model test route is `app/api/models-config/test/route.ts`; `app/api/models/test/` is not a real route.

### Completion sound
- `hooks/useAudio.ts` stores the toggle in `localStorage` as `pi-sound-enabled` and reuses one `AudioContext`.
- Browser autoplay policy means sound must be unlocked from a user gesture; `ChatInput` calls the unlock hook from interactive controls, and `ChatWindow` plays the tone from `onAgentEnd`.

### Exported session HTML
- `/api/sessions/[id]/export` delegates to pi's export helper, then patches recursive tree helpers in the generated HTML to iterative versions so very deep linear sessions do not overflow the browser call stack.

### Hot update & kernel upgrade
- The pi kernel (`@earendil-works/pi-coding-agent / pi-ai / pi-tui`) is a `serverExternalPackage`, cached by Node's require — upgrading it needs a **process restart**, not an in-place swap. Same for the whole app (`.next`).
- Restart is launcher-driven: the service writes `logs/pix-restart.json` (`lib/restart.ts requestRestart()`), gracefully stops sessions, then `process.exit(0)`. The launcher's `RefreshStatusAsync` (1s tick) sees the exit, **consumes (deletes) the marker** via `PiWebProcessManager.TryConsumeRestartMarker`, then `RestartAsync()` (new port) + `OpenBrowser()` re-points the Edge/Chrome `--app` window. Marker consumption + the `busy` flag prevent restart loops; a markerless exit (crash) only renders “stopped”, never auto-restarts.
- `GET /api/update/status` reads versions **at runtime** (app = web-root `package.json`; kernel = `node_modules` `package.json`), not the build-time `NEXT_PUBLIC_PI_VERSION` (stale after a kernel-only upgrade). Latest comes from the npm registry (`PIX_NPM_REGISTRY` overrides the default `registry.npmjs.org`).
- `POST /api/update/upgrade` streams npm output over SSE. `target:"kernel"` runs `npm install <kernel>@<ver>` in the web root (cross-platform `npm-cli.js` invocation mirroring `lib/npx.ts`, never a shell); on failure it restores the snapshotted `package.json`/lock + reinstalls (rollback) and does NOT restart. `target:"app"` `npm pack`s the new `@agegr/pi-web`, stages it, and the marker carries an `appUpdate` payload.
- Whole-app swap happens in the launcher while the service is stopped: `PiWebProcessManager.ApplyAppUpdate` runs `bin/apply-update.js <stagedDir>` (swap `bin/.next/public/next.config.ts/package.json`, keep `node_modules`/`logs`/`.env`, then `npm install --omit=dev`). `apply-update.js` reads its own source before the swap and writes itself back if the staged package lacked `bin/apply-update.js` (a broken publish must not kill the rollback path). If the new build fails the health check (120s deadline — cold boot / AV scanning a freshly swapped `.next` can exceed 30s), `RestartForUpdateAsync` restores from the backup dir once (`--no-cleanup`) and retries. Staging's pre-swap backup copies `.next` with a filter that skips `.next/dev` (Turbopack dev cache full of symlinks — recreating them needs admin on Windows and the cache is useless for rollback).
- Update endpoints are loopback-only like the rest of the browser API (no launcher token); update state lives on `globalThis` to survive hot-reload.

## Pi Session File Format

Location: `~/.pi/agent/sessions/<encoded-cwd>/<timestamp>_<uuid>.jsonl`

```jsonl
{"type":"session","version":3,"id":"<uuid>","timestamp":"...","cwd":"/path","parentSession":"/abs/path/to/parent.jsonl"}
{"type":"model_change","id":"<8hex>","parentId":null,"provider":"zenmux","modelId":"claude-sonnet-4-6","timestamp":"..."}
{"type":"message","id":"<8hex>","parentId":"<8hex>","message":{"role":"user","content":"..."}}
{"type":"message","id":"<8hex>","parentId":"<8hex>","message":{"role":"assistant","content":[...],...}}
{"type":"message","id":"<8hex>","parentId":"<8hex>","message":{"role":"toolResult","toolCallId":"...","content":[...]}}
{"type":"compaction","id":"<8hex>","parentId":"<8hex>","summary":"...","firstKeptEntryId":"<8hex>","tokensBefore":N}
{"type":"session_info","id":"...","parentId":"...","name":"user-defined name"}
```

`entryIds[]` in `SessionContext` is a parallel array to `messages[]` — maps each displayed message back to its `.jsonl` entry id, used for fork and navigate_tree calls.

---

## CSS Variables (`app/globals.css`)

```
--bg --bg-panel --bg-hover --bg-selected --border
--text --text-muted --text-dim
--accent --user-bg --tool-bg
--font-mono
```
