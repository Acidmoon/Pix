import { execFile, spawn } from "child_process";
import { copyFile, cp, mkdir, mkdtemp, readdir, rm } from "fs/promises";
import { existsSync } from "fs";
import { tmpdir } from "os";
import { dirname, join } from "path";
import { execPath } from "process";
import { promisify } from "util";
import { getAppCurrentVersion, getKernelCurrentVersion, getLatestVersion, APP_PACKAGE, KERNEL_PACKAGES } from "./update-check";
import type { AppUpdatePayload } from "./restart";

const execFileAsync = promisify(execFile);

/**
 * 跨平台跑 npm。Windows 的 `npm` 是 `npm.cmd`，Node ≥20.12（CVE-2024-27980）
 * 拒绝无 shell spawn `.cmd`；走 shell 又会引入引号注入问题。与 lib/npx.ts 同理：
 * 找到随 Node 安装的 `npm-cli.js`，直接用当前 node（execPath）跑，绝不经过 shell。
 */
function findNpmCli(): string | null {
  const nodeDir = dirname(execPath);
  const candidates = [
    // Windows MSI 布局：node.exe 与 node_modules 同目录
    join(nodeDir, "node_modules", "npm", "bin", "npm-cli.js"),
    // Unix 布局：.../bin/node + .../lib/node_modules/npm/bin/npm-cli.js
    join(nodeDir, "..", "lib", "node_modules", "npm", "bin", "npm-cli.js"),
  ];
  for (const p of candidates) {
    try {
      if (existsSync(p)) return p;
    } catch {
      // ignore
    }
  }
  return null;
}

function resolveNpmCommand(): { command: string; commandArgs: string[] } {
  const npmCli = findNpmCli();
  return npmCli
    ? { command: execPath, commandArgs: [npmCli] }
    : { command: "npm", commandArgs: [] };
}

export interface RunNpmOptions {
  cwd?: string;
  timeout?: number;
  env?: NodeJS.ProcessEnv;
}

/** 一次性 npm 命令（如 npm pack），收集全部输出。 */
export async function runNpm(
  args: string[],
  opts: RunNpmOptions = {},
): Promise<{ stdout: string; stderr: string }> {
  const { command, commandArgs } = resolveNpmCommand();
  return execFileAsync(command, [...commandArgs, ...args], {
    cwd: opts.cwd ?? process.cwd(),
    timeout: opts.timeout,
    env: opts.env ?? process.env,
    maxBuffer: 64 * 1024 * 1024,
  });
}

export interface SpawnNpmOptions {
  cwd?: string;
  env?: NodeJS.ProcessEnv;
  /** 每行输出回调（stdout+stderr 合流，按行切分），用于 SSE 进度。 */
  onLine?: (line: string) => void;
  signal?: AbortSignal;
}

/** 流式 npm 命令，逐行回调输出，返回退出码。 */
export function spawnNpm(args: string[], opts: SpawnNpmOptions = {}): Promise<number> {
  return new Promise((resolve, reject) => {
    const { command, commandArgs } = resolveNpmCommand();
    const child = spawn(command, [...commandArgs, ...args], {
      cwd: opts.cwd ?? process.cwd(),
      env: opts.env ?? process.env,
      shell: false,
      windowsHide: true,
    });

    let buffer = "";
    const push = (chunk: Buffer | string) => {
      buffer += chunk.toString();
      let idx: number;
      while ((idx = buffer.indexOf("\n")) >= 0) {
        const line = buffer.slice(0, idx).replace(/\r$/, "");
        buffer = buffer.slice(idx + 1);
        if (line.trim()) opts.onLine?.(line);
      }
    };
    child.stdout?.on("data", push);
    child.stderr?.on("data", push);

    const onAbort = () => child.kill("SIGTERM");
    opts.signal?.addEventListener("abort", onAbort);

    child.on("error", (err) => {
      opts.signal?.removeEventListener("abort", onAbort);
      reject(err);
    });
    child.on("close", (code) => {
      opts.signal?.removeEventListener("abort", onAbort);
      if (buffer.trim()) opts.onLine?.(buffer.replace(/\r$/, ""));
      resolve(code ?? 0);
    });
  });
}

// ---------------------------------------------------------------------------
// manifest 快照 / 回滚
//
// 不备份整个 node_modules（太大）。内核升级在重启前同步完成，失败在 npm install
// 阶段即可检测到 → 恢复 package.json / lock 再重装即可还原旧版本。
// ---------------------------------------------------------------------------

const MANIFEST_FILES = ["package.json", "package-lock.json"];

async function makeBackupDir(prefix: string): Promise<string> {
  const ts = new Date().toISOString().replace(/[:.]/g, "-");
  const dir = join(process.cwd(), "logs", `${prefix}-${ts}`);
  await mkdir(dir, { recursive: true });
  return dir;
}

export async function snapshotManifest(webRoot: string): Promise<string> {
  const backupDir = await makeBackupDir("updater-backup");
  for (const file of MANIFEST_FILES) {
    const src = join(webRoot, file);
    if (existsSync(src)) await copyFile(src, join(backupDir, file));
  }
  return backupDir;
}

export async function restoreManifest(webRoot: string, backupDir: string): Promise<void> {
  for (const file of MANIFEST_FILES) {
    const src = join(backupDir, file);
    if (existsSync(src)) await copyFile(src, join(webRoot, file));
  }
}

// ---------------------------------------------------------------------------
// 内核升级
// ---------------------------------------------------------------------------

export interface UpgradeKernelOptions {
  /** 目标版本，缺省 = 内核主包的 registry latest。 */
  version?: string;
  onLine?: (line: string) => void;
}

export interface UpgradeKernelResult {
  fromVersion: string | null;
  toVersion: string;
}

export async function upgradeKernel(opts: UpgradeKernelOptions = {}): Promise<UpgradeKernelResult> {
  const webRoot = process.cwd();
  const onLine = opts.onLine ?? (() => {});
  const fromVersion = await getKernelCurrentVersion(KERNEL_PACKAGES[0]);

  const version = opts.version ?? (await getLatestVersion(KERNEL_PACKAGES[0]));
  if (!version) throw new Error("无法确定目标版本（网络不可用或 registry 未返回版本）");
  if (fromVersion === version) {
    onLine(`内核已是 ${version}，无需升级。`);
    return { fromVersion, toVersion: version };
  }

  const backupDir = await snapshotManifest(webRoot);
  onLine(`准备升级内核：${fromVersion ?? "未知"} → ${version}`);
  try {
    const specs = KERNEL_PACKAGES.map((pkg) => `${pkg}@${version}`);
    const code = await spawnNpm(
      ["install", ...specs, "--no-audit", "--no-fund", "--loglevel=warn"],
      { cwd: webRoot, onLine },
    );
    if (code !== 0) throw new Error(`npm install 退出码 ${code}`);
  } catch (err) {
    onLine("升级失败，正在回滚到升级前的依赖…");
    await restoreManifest(webRoot, backupDir).catch(() => {});
    await spawnNpm(["install", "--no-audit", "--no-fund", "--loglevel=warn"], {
      cwd: webRoot,
      onLine,
    }).catch(() => {});
    throw err;
  }

  onLine(`内核已升级到 ${version}，准备重启服务…`);
  return { fromVersion, toVersion: version };
}

// ---------------------------------------------------------------------------
// 整应用升级（Phase 3）
//
// 应用本身就是 npm 包 @agegr/pi-web（files 含 bin/.next/public，不含 node_modules）。
// 思路：npm pack 下载新版 tarball → 解压到暂存目录 → 校验 → 备份当前应用文件；
// 真正的「换文件 + 重装依赖」由启动器在重启前跑 bin/apply-update.js 完成
// （那时服务已退出，.next 文件句柄已释放）。
// ---------------------------------------------------------------------------

/** 换文件/备份的应用文件集合（与 npm 包 files 一致；保留 node_modules/logs/.env）。 */
export const APP_SWAP_ENTRIES = ["bin", ".next", "public", "next.config.ts", "package.json"];

async function backupAppFiles(webRoot: string, backupDir: string): Promise<void> {
  for (const entry of APP_SWAP_ENTRIES) {
    const src = join(webRoot, entry);
    if (!existsSync(src)) continue;
    await cp(src, join(backupDir, entry), { recursive: true });
  }
}

export interface StageAppUpdateOptions {
  version?: string;
  onLine?: (line: string) => void;
}

export async function stageAppUpdate(opts: StageAppUpdateOptions = {}): Promise<AppUpdatePayload> {
  const webRoot = process.cwd();
  const onLine = opts.onLine ?? (() => {});
  const fromVersion = (await getAppCurrentVersion()) ?? "unknown";

  const version = opts.version ?? (await getLatestVersion(APP_PACKAGE));
  if (!version) throw new Error("无法确定目标版本（网络不可用或 registry 未返回版本）");
  if (fromVersion === version) throw new Error(`应用已是 ${version}，无需升级`);

  const stage = await mkdtemp(join(tmpdir(), "pix-app-stage-"));
  try {
    onLine(`下载 ${APP_PACKAGE}@${version} …`);
    const packResult = await runNpm(
      ["pack", `${APP_PACKAGE}@${version}`, "--pack-destination", stage, "--loglevel=warn"],
      { cwd: webRoot },
    ).catch((err) => {
      throw new Error(`npm pack 失败：${err instanceof Error ? err.message : String(err)}`);
    });
    if (packResult.stderr.trim()) onLine(packResult.stderr.trim().split("\n")[0]);

    const tarball = (await readdir(stage)).find((f) => f.endsWith(".tgz"));
    if (!tarball) throw new Error("npm pack 未生成 tarball");
    const tarballPath = join(stage, tarball);

    onLine(`解压 ${tarball} …`);
    await execFileAsync("tar", ["-xzf", tarballPath, "-C", stage]);
    const pkgDir = join(stage, "package");

    for (const required of ["package.json", join("bin", "pi-web.js"), ".next"]) {
      if (!existsSync(join(pkgDir, required))) throw new Error(`新版包缺少 ${required}`);
    }

    const backupDir = await makeBackupDir("pix-app-backup");
    onLine(`备份当前应用到 logs/${backupDir.split(/[\\/]/).pop()} …`);
    await backupAppFiles(webRoot, backupDir);

    onLine(`暂存完成，准备重启并替换到 ${version} …`);
    return { stagedDir: pkgDir, backupDir, fromVersion, toVersion: version };
  } catch (err) {
    // 暂存失败：清理临时目录（备份若已生成则保留，供人工检查）。
    await rm(stage, { recursive: true, force: true }).catch(() => {});
    throw err;
  }
}
