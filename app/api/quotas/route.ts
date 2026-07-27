import { NextResponse } from "next/server";
import { getProviderAccountSummaries, getProviderKeyMapping } from "@/lib/provider-quotas";

export const dynamic = "force-dynamic";

/**
 * 浏览器侧的厂商余额/配额查询（loopback，无需 launcher token）。
 * 与 /api/launcher/status 共用同一数据源和 60s 缓存；原始凭据不出服务端。
 * providerKeys 用于把 modelList 里的 provider 关联到对应的厂商账户。
 */
export async function GET() {
  try {
    return NextResponse.json({
      providers: await getProviderAccountSummaries(),
      providerKeys: getProviderKeyMapping(),
      updatedAt: new Date().toISOString(),
    }, {
      headers: { "Cache-Control": "no-store" },
    });
  } catch (error) {
    return NextResponse.json({ error: String(error) }, { status: 500 });
  }
}
