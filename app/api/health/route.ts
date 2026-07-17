import { NextResponse } from "next/server";
import { getRunningRpcSessionIds } from "@/lib/rpc-manager";

export const dynamic = "force-dynamic";

/** Lightweight readiness and activity snapshot for the native launcher. */
export async function GET() {
  const runningAgentCount = getRunningRpcSessionIds().length;
  return NextResponse.json({
    status: "ok",
    agentRunning: runningAgentCount > 0,
    runningAgentCount,
    startedAt: process.env.PI_WEB_STARTED_AT ?? null,
  }, {
    headers: { "Cache-Control": "no-store" },
  });
}
