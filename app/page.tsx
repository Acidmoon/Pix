import { Suspense } from "react";
import { AppShell } from "@/components/AppShell";
import { PixI18nProvider } from "@/lib/i18n";
import { I18nProvider } from "@/hooks/useI18n";

export default function Home() {
  return (
    <Suspense>
      {/* 两套 i18n 并存：PixI18nProvider 服务本 fork 的 useT 文案，
          I18nProvider 服务上游新增组件的 useI18n 文案 */}
      <PixI18nProvider>
        <I18nProvider>
          <AppShell />
        </I18nProvider>
      </PixI18nProvider>
    </Suspense>
  );
}
