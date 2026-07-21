/**
 * 语音转写 API：POST 原始 Float32 PCM（application/octet-stream），
 * 请求头 X-Sample-Rate 指明采样率（默认 16000），返回 { text }。
 *
 * 客户端（hooks/useVoiceInput.ts）负责把麦克风音频整理成这个格式。
 */

import { transcribePcm } from "@/lib/voice";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export async function POST(req: Request) {
  try {
    const sampleRate = parseInt(req.headers.get("x-sample-rate") || "16000", 10);
    if (!Number.isFinite(sampleRate) || sampleRate <= 0) {
      return Response.json({ error: "非法的 X-Sample-Rate" }, { status: 400 });
    }
    const buf = Buffer.from(await req.arrayBuffer());
    if (buf.byteLength < 4 || buf.byteLength % 4 !== 0) {
      return Response.json({ error: "音频数据为空或格式不对" }, { status: 400 });
    }
    // 按 Float32 小端解读原始 PCM
    const samples = new Float32Array(
      buf.buffer,
      buf.byteOffset,
      buf.byteLength / 4,
    );
    const text = transcribePcm(samples, sampleRate);
    return Response.json({ text });
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    return Response.json({ error: message }, { status: 500 });
  }
}
