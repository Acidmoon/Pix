import { NextResponse } from "next/server";

export const dynamic = "force-dynamic";

/**
 * Lightweight readiness and activity snapshot for the native launcher.
 *
 * Reads the in-process session registry directly from `globalThis` instead
 * of importing `@/lib/rpc-manager`. That module statically imports the entire
 * `@earendil-works/pi-coding-agent` SDK, whose transitive bundle traces to
 * ~3600 files in the route's nft — loading it on the first request takes
 * ~20 s, longer than the launcher's 30 s health-check window, so the service
 * was reported as "did not become healthy within 30 seconds".
 *
 * `globalThis.__piSessions` is populated lazily by `lib/rpc-manager` the
 * first time a session is created, so reading it here stays correct both
 * before that module is loaded (count 0) and after (real running count).
 */
type RunningSession = { isRunning?: () => boolean; sessionId?: string };

export async function GET() {
  const sessions = (globalThis as { __piSessions?: Map<string, RunningSession> }).__piSessions;
  let runningAgentCount = 0;
  if (sessions) {
    for (const [, session] of sessions) {
      if (session?.isRunning?.()) runningAgentCount++;
    }
  }
  return NextResponse.json({
    status: "ok",
    agentRunning: runningAgentCount > 0,
    runningAgentCount,
    startedAt: process.env.PI_WEB_STARTED_AT ?? null,
  }, {
    headers: { "Cache-Control": "no-store" },
  });
}
