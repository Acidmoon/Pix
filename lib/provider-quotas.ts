import { ModelRuntime } from "@earendil-works/pi-coding-agent";
import { readFileSync, writeFileSync, renameSync, existsSync } from "fs";

/**
 * 厂商额度/余额查询（注册表 + env 配置）。
 *
 * 配置全部走环境变量，每个厂商一组 QUOTA_<STEM>_* 变量（见 .env.example）：
 *   QUOTA_<STEM>_NAME           展示名
 *   QUOTA_<STEM>_URL            查询端点（MiniMax 这类双区厂商用 _CN_URL 和 _URL 两条）
 *   QUOTA_<STEM>_KEY_PROVIDERS  pi auth 存储里的密钥来源 id，逗号分隔，取第一个有 key 的
 * 代码内的默认值与 .env.example 一致；.env（不入库）可覆盖。
 *
 * 新增厂商 = 在 .env 加一组配置 + 在 ADAPTERS 注册一个适配器（解析逻辑），
 * 框架其余部分（API 路由、C# 启动器、契约类型）不用动。
 */

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

/** 适配器静态配置（全部来自 env） */
interface AdapterConfig {
  id: string;
  displayName: string;
  keyProviders: string[];
  /** 适配器私有配置（端点等），各适配器自取 */
  extras: Record<string, string>;
}

interface AdapterContext {
  config: AdapterConfig;
  apiKey?: string;
  /** 实际命中密钥的来源 id（keyProviders 之一） */
  authProvider: string;
}

interface ProviderQuotaAdapter {
  config(): AdapterConfig;
  query(ctx: AdapterContext): Promise<ProviderAccountSummary>;
}

declare global {
  var __piProviderQuotaCache: { timestamp: number; data: ProviderAccountSummary[] } | undefined;
}

const CACHE_TTL_MS = 60_000;

function envOr(name: string, fallback: string): string {
  const value = process.env[name];
  return value && value.trim() ? value.trim() : fallback;
}

function envList(name: string, fallback: string): string[] {
  return envOr(name, fallback).split(",").map((s) => s.trim()).filter(Boolean);
}

async function fetchJson<T>(
  url: string,
  apiKey: string,
  options: { method?: string; body?: string; headers?: Record<string, string> } = {},
): Promise<T> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 15_000);
  try {
    const response = await fetch(url, {
      method: options.method ?? "GET",
      body: options.body,
      headers: {
        Authorization: `Bearer ${apiKey}`,
        Accept: "application/json",
        ...options.headers,
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

function toIsoTimestamp(timestamp: number | undefined): string | undefined {
  if (!timestamp || !Number.isFinite(timestamp)) return undefined;
  try { return new Date(timestamp).toISOString(); }
  catch { return undefined; }
}

function notConfigured(config: AdapterConfig): ProviderAccountSummary {
  return {
    id: config.id,
    displayName: config.displayName,
    status: "not_configured",
    message: `尚未配置 ${config.displayName} API Key`,
  };
}

function queryError(config: AdapterConfig, error: unknown): ProviderAccountSummary {
  return {
    id: config.id,
    displayName: config.displayName,
    status: "error",
    message: error instanceof Error ? error.message : String(error),
  };
}

// ---- DeepSeek ----

type DeepSeekBalanceResponse = {
  is_available?: boolean;
  balance_infos?: Array<{
    currency?: string;
    total_balance?: string;
    granted_balance?: string;
    topped_up_balance?: string;
  }>;
};

const deepseekAdapter: ProviderQuotaAdapter = {
  config: () => ({
    id: "deepseek",
    displayName: envOr("QUOTA_DEEPSEEK_NAME", "DeepSeek"),
    keyProviders: envList("QUOTA_DEEPSEEK_KEY_PROVIDERS", "deepseek"),
    extras: {
      url: envOr("QUOTA_DEEPSEEK_URL", "https://api.deepseek.com/user/balance"),
    },
  }),
  async query({ config, apiKey }) {
    if (!apiKey) return notConfigured(config);
    try {
      const body = await fetchJson<DeepSeekBalanceResponse>(config.extras.url, apiKey);
      return {
        id: config.id,
        displayName: config.displayName,
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
      return queryError(config, error);
    }
  },
};

// ---- MiniMax（国内/国际双区，取第一个有 key 的区） ----

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

const miniMaxAdapter: ProviderQuotaAdapter = {
  config: () => ({
    id: "minimax",
    displayName: envOr("QUOTA_MINIMAX_NAME", "MiniMax Token Plan"),
    keyProviders: envList("QUOTA_MINIMAX_KEY_PROVIDERS", "minimax-cn,minimax"),
    extras: {
      urlCn: envOr("QUOTA_MINIMAX_CN_URL", "https://api.minimaxi.com/v1/api/openplatform/coding_plan/remains"),
      urlGlobal: envOr("QUOTA_MINIMAX_URL", "https://api.minimax.io/v1/api/openplatform/coding_plan/remains"),
    },
  }),
  async query({ config, apiKey, authProvider }) {
    const isChina = authProvider === "minimax-cn";
    const displayName = isChina ? config.displayName : `${config.displayName} (Global)`;
    const scopedConfig = { ...config, id: authProvider, displayName };
    if (!apiKey) return notConfigured(scopedConfig);

    try {
      const url = isChina ? config.extras.urlCn : config.extras.urlGlobal;
      const body = await fetchJson<MiniMaxRemainsResponse>(url, apiKey);
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
        id: scopedConfig.id,
        displayName,
        status: "available",
        tiers,
        ...(!tiers.length ? { message: "当前套餐未返回可展示的额度窗口" } : {}),
      };
    } catch (error) {
      return queryError(scopedConfig, error);
    }
  },
};

// ---- Kimi（本地 refresh_token 文件 → 换 access_token → GetSubscriptionStats） ----
// 移植自 kimi_quota.py：refresh_token 每次刷新会轮换，必须把新值写回文件。

type KimiStatsResponse = {
  ratelimitCode5h?: { enabled?: boolean; ratio?: number; resetTime?: string };
  ratelimitCode7d?: { enabled?: boolean; ratio?: number; resetTime?: string };
  subscriptionBalance?: { amountUsedRatio?: number; expireTime?: string; kimiCodeUsedRatio?: number };
  giftBalances?: Array<{ amountUsedRatio?: number; expireTime?: string }>;
};

const KIMI_ORIGIN = "https://www.kimi.com";

/** 读 refresh_token 文件；文件不存在返回 undefined */
function loadKimiRefreshToken(tokenFile: string): string | undefined {
  if (!tokenFile || !existsSync(tokenFile)) return undefined;
  const token = readFileSync(tokenFile, "utf-8").trim();
  return token || undefined;
}

/** 原子写回轮换后的 refresh_token（先写临时文件再替换） */
function saveKimiRefreshToken(tokenFile: string, token: string): void {
  const tmp = `${tokenFile}.tmp`;
  writeFileSync(tmp, token, "utf-8");
  renameSync(tmp, tokenFile);
}

/** 用量比例 → 剩余百分比（0-100，截断） */
function remainingPercent(usedRatio: number | undefined): number {
  const ratio = typeof usedRatio === "number" ? usedRatio : 0;
  return Math.max(0, Math.min(100, (1 - ratio) * 100));
}

function formatLocal(iso: string | undefined): string | undefined {
  if (!iso) return undefined;
  try {
    return new Date(iso).toLocaleString("zh-CN", { hour12: false });
  } catch {
    return iso;
  }
}

const kimiAdapter: ProviderQuotaAdapter = {
  config: () => ({
    id: "kimi",
    displayName: envOr("QUOTA_KIMI_NAME", "Kimi"),
    keyProviders: [], // Kimi 凭据不走 pi auth 存储，用本地 refresh_token 文件
    extras: {
      refreshUrl: envOr("QUOTA_KIMI_REFRESH_URL", `${KIMI_ORIGIN}/api/auth/token/refresh`),
      statsUrl: envOr("QUOTA_KIMI_STATS_URL", `${KIMI_ORIGIN}/apiv2/kimi.gateway.membership.v2.MembershipService/GetSubscriptionStats`),
      tokenFile: envOr("QUOTA_KIMI_TOKEN_FILE", ""),
    },
  }),
  async query({ config }) {
    const tokenFile = config.extras.tokenFile;
    const refreshToken = loadKimiRefreshToken(tokenFile);
    if (!refreshToken) {
      return {
        id: config.id,
        displayName: config.displayName,
        status: "not_configured",
        message: tokenFile
          ? `未找到 refresh_token 文件：${tokenFile}（从 www.kimi.com 的 localStorage 取 refresh_token 写入）`
          : "未配置 QUOTA_KIMI_TOKEN_FILE（refresh_token 文件路径）",
      };
    }

    const kimiHeaders = {
      "Content-Type": "application/json",
      Origin: KIMI_ORIGIN,
      Referer: `${KIMI_ORIGIN}/membership/subscription?tab=quota`,
      "User-Agent": "Mozilla/5.0",
    };
    try {
      // 刷新接口是 GET，鉴权头带 refresh_token
      const refreshed = await fetchJson<{ access_token?: string; refresh_token?: string }>(
        config.extras.refreshUrl,
        refreshToken,
        { headers: kimiHeaders },
      );
      if (!refreshed.access_token) throw new Error("刷新失败，返回里没有 access_token");
      if (refreshed.refresh_token && refreshed.refresh_token !== refreshToken) {
        saveKimiRefreshToken(tokenFile, refreshed.refresh_token); // 关键：持久化轮换后的 token
      }

      const stats = await fetchJson<KimiStatsResponse>(
        config.extras.statsUrl,
        refreshed.access_token,
        { method: "POST", body: "{}", headers: kimiHeaders },
      );

      const tiers: NonNullable<ProviderAccountSummary["tiers"]> = [];
      if (stats.ratelimitCode5h?.enabled) {
        tiers.push({
          name: "five_hour",
          label: "Code 5 小时",
          remainingPercent: remainingPercent(stats.ratelimitCode5h.ratio),
          resetsAt: stats.ratelimitCode5h.resetTime,
        });
      }
      if (stats.ratelimitCode7d?.enabled) {
        tiers.push({
          name: "weekly_limit",
          label: "Code 7 天",
          remainingPercent: remainingPercent(stats.ratelimitCode7d.ratio),
          resetsAt: stats.ratelimitCode7d.resetTime,
        });
      }

      // 订阅总额度和赠送额度没有对应的 tier 槽位，汇总进 message
      const notes: string[] = [];
      const sub = stats.subscriptionBalance;
      if (sub && typeof sub.amountUsedRatio === "number") {
        notes.push(
          `订阅总额度剩余 ${remainingPercent(sub.amountUsedRatio).toFixed(0)}%`
          + (sub.expireTime ? ` · 到期 ${formatLocal(sub.expireTime)}` : ""),
        );
      }
      for (const [index, gift] of (stats.giftBalances ?? []).entries()) {
        const tag = (stats.giftBalances?.length ?? 0) > 1 ? `赠送额度#${index + 1}` : "赠送额度";
        notes.push(
          `${tag}剩余 ${remainingPercent(gift.amountUsedRatio).toFixed(0)}%`
          + (gift.expireTime ? ` · 到期 ${formatLocal(gift.expireTime)}` : ""),
        );
      }

      return {
        id: config.id,
        displayName: config.displayName,
        status: "available",
        tiers,
        ...(notes.length ? { message: notes.join("；") } : {}),
      };
    } catch (error) {
      return queryError(config, error);
    }
  },
};

/** 厂商注册表：新增厂商在这里加一条适配器（配置走 env） */
const ADAPTERS: ProviderQuotaAdapter[] = [
  deepseekAdapter,
  miniMaxAdapter,
  kimiAdapter,
];

/** Query only official provider endpoints; raw credentials never leave the server. */
export async function getProviderAccountSummaries(): Promise<ProviderAccountSummary[]> {
  const cached = globalThis.__piProviderQuotaCache;
  if (cached && Date.now() - cached.timestamp < CACHE_TTL_MS) return cached.data;

  const modelRuntime = await ModelRuntime.create();
  const data = await Promise.all(ADAPTERS.map(async (adapter) => {
    const config = adapter.config();
    // 取第一个配置了 key 的来源 id
    let apiKey: string | undefined;
    let authProvider = config.keyProviders[0] ?? config.id;
    for (const provider of config.keyProviders) {
      const result = await modelRuntime.getAuth(provider);
      if (result?.auth.apiKey) {
        apiKey = result.auth.apiKey;
        authProvider = provider;
        break;
      }
    }
    return adapter.query({ config, apiKey, authProvider });
  }));
  globalThis.__piProviderQuotaCache = { timestamp: Date.now(), data };
  return data;
}
