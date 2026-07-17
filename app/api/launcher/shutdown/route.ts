import { NextResponse } from "next/server";
import { isLauncherAuthorized } from "@/lib/launcher-auth";
import { shutdownAllRpcSessions } from "@/lib/rpc-manager";

export const dynamic = "force-dynamic";

/**
 * Gracefully stop all AgentSessions, then let the launcher-observed Node
 * process exit after the HTTP response has had a chance to flush.
 */
export async function POST(req: Request) {
  if (!isLauncherAuthorized(req)) {
    // Hide the control endpoint when Pi Web was not started by the launcher.
    return NextResponse.json({ error: "Not found" }, { status: 404 });
  }

  await shutdownAllRpcSessions();
  setTimeout(() => process.exit(0), 100);
  return NextResponse.json({ success: true });
}
