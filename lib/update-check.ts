import { readFile } from "fs/promises";
import { join } from "path";
import type { PackageUpdateInfo, UpdateStatus } from "@/lib/api-types";

/**
 * 更新检查：运行时读取本地实际安装版本，查 npm registry 得到 latest，
 * 用内置 semver 比较得出是否有更新。模式参考 lib/skill-updates.ts。
 *
 * 注意：内核版本一律运行时从 node_modules 读取，不用 next.config.ts 烘焙的
 * NEXT_PUBLIC_PI_VERSION——内核单独升级后那个构建期值会过期。
 */

const DEFAULT_REGISTRY = process.env.PIX_NPM_REGISTRY || "https://registry.npmjs.org";
const CHECK_TIMEOUT_MS = 15_000;
const CACHE_TTL_MS = 10 * 60 * 1000;

export const APP_PACKAGE = "@agegr/pi-web";

/** 内核三包，版本通常一致，升级时统一升到同一版本。 */
export const KERNEL_PACKAGES = [
  "@earendil-works/pi-coding-agent",
  "@earendil-works/pi-ai",
  "@earendil-works/pi-tui",
] as const;

type Fetcher = (input: string, init?: RequestInit) => Promise<Response>;

export interface UpdateStatusOptions {
  fetcher?: Fetcher;
  registry?: string;
  /** 跳过缓存强制重新检查。 */
  force?: boolean;
}

declare global {
  var __piUpdateStatus: { at: number; status: UpdateStatus } | undefined;
}

// ---------------------------------------------------------------------------
// 版本比较（内置极简 semver，避免引入/依赖传递性 semver 包）
// ---------------------------------------------------------------------------

function parseVersion(version: string): { nums: number[]; pre: string[] } {
  let clean = version.trim().replace(/^v/i, "");
  const plus = clean.indexOf("+");
  if (plus >= 0) clean = clean.slice(0, plus); // 丢弃构建元数据
  const dash = clean.indexOf("-");
  let main = clean;
  let pre = "";
  if (dash >= 0) {
    main = clean.slice(0, dash);
    pre = clean.slice(dash + 1);
  }
  const nums = main.split(".").map((part) => {
    const n = parseInt(part, 10);
    return Number.isFinite(n) ? n : 0;
  });
  return { nums, pre: pre ? pre.split(".") : [] };
}

/** 返回 -1 / 0 / 1，遵循 semver 主版本.次版本.补丁 + 预发布规则。 */
export function compareVersions(a: string, b: string): number {
  const pa = parseVersion(a);
  const pb = parseVersion(b);
  const mainLen = Math.max(pa.nums.length, pb.nums.length);
  for (let i = 0; i < mainLen; i++) {
    const da = pa.nums[i] ?? 0;
    const db = pb.nums[i] ?? 0;
    if (da !== db) return da < db ? -1 : 1;
  }
  // 主版本相同：正式版 > 预发布版
  if (pa.pre.length === 0 && pb.pre.length === 0) return 0;
  if (pa.pre.length === 0) return 1;
  if (pb.pre.length === 0) return -1;
  const preLen = Math.max(pa.pre.length, pb.pre.length);
  for (let i = 0; i < preLen; i++) {
    const ia = pa.pre[i];
    const ib = pb.pre[i];
    if (ia === undefined) return -1;
    if (ib === undefined) return 1;
    const na = /^\d+$/.test(ia) ? parseInt(ia, 10) : null;
    const nb = /^\d+$/.test(ib) ? parseInt(ib, 10) : null;
    if (na !== null && nb !== null) {
      if (na !== nb) return na < nb ? -1 : 1;
    } else if (na !== null) {
      return -1; // 数字标识符 < 字母标识符
    } else if (nb !== null) {
      return 1;
    } else if (ia !== ib) {
      return ia < ib ? -1 : 1;
    }
  }
  return 0;
}

export function isNewerVersion(latest: string, current: string): boolean {
  return compareVersions(latest, current) > 0;
}

// ---------------------------------------------------------------------------
// 本地版本（运行时读取）
// ---------------------------------------------------------------------------

async function readPackageVersion(dir: string): Promise<string | null> {
  try {
    const raw = await readFile(join(dir, "package.json"), "utf8");
    const parsed = JSON.parse(raw) as { version?: unknown };
    return typeof parsed.version === "string" ? parsed.version : null;
  } catch {
    return null;
  }
}

export function getAppCurrentVersion(): Promise<string | null> {
  return readPackageVersion(process.cwd());
}

export function getKernelCurrentVersion(pkg: string): Promise<string | null> {
  return readPackageVersion(join(process.cwd(), "node_modules", pkg));
}

// ---------------------------------------------------------------------------
// 远端 latest
// ---------------------------------------------------------------------------

function registryPackageUrl(registry: string, pkg: string): string {
  // scoped 包名里的 "/" 必须编码为 %2F；保留 "@" 字面量（npm registry 规范形式）。
  const name = pkg.startsWith("@")
    ? `@${encodeURIComponent(pkg.slice(1))}`
    : encodeURIComponent(pkg);
  return `${registry.replace(/\/+$/, "")}/${name}/latest`;
}

export async function getLatestVersion(
  pkg: string,
  options: UpdateStatusOptions = {},
): Promise<string | null> {
  const fetcher = options.fetcher ?? fetch;
  const registry = options.registry ?? DEFAULT_REGISTRY;
  const response = await fetcher(registryPackageUrl(registry, pkg), {
    cache: "no-store",
    signal: AbortSignal.timeout(CHECK_TIMEOUT_MS),
  });
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  const data = (await response.json()) as { version?: unknown };
  return typeof data.version === "string" ? data.version : null;
}

async function buildPackageInfo(
  pkg: string,
  current: string | null,
  options: UpdateStatusOptions,
): Promise<PackageUpdateInfo> {
  let latest: string | null = null;
  let error: string | undefined;
  try {
    latest = await getLatestVersion(pkg, options);
  } catch (err) {
    error = err instanceof Error ? err.message : String(err);
  }
  return {
    pkg,
    current,
    latest,
    updateAvailable: !!(current && latest && isNewerVersion(latest, current)),
    ...(error ? { error } : {}),
  };
}

// ---------------------------------------------------------------------------
// 汇总（带 globalThis 缓存，抗热重载、避免频繁打 registry）
// ---------------------------------------------------------------------------

export async function getUpdateStatus(options: UpdateStatusOptions = {}): Promise<UpdateStatus> {
  const cached = globalThis.__piUpdateStatus;
  if (!options.force && cached && Date.now() - cached.at < CACHE_TTL_MS) {
    return cached.status;
  }

  const [appCurrent, ...kernelCurrents] = await Promise.all([
    getAppCurrentVersion(),
    ...KERNEL_PACKAGES.map((pkg) => getKernelCurrentVersion(pkg)),
  ]);

  const app = await buildPackageInfo(APP_PACKAGE, appCurrent, options);
  const kernel = await Promise.all(
    KERNEL_PACKAGES.map((pkg, i) => buildPackageInfo(pkg, kernelCurrents[i] ?? null, options)),
  );

  const status: UpdateStatus = { app, kernel, checkedAt: new Date().toISOString() };
  globalThis.__piUpdateStatus = { at: Date.now(), status };
  return status;
}
