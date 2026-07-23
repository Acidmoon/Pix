import { Suspense } from "react";
import { AppShell } from "@/components/AppShell";
import { PixI18nProvider } from "@/lib/i18n";

export default function Home() {
  return (
    <Suspense>
      <PixI18nProvider>
        <AppShell />
      </PixI18nProvider>
    </Suspense>
  );
}
