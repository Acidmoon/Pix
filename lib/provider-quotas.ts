import { AuthStorage } from "@earendil-works/pi-coding-agent";

export interface ProviderAccountSummary {
  id: string;
  displayName: string;
  status: "available" | "not_configured" | "error";
  isAvailable?: boolean;
  balances?: Array<{
    currency: "CNY" | "USD" | string;
    total: number;
    granted: number;
    toppedUp: number;
  }>;
  tiers?: Array<{
    name: "five_hour" | "weekly_limit";
    label: string;
    remainingPercent: number;
    resetsAt?: string;
  }>;
  message?: string;
}

type DeepSeekBalanceResponse = {
  is_available?: boolean;
  balance_infos?: Array<{
    currency?: string;
    total_balance?: string;
    granted_balance?: string;
    topped_up_balance?: string;
  }>;
};

type MiniMaxRemainsResponse = {
  base_resp?: { status_code?: number; status_msg?: string };
  model_remains?: Array<{
    model_name?: string;
    current_interval_remaining_percent?: number;
    end_time?: number;
    current_weekly_status?: number;
    current_weekly_remaining_percent?: number;
    weekly_end_time?: number;
  }>;
};

declare global {
  var __piProviderQuotaCache: { timestamp: number; data: ProviderAccountSummary[] } | undefined;
}

const CACHE_TTL_MS = 60_000;

async function fetchJson<T>(url: string, apiKey: string): Promise<T> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 15_000);
  try {
    const response = await fetch(url, {
      headers: {
        Authorization: `Bearer ${apiKey}`,
        Accept: "application/json",
      },
      cache: "no-store",
      signal: controller.signal,
    });
    if (!response.ok) {
      throw new Error(response.status === 401 || response.status === 403
        ? "凭据无效或已过期"
        : `官方接口返回 HTTP ${response.status}`);
    }
    return await response.json() as T;
  } finally {
    clearTimeout(timeout);
  }
}

function parseAmount(value: string | undefined): number {
  const amount = Number.parseFloat(value ?? "0");
  return Number.isFinite(amount) ? amount : 0;
}

async function queryDeepSeek(apiKey: string | undefined): Promise<ProviderAccountSummary> {
  if (!apiKey) {
    return {
      id: "deepseek",
      displayName: "DeepSeek",
      status: "not_configured",
      message: "尚未配置 DeepSeek API Key",
    };
  }

  try {
    const body = await fetchJson<DeepSeekBalanceResponse>(
      "https://api.deepseek.com/user/balance",
      apiKey,
    );
    return {
      id: "deepseek",
      displayName: "DeepSeek",
      status: "available",
      isAvailable: body.is_available === true,
      balances: (body.balance_infos ?? []).map((balance) => ({
        currency: balance.currency ?? "CNY",
        total: parseAmount(balance.total_balance),
        granted: parseAmount(balance.granted_balance),
        toppedUp: parseAmount(balance.topped_up_balance),
      })),
    };
  } catch (error) {
    return {
      id: "deepseek",
      displayName: "DeepSeek",
      status: "error",
      message: error instanceof Error ? error.message : String(error),
    };
  }
}

function toIsoTimestamp(timestamp: number | undefined): string | undefined {
  if (!timestamp || !Number.isFinite(timestamp)) return undefined;
  try { return new Date(timestamp).toISOString(); }
  catch { return undefined; }
}

async function queryMiniMax(
  providerId: "minimax-cn" | "minimax",
  apiKey: string | undefined,
): Promise<ProviderAccountSummary> {
  const isChina = providerId === "minimax-cn";
  const displayName = isChina ? "MiniMax Token Plan" : "MiniMax Token Plan (Global)";
  if (!apiKey) {
    return {
      id: providerId,
      displayName,
      status: "not_configured",
      message: "尚未配置 MiniMax API Key",
    };
  }

  try {
    const domain = isChina ? "api.minimaxi.com" : "api.minimax.io";
    const body = await fetchJson<MiniMaxRemainsResponse>(
      `https://${domain}/v1/api/openplatform/coding_plan/remains`,
      apiKey,
    );
    if (body.base_resp?.status_code !== undefined && body.base_resp.status_code !== 0) {
      throw new Error(body.base_resp.status_msg || `业务错误 ${body.base_resp.status_code}`);
    }

    const general = body.model_remains?.find((item) => item.model_name === "general");
    if (!general) throw new Error("官方接口未返回编程套餐额度");

    const tiers: NonNullable<ProviderAccountSummary["tiers"]> = [];
    if (typeof general.current_interval_remaining_percent === "number") {
      tiers.push({
        name: "five_hour",
        label: "5 小时额度",
        remainingPercent: Math.max(0, Math.min(100, general.current_interval_remaining_percent)),
        resetsAt: toIsoTimestamp(general.end_time),
      });
    }
    if (
      general.current_weekly_status === 1
      && typeof general.current_weekly_remaining_percent === "number"
    ) {
      tiers.push({
        name: "weekly_limit",
        label: "7 天额度",
        remainingPercent: Math.max(0, Math.min(100, general.current_weekly_remaining_percent)),
        resetsAt: toIsoTimestamp(general.weekly_end_time),
      });
    }

    return {
      id: providerId,
      displayName,
      status: "available",
      tiers,
      ...(!tiers.length ? { message: "当前套餐未返回可展示的额度窗口" } : {}),
    };
  } catch (error) {
    return {
      id: providerId,
      displayName,
      status: "error",
      message: error instanceof Error ? error.message : String(error),
    };
  }
}

/** Query only official provider endpoints; raw credentials never leave the server. */
export async function getProviderAccountSummaries(): Promise<ProviderAccountSummary[]> {
  const cached = globalThis.__piProviderQuotaCache;
  if (cached && Date.now() - cached.timestamp < CACHE_TTL_MS) return cached.data;

  const authStorage = AuthStorage.create();
  const storedKey = (provider: string): string | undefined => {
    const credential = authStorage.get(provider);
    return credential?.type === "api_key" ? credential.key : undefined;
  };
  const deepSeekKey = storedKey("deepseek");
  const miniMaxCnKey = storedKey("minimax-cn");
  const miniMaxGlobalKey = storedKey("minimax");

  const miniMaxProvider = miniMaxCnKey ? "minimax-cn" : "minimax";
  const data = await Promise.all([
    queryDeepSeek(deepSeekKey),
    queryMiniMax(miniMaxProvider, miniMaxCnKey ?? miniMaxGlobalKey),
  ]);
  globalThis.__piProviderQuotaCache = { timestamp: Date.now(), data };
  return data;
}
