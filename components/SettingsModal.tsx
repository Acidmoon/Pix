"use client";

import { useIsMobile } from "@/hooks/useIsMobile";
import { useT } from "@/lib/i18n";

interface SettingsModalProps {
  onClose: () => void;
  onOpenSkills: () => void;
  onOpenPlugins: () => void;
  onOpenUpdate: () => void;
  updateAvailable?: boolean;
}

export function SettingsModal({ onClose, onOpenSkills, onOpenPlugins, onOpenUpdate, updateAvailable }: SettingsModalProps) {
  const isMobile = useIsMobile();
  const { t, lang, setLang } = useT();

  const items = [
    {
      label: t("settings.skills"),
      desc: t("settings.skillsDesc"),
      icon: (
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M12 2L2 7l10 5 10-5-10-5z" />
          <path d="M2 17l10 5 10-5" />
          <path d="M2 12l10 5 10-5" />
        </svg>
      ),
      onClick: onOpenSkills,
    },
    {
      label: t("settings.plugins"),
      desc: t("settings.pluginsDesc"),
      icon: (
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M9 7V2" />
          <path d="M15 7V2" />
          <path d="M6 13V8a1 1 0 0 1 1-1h10a1 1 0 0 1 1 1v5a6 6 0 0 1-12 0Z" />
          <path d="M12 19v3" />
        </svg>
      ),
      onClick: onOpenPlugins,
    },
    {
      label: t("settings.update"),
      desc: t("settings.updateDesc"),
      badge: updateAvailable,
      icon: (
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M21 12a9 9 0 1 1-3-6.7L21 8" />
          <path d="M21 3v5h-5" />
        </svg>
      ),
      onClick: onOpenUpdate,
    },
  ];

  return (
    <div
      style={{ position: "fixed", inset: 0, zIndex: 1000, background: "rgba(0,0,0,0.35)", display: "flex", alignItems: "center", justifyContent: "center" }}
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
    >
      <div style={{
        width: isMobile ? "calc(100vw - 16px)" : 420, maxWidth: "calc(100vw - 16px)",
        background: "var(--bg)", border: "1px solid var(--border)", borderRadius: 10,
        display: "flex", flexDirection: "column", boxShadow: "0 8px 32px rgba(0,0,0,0.18)", overflow: "hidden",
      }}>
        {/* Header */}
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", padding: "12px 18px", borderBottom: "1px solid var(--border)", flexShrink: 0 }}>
          <span style={{ fontSize: 15, fontWeight: 700, color: "var(--text)" }}>{t("settings.title")}</span>
          <button onClick={onClose} style={{ background: "none", border: "none", color: "var(--text-muted)", cursor: "pointer", fontSize: 20, lineHeight: 1, padding: "2px 6px" }}>×</button>
        </div>

        {/* Items */}
        <div style={{ padding: "8px 12px", display: "flex", flexDirection: "column", gap: 4 }}>
          {items.map((item) => (
            <button
              key={item.label}
              onClick={item.onClick}
              style={{
                display: "flex", alignItems: "center", gap: 12, width: "100%",
                padding: "10px 12px", background: "none", border: "none",
                borderRadius: 8, color: "var(--text)", cursor: "pointer",
                textAlign: "left", position: "relative",
              }}
              onMouseEnter={(e) => { e.currentTarget.style.background = "var(--bg-hover)"; }}
              onMouseLeave={(e) => { e.currentTarget.style.background = "none"; }}
            >
              <span style={{ color: "var(--text-muted)", display: "flex", alignItems: "center", flexShrink: 0 }}>{item.icon}</span>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontSize: 13, fontWeight: 500 }}>{item.label}</div>
                <div style={{ fontSize: 11, color: "var(--text-muted)", marginTop: 1 }}>{item.desc}</div>
              </div>
              {item.badge && (
                <span style={{ width: 8, height: 8, borderRadius: "50%", background: "#ef4444", flexShrink: 0 }} />
              )}
            </button>
          ))}
        </div>

        {/* Language toggle */}
        <div style={{ padding: "8px 12px 12px", borderTop: "1px solid var(--border)", display: "flex", alignItems: "center", gap: 10 }}>
          <span style={{ fontSize: 12, color: "var(--text-muted)", flexShrink: 0 }}>🌐</span>
          <span style={{ fontSize: 12, color: "var(--text-muted)" }}>{t("settings.language")}</span>
          <div style={{ flex: 1 }} />
          <button
            onClick={() => setLang("zh")}
            style={{
              padding: "3px 10px", borderRadius: 6, fontSize: 12, border: lang === "zh" ? "1px solid var(--accent)" : "1px solid var(--border)",
              background: lang === "zh" ? "var(--accent)" : "var(--bg-panel)",
              color: lang === "zh" ? "#fff" : "var(--text-muted)", cursor: "pointer", fontWeight: lang === "zh" ? 600 : 400,
            }}
          >中文</button>
          <button
            onClick={() => setLang("en")}
            style={{
              padding: "3px 10px", borderRadius: 6, fontSize: 12, border: lang === "en" ? "1px solid var(--accent)" : "1px solid var(--border)",
              background: lang === "en" ? "var(--accent)" : "var(--bg-panel)",
              color: lang === "en" ? "#fff" : "var(--text-muted)", cursor: "pointer", fontWeight: lang === "en" ? 600 : 400,
            }}
          >English</button>
        </div>
      </div>
    </div>
  );
}
