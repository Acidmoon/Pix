import { mkdir, writeFile } from "fs/promises";
import { dirname, join } from "path";
import { shutdownAllRpcSessions } from "./rpc-manager";

/**
 * 重启标志机制（热更新地基）。
 *
 * 内核是 serverExternalPackages，被 Node require 缓存，升级后必须重启 Node
 * 进程才能生效。而端口 / Job Object / 浏览器宿主都由 C# 启动器持有，所以重启
 * 必须由启动器驱动：服务只负责「写好标志后优雅退出」，启动器观察到进程退出、
 * 消费标志、再拉起一个新端口的进程并把浏览器重指过去。
 */

export type RestartReason = "kernel-upgrade" | "app-update" | "manual-restart";

/** 整应用升级时随标志一起传给启动器的换文件载荷（Phase 3）。 */
export interface AppUpdatePayload {
  /** 解压好的新版应用目录（npm pack 解包产物）。 */
  stagedDir: string;
  /** 旧版应用文件备份目录，供启动器健康门控回滚。 */
  backupDir: string;
  fromVersion: string;
  toVersion: string;
}

export interface RestartMarker {
  reason: RestartReason;
  requestedAt: string;
  appUpdate?: AppUpdatePayload;
}

/**
 * 标志文件路径。服务的 process.cwd() 与启动器 FindWebRoot() 都指向 web root，
 * 两侧读写同一文件。logs/ 目录在启动器模式下一定存在（service log 就在其中），
 * 这里仍做一次 mkdir 以兼容 dev 模式。
 */
export function restartMarkerPath(): string {
  return join(process.cwd(), "logs", "pix-restart.json");
}

export async function writeRestartMarker(marker: RestartMarker): Promise<void> {
  const markerPath = restartMarkerPath();
  await mkdir(dirname(markerPath), { recursive: true });
  await writeFile(markerPath, JSON.stringify(marker, null, 2), "utf8");
}

/**
 * 写重启标志 → 优雅停止所有 in-process AgentSession → 退出进程。
 *
 * 短延迟用于让进行中的 SSE/HTTP 响应（例如升级进度的 done 帧）冲刷到客户端，
 * 之后再 process.exit。启动器随后消费标志并重启。
 */
export async function requestRestart(marker: RestartMarker): Promise<void> {
  await writeRestartMarker(marker);
  await shutdownAllRpcSessions();
  setTimeout(() => process.exit(0), 250);
}
