"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useIsMobile } from "@/hooks/useIsMobile";
import { useT } from "@/lib/i18n";
import type { PackageUpdateInfo, UpdateStatus, UpdateTarget, UpdateUpgradeEvent } from "@/lib/api-types";

interface UpdateConfigProps {
  onClose: () => void;
  /** 升级完成并重启后回调（用于刷新侧边栏红点等）。 */
  onUpdated?: () => void;
}

/** 解析 POST /api/update/upgrade 的 SSE 流（EventSource 只支持 GET，这里手动读 body）。 */
async function streamUpgrade(
  target: UpdateTarget,
  version: string | undefined,
  onEvent: (event: UpdateUpgradeEvent) => void,
  signal?: AbortSignal,
): Promise<void> {
  const response = await fetch("/api/update/upgrade", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ target, ...(version ? { version } : {}) }),
    signal,
  });
  if (!response.ok || !response.body) {
    let message = `HTTP ${response.status}`;
    try {
      const json = (await response.json()) as { error?: string };
      if (json.error) message = json.error;
    } catch {
      // 忽略解析错误，使用默认信息
    }
    throw new Error(message);
  }
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
    let idx: number;
    while ((idx = buffer.indexOf("\n\n")) >= 0) {
      const frame = buffer.slice(0, idx);
      buffer = buffer.slice(idx + 2);
      const dataLine = frame.split("\n").find((l) => l.startsWith("data: "));
      if (!dataLine) continue;
      try {
        onEvent(JSON.parse(dataLine.slice("data: ".length)) as UpdateUpgradeEvent);
      } catch {
        // 忽略损坏帧
      }
    }
  }
}

function VersionRow({ info, label }: { info: PackageUpdateInfo; label: string }) {
  const { t } = useT();
  const hasUpdate = info.updateAvailable;
  return (
    <div style={{ display: "flex", alignItems: "center", gap: 10, padding: "6px 0" }}>
      <span style={{ width: 190, flexShrink: 0, fontSize: 12, color: "var(--text-muted)" }}>{label}</span>
      <code style={{ fontSize: 12, color: "var(--text)", fontFamily: "var(--font-mono)" }}>
        {info.current ?? t("update.unknown")}
      </code>
      {hasUpdate && info.latest && (
        <>
          <span style={{ color: "var(--text-dim)", fontSize: 12 }}>→</span>
          <code style={{ fontSize: 12, color: "var(--accent)", fontFamily: "var(--font-mono)", fontWeight: 700 }}>
            {info.latest}
          </code>
          <span style={{
            fontSize: 10, padding: "1px 6px", borderRadius: 8,
            background: "var(--accent)", color: "#fff", fontWeight: 600,
          }}>{t("update.available")}</span>
        </>
      )}
      {info.error && (
        <span style={{ fontSize: 11, color: "#ef4444" }}>{t("update.checkFailed", { error: info.error })}</span>
      )}
    </div>
  );
}

export function UpdateConfig({ onClose, onUpdated }: UpdateConfigProps) {
  const { t } = useT();
  const isMobile = useIsMobile();
  const [status, setStatus] = useState<UpdateStatus | null>(null);
  const [checking, setChecking] = useState(false);
  const [upgrading, setUpgrading] = useState(false);
  const [restarting, setRestarting] = useState(false);
  const [logs, setLogs] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const logRef = useRef<HTMLDivElement | null>(null);
  const abortRef = useRef<AbortController | null>(null);

  const loadStatus = useCallback(async (force: boolean) => {
    setChecking(true);
    setError(null);
    try {
      const res = await fetch(`/api/update/status${force ? "?force=1" : ""}`, { cache: "no-store" });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      setStatus(await res.json() as UpdateStatus);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setChecking(false);
    }
  }, []);

  useEffect(() => {
    void loadStatus(false);
    return () => abortRef.current?.abort();
  }, [loadStatus]);

  useEffect(() => {
    logRef.current?.scrollTo({ top: logRef.current.scrollHeight });
  }, [logs]);

  const kernelUpdateAvailable = status?.kernel.some((k) => k.updateAvailable) ?? false;
  const appUpdateAvailable = status?.app.updateAvailable ?? false;

  const runUpgrade = useCallback(async (target: UpdateTarget) => {
    setUpgrading(true);
    setRestarting(false);
    setLogs([]);
    setError(null);
    const controller = new AbortController();
    abortRef.current = controller;
    try {
      await streamUpgrade(target, undefined, (event) => {
        if (event.type === "log") {
          setLogs((prev) => [...prev, event.line]);
        } else if (event.type === "done") {
          if (event.restart) {
            setRestarting(true);
            setLogs((prev) => [...prev, t("update.restartingDesc")]);
            onUpdated?.();
          }
        } else if (event.type === "error") {
          setError(event.message);
          setLogs((prev) => [...prev, t("update.upgradeFailed", { error: event.message })]);
        }
      }, controller.signal);
    } catch (err) {
      if ((err as Error).name !== "AbortError") {
        setError(err instanceof Error ? err.message : String(err));
      }
    } finally {
      setUpgrading(false);
    }
  }, [onUpdated, t]);

  const busy = upgrading || restarting;

  return (
    <div
      style={{ position: "fixed", inset: 0, zIndex: 1000, background: "rgba(0,0,0,0.35)", display: "flex", alignItems: "center", justifyContent: "center" }}
      onClick={(e) => { if (e.target === e.currentTarget && !busy) onClose(); }}
    >
      <div style={{
        width: isMobile ? "calc(100vw - 16px)" : 640, maxWidth: "calc(100vw - 16px)",
        height: isMobile ? "calc(100dvh - 16px)" : "70vh", maxHeight: "calc(100dvh - 16px)",
        background: "var(--bg)", border: "1px solid var(--border)", borderRadius: 10,
        display: "flex", flexDirection: "column", boxShadow: "0 8px 32px rgba(0,0,0,0.18)", overflow: "hidden",
      }}>
        {/* Header */}
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", padding: "12px 18px", borderBottom: "1px solid var(--border)", flexShrink: 0 }}>
          <div style={{ display: "flex", alignItems: "baseline", gap: 10 }}>
            <span style={{ fontSize: 15, fontWeight: 700, color: "var(--text)" }}>{t("update.title")}</span>
            <span style={{ fontSize: 11, color: "var(--text-muted)" }}>{t("update.versionInfo")}</span>
          </div>
          <button
            onClick={onClose}
            disabled={busy}
            style={{ background: "none", border: "none", color: "var(--text-muted)", cursor: busy ? "default" : "pointer", fontSize: 20, lineHeight: 1, padding: "2px 6px", opacity: busy ? 0.4 : 1 }}
          >×</button>
        </div>

        {/* Body */}
        <div style={{ flex: 1, overflowY: "auto", padding: "14px 18px", display: "flex", flexDirection: "column", gap: 14 }}>
          {/* 版本信息 */}
          <section>
            <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 6 }}>
              <h3 style={{ margin: 0, fontSize: 13, fontWeight: 600, color: "var(--text)" }}>{t("update.currentVersion")}</h3>
              <button
                onClick={() => void loadStatus(true)}
                disabled={checking || busy}
                style={{
                  fontSize: 12, padding: "3px 10px", borderRadius: 7,
                  border: "1px solid var(--border)", background: "var(--bg-panel)",
                  color: "var(--text)", cursor: checking || busy ? "default" : "pointer", opacity: checking || busy ? 0.5 : 1,
                }}
              >{checking ? t("update.checking") : t("update.checkUpdate")}</button>
            </div>
            {status ? (
              <div style={{ borderTop: "1px solid var(--border)", borderBottom: "1px solid var(--border)", padding: "4px 0" }}>
                <VersionRow info={status.app} label="应用 @agegr/pi-web" />
                {status.kernel.map((k) => (
                  <VersionRow key={k.pkg} info={k} label={`内核 ${k.pkg.replace("@earendil-works/", "")}`} />
                ))}
              </div>
            ) : (
              <div style={{ fontSize: 12, color: "var(--text-muted)" }}>{checking ? "正在读取版本信息…" : "无版本信息"}</div>
            )}
            {status && !kernelUpdateAvailable && !appUpdateAvailable && !checking && (
              <div style={{ marginTop: 8, fontSize: 12, color: "var(--text-muted)" }}>{t("update.upToDate")}</div>
            )}
          </section>

          {/* 操作 */}
          <section style={{ display: "flex", gap: 10, flexWrap: "wrap" }}>
            <button
              onClick={() => void runUpgrade("kernel")}
              disabled={busy || !kernelUpdateAvailable}
              title={kernelUpdateAvailable ? "升级内核三包并自动重启" : "内核已是最新"}
              style={{
                fontSize: 13, padding: "7px 16px", borderRadius: 8,
                background: kernelUpdateAvailable && !busy ? "var(--accent)" : "var(--bg-panel)",
                color: kernelUpdateAvailable && !busy ? "#fff" : "var(--text-muted)",
                cursor: busy || !kernelUpdateAvailable ? "default" : "pointer",
                opacity: busy || !kernelUpdateAvailable ? 0.55 : 1, fontWeight: 600,
                border: "1px solid var(--border)",
              }}
            >{upgrading ? t("update.upgrading") : t("update.upgradeKernel")}</button>

            <button
              onClick={() => void runUpgrade("app")}
              disabled={busy || !appUpdateAvailable}
              title={appUpdateAvailable ? "下载新版 pi-web 并自动重启替换" : "应用已是最新"}
              style={{
                fontSize: 13, padding: "7px 16px", borderRadius: 8,
                background: appUpdateAvailable && !busy ? "var(--accent)" : "var(--bg-panel)",
                color: appUpdateAvailable && !busy ? "#fff" : "var(--text-muted)",
                cursor: busy || !appUpdateAvailable ? "default" : "pointer",
                opacity: busy || !appUpdateAvailable ? 0.55 : 1, fontWeight: 600,
                border: "1px solid var(--border)",
              }}
            >{upgrading ? t("update.upgrading") : t("update.upgradeApp")}</button>
          </section>

          {error && !upgrading && (
            <div style={{ fontSize: 12, color: "#ef4444", background: "rgba(239,68,68,0.08)", border: "1px solid rgba(239,68,68,0.3)", borderRadius: 7, padding: "8px 10px" }}>
              {error}
            </div>
          )}

          {/* 进度日志 */}
          {(logs.length > 0 || upgrading || restarting) && (
            <section style={{ display: "flex", flexDirection: "column", flex: 1, minHeight: 120 }}>
              <h3 style={{ margin: "0 0 6px", fontSize: 13, fontWeight: 600, color: "var(--text)" }}>
                {restarting ? t("update.restarting") : t("update.progress")}
              </h3>
              <div
                ref={logRef}
                style={{
                  flex: 1, minHeight: 120, overflowY: "auto", background: "var(--bg-panel)",
                  border: "1px solid var(--border)", borderRadius: 8, padding: "8px 10px",
                  fontFamily: "var(--font-mono)", fontSize: 11, lineHeight: 1.6, color: "var(--text-muted)",
                  whiteSpace: "pre-wrap", wordBreak: "break-all",
                }}
              >
                {logs.length === 0 ? t("update.waiting") : logs.join("\n")}
              </div>
            </section>
          )}
        </div>

        {/* 重启遮罩 */}
        {restarting && (
          <div style={{ position: "absolute", inset: 0, background: "rgba(0,0,0,0.55)", display: "flex", alignItems: "center", justifyContent: "center", flexDirection: "column", gap: 12, borderRadius: 10 }}>
            <div style={{ fontSize: 14, color: "#fff", fontWeight: 600 }}>{t("update.restartingOverlay")}</div>
            <div style={{ fontSize: 12, color: "rgba(255,255,255,0.75)" }}>{t("update.restartingOverlayDesc")}</div>
          </div>
        )}
      </div>
    </div>
  );
}
