import { NextResponse } from "next/server";
import { getUpdateStatus } from "@/lib/update-check";

export const dynamic = "force-dynamic";

/**
 * GET /api/update/status — 当前/最新版本与是否有更新。
 * 版本运行时读取（内核单独升级后构建期 env 会过期）。内部 10min 缓存。
 * 传 ?force=1 跳过缓存强制重新检查。
 */
export async function GET(req: Request) {
  const force = new URL(req.url).searchParams.get("force") === "1";
  try {
    const status = await getUpdateStatus({ force });
    return NextResponse.json(status, { headers: { "Cache-Control": "no-store" } });
  } catch (error) {
    return NextResponse.json(
      { error: error instanceof Error ? error.message : String(error) },
      { status: 500 },
    );
  }
}
