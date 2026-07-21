import { NextResponse } from "next/server";
import { statSync, type Stats } from "fs";
import { isAbsolute, resolve } from "path";
import { allowFileRoot } from "@/lib/file-access";

// 注意：不要用 os.homedir() 直接拼接动态路径——Next 构建期 @vercel/nft
// 会对 resolve(homedir(), <动态参数>) 生成整个用户目录的资源 glob，
// 在 Windows 上递归扫描系统 junction（My Documents 等）EPERM 导致构建失败。
// process.env 运行时才取值，nft 无法静态求值。
function homeDirectory(): string {
  const home = process.env.USERPROFILE ?? process.env.HOME;
  if (!home) throw new Error("无法确定用户主目录");
  return home;
}

function normalizeCwd(cwd: string): string {
  if (cwd === "~") return homeDirectory();
  if (cwd.startsWith("~/")) return resolve(homeDirectory(), cwd.slice(2));
  return isAbsolute(cwd) ? cwd : resolve(cwd);
}

// POST /api/cwd/validate  body: { cwd: string }
// Validates a candidate workspace before the UI selects it.
export async function POST(req: Request) {
  try {
    const body = await req.json() as { cwd?: unknown };
    const cwd = typeof body.cwd === "string" ? body.cwd.trim() : "";

    if (!cwd) {
      return NextResponse.json({ error: "Path is required" }, { status: 400 });
    }

    const normalizedCwd = normalizeCwd(cwd);
    let stat: Stats;
    try {
      stat = statSync(normalizedCwd);
    } catch {
      return NextResponse.json({ error: `Directory does not exist: ${cwd}` }, { status: 400 });
    }

    if (!stat.isDirectory()) {
      return NextResponse.json({ error: `Path is not a directory: ${cwd}` }, { status: 400 });
    }

    allowFileRoot(normalizedCwd);
    return NextResponse.json({ success: true, cwd: normalizedCwd });
  } catch (error) {
    return NextResponse.json({ error: String(error) }, { status: 500 });
  }
}
