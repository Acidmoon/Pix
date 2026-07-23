import { NextResponse } from "next/server";
import { requestRestart } from "@/lib/restart";
import { stageAppUpdate, upgradeKernel } from "@/lib/updater";
import type { UpdateTarget, UpdateUpgradeEvent } from "@/lib/api-types";

export const dynamic = "force-dynamic";

declare global {
  var __piUpdateInProgress: boolean | undefined;
}

interface UpgradeBody {
  target?: UpdateTarget;
  version?: string;
}

const VALID_TARGETS: UpdateTarget[] = ["kernel", "app", "restart-only"];

/**
 * POST /api/update/upgrade — SSE 流式升级。
 *
 * 帧：{type:"log",line} 进度；{type:"done",target,restart} 成功（随后写重启标志并退出）；
 * {type:"error",message} 失败（已回滚，不重启，服务继续运行）。
 * 本地回环服务，与其余浏览器 API 一致无额外鉴权。升级进行中返回 409。
 */
export async function POST(req: Request) {
  let body: UpgradeBody = {};
  try {
    body = (await req.json()) as UpgradeBody;
  } catch {
    // 空 body 也允许，默认 kernel
  }
  const target = body.target ?? "kernel";
  if (!VALID_TARGETS.includes(target)) {
    return NextResponse.json({ error: "Invalid target" }, { status: 400 });
  }
  if (globalThis.__piUpdateInProgress) {
    return NextResponse.json({ error: "Update already in progress" }, { status: 409 });
  }

  const stream = new ReadableStream({
    async start(controller) {
      const encode = (event: UpdateUpgradeEvent) => {
        try {
          controller.enqueue(new TextEncoder().encode(`data: ${JSON.stringify(event)}\n\n`));
        } catch {
          // controller 已关闭
        }
      };
      const onLine = (line: string) => encode({ type: "log", line });

      globalThis.__piUpdateInProgress = true;
      try {
        if (target === "restart-only") {
          onLine("正在重启服务…");
          encode({ type: "done", target, restart: true });
          await requestRestart({ reason: "manual-restart", requestedAt: new Date().toISOString() });
          return;
        }

        if (target === "kernel") {
          const result = await upgradeKernel({ version: body.version, onLine });
          encode({
            type: "done",
            target,
            restart: true,
            fromVersion: result.fromVersion,
            toVersion: result.toVersion,
          });
          await requestRestart({ reason: "kernel-upgrade", requestedAt: new Date().toISOString() });
          return;
        }

        // target === "app"：下载并暂存新版包，重启时由启动器跑 apply-update.js 换文件。
        const payload = await stageAppUpdate({ version: body.version, onLine });
        encode({
          type: "done",
          target,
          restart: true,
          fromVersion: payload.fromVersion,
          toVersion: payload.toVersion,
        });
        await requestRestart({
          reason: "app-update",
          requestedAt: new Date().toISOString(),
          appUpdate: payload,
        });
      } catch (error) {
        encode({
          type: "error",
          message: error instanceof Error ? error.message : String(error),
        });
      } finally {
        globalThis.__piUpdateInProgress = false;
        try {
          controller.close();
        } catch {
          // 已关闭
        }
      }
    },
  });

  return new Response(stream, {
    headers: {
      "Content-Type": "text/event-stream",
      "Cache-Control": "no-cache",
      Connection: "keep-alive",
    },
  });
}
