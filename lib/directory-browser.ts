import { readdir, realpath, stat } from "fs/promises";
import path from "path";

export interface BrowsableDirectory {
  name: string;
  path: string;
}

// 不要用 os.homedir()：构建期 @vercel/nft 会静态求值 resolve(homedir(), <动态参数>)，
// 把整个用户目录当资源 glob 递归扫描，Windows 上扫到 My Documents 等系统
// junction 会 EPERM 导致构建失败。process.env 运行时取值，nft 无法静态求值
// （同 lib/file-access.ts 的处理）。
function homeDir(): string {
  const home = process.env.USERPROFILE ?? process.env.HOME;
  if (!home) throw new Error("cannot determine home directory");
  return home;
}

export function getBrowseStartDirectory(directory?: string): string {
  return directory || homeDir();
}

export function normalizeDirectory(directory: string): string {
  if (directory === "~") return homeDir();
  if (directory.startsWith("~/")) return path.resolve(homeDir(), directory.slice(2));
  return path.resolve(directory);
}

export function getParentDirectory(directory: string): string | null {
  // 非 Windows 盘符/UNC 路径一律按 POSIX 语义处理，避免在 Windows 上把
  // "/Users/alex" 规范化成 "\Users\alex"（上游在 Windows 下的测试失败点）。
  const pathApi = /^[a-zA-Z]:[\\/]/.test(directory) || directory.startsWith("\\\\")
    ? path.win32
    : path.posix;
  const normalized = pathApi.normalize(directory);
  const parent = pathApi.dirname(normalized);
  return parent === normalized ? null : parent;
}

export async function resolveDirectory(directory: string): Promise<string> {
  return realpath(normalizeDirectory(directory));
}

export async function listDirectories(directory: string): Promise<BrowsableDirectory[]> {
  const entries = await readdir(directory, { withFileTypes: true });
  // 忽略损坏、不可访问或不指向目录的符号链接。
  const candidates = await Promise.all(entries.map(async (entry) => {
    if (entry.isDirectory()) {
      return { name: entry.name, path: path.join(directory, entry.name) };
    }
    if (!entry.isSymbolicLink()) return null;

    try {
      const entryPath = path.join(directory, entry.name);
      const realEntryPath = await realpath(entryPath);
      const entryStat = await stat(realEntryPath);
      if (!entryStat.isDirectory()) return null;
      return { name: entry.name, path: entryPath };
    } catch {
      return null;
    }
  }));

  return candidates
    .filter((entry): entry is BrowsableDirectory => entry !== null)
    .sort((left, right) => left.name.localeCompare(right.name));
}
