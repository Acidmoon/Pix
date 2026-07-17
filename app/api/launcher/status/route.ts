import { NextResponse } from "next/server";
import { isLauncherAuthorized } from "@/lib/launcher-auth";
import { getProviderAccountSummaries } from "@/lib/provider-quotas";

export const dynamic = "force-dynamic";

/** Return launcher-only balances and quota windows without exposing credentials. */
export async function GET(req: Request) {
  if (!isLauncherAuthorized(req)) {
    return NextResponse.json({ error: "Not found" }, { status: 404 });
  }

  try {
    return NextResponse.json({
      providers: await getProviderAccountSummaries(),
      updatedAt: new Date().toISOString(),
    }, {
      headers: { "Cache-Control": "no-store" },
    });
  } catch (error) {
    return NextResponse.json({ error: String(error) }, { status: 500 });
  }
}
