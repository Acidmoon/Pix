/**
 * 语音输入 hook：浏览器麦克风采集 16kHz PCM，停止后上传
 * /api/voice/transcribe 转写，结果经 onText 回调交给调用方。
 *
 * 用 AudioWorklet 直接拿原始 PCM（绕过 MediaRecorder 的 webm 编解码），
 * worklet 代码以 Blob URL 内联加载，不需要额外的 public 静态文件。
 * 采样率优先直接开 16kHz 的 AudioContext，不支持时按实际采样率
 * 线性插值重采样后再上传。
 */

"use client";

import { useCallback, useEffect, useRef, useState } from "react";

export type VoiceInputState = "idle" | "requesting" | "recording" | "transcribing";

const TARGET_SAMPLE_RATE = 16000;

// AudioWorklet 处理器：把每个输入块的单声道 PCM 拷给主线程
const WORKLET_CODE = `
class PcmCollector extends AudioWorkletProcessor {
  process(inputs) {
    const ch = inputs[0] && inputs[0][0];
    if (ch && ch.length > 0) this.port.postMessage(ch.slice(0));
    return true;
  }
}
registerProcessor("pcm-collector", PcmCollector);
`;

/** 线性插值重采样到 16kHz（AudioContext 不支持自定义采样率时的兜底） */
function resampleTo16k(samples: Float32Array, fromRate: number): Float32Array {
  if (fromRate === TARGET_SAMPLE_RATE) return samples;
  const ratio = TARGET_SAMPLE_RATE / fromRate;
  const outLen = Math.round(samples.length * ratio);
  const out = new Float32Array(outLen);
  for (let i = 0; i < outLen; i++) {
    const pos = i / ratio;
    const lo = Math.floor(pos);
    const hi = Math.min(lo + 1, samples.length - 1);
    out[i] = samples[lo] + (samples[hi] - samples[lo]) * (pos - lo);
  }
  return out;
}

export function useVoiceInput(options: {
  onText: (text: string) => void;
  onError?: (message: string) => void;
}) {
  const { onText, onError } = options;
  const [state, setState] = useState<VoiceInputState>("idle");
  const [seconds, setSeconds] = useState(0);

  const chunksRef = useRef<Float32Array[]>([]);
  const ctxRef = useRef<AudioContext | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  // 用 ref 保存回调，避免闭包过期
  const onTextRef = useRef(onText);
  const onErrorRef = useRef(onError);
  onTextRef.current = onText;
  onErrorRef.current = onError;

  const cleanup = useCallback(() => {
    if (timerRef.current) {
      clearInterval(timerRef.current);
      timerRef.current = null;
    }
    streamRef.current?.getTracks().forEach((t) => t.stop());
    streamRef.current = null;
    void ctxRef.current?.close().catch(() => undefined);
    ctxRef.current = null;
  }, []);

  // 组件卸载时释放麦克风
  useEffect(() => cleanup, [cleanup]);

  const start = useCallback(async () => {
    // 先进入"请求权限中"状态：getUserMedia 在等用户授权时会 pending，
    // 没有这个中间态按钮看起来就像没反应
    setState("requesting");
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true },
      });
      streamRef.current = stream;
      chunksRef.current = [];

      let ctx: AudioContext;
      try {
        ctx = new AudioContext({ sampleRate: TARGET_SAMPLE_RATE });
      } catch {
        ctx = new AudioContext(); // 不支持自定义采样率时用默认值，之后重采样
      }
      ctxRef.current = ctx;

      const workletUrl = URL.createObjectURL(
        new Blob([WORKLET_CODE], { type: "application/javascript" }),
      );
      try {
        await ctx.audioWorklet.addModule(workletUrl);
      } finally {
        URL.revokeObjectURL(workletUrl);
      }
      const source = ctx.createMediaStreamSource(stream);
      const node = new AudioWorkletNode(ctx, "pcm-collector");
      node.port.onmessage = (e: MessageEvent<Float32Array>) => {
        chunksRef.current.push(e.data);
      };
      // 经零增益接到 destination，保证 worklet 持续被拉流
      const mute = ctx.createGain();
      mute.gain.value = 0;
      source.connect(node);
      node.connect(mute);
      mute.connect(ctx.destination);

      setSeconds(0);
      timerRef.current = setInterval(() => setSeconds((s) => s + 1), 1000);
      setState("recording");
    } catch (err) {
      cleanup();
      setState("idle");
      onErrorRef.current?.(
        err instanceof Error ? `无法使用麦克风：${err.message}` : "无法使用麦克风",
      );
    }
  }, [cleanup]);

  const stop = useCallback(async () => {
    if (state !== "recording") return;
    const ctx = ctxRef.current;
    const fromRate = ctx?.sampleRate ?? TARGET_SAMPLE_RATE;
    const chunks = chunksRef.current;
    cleanup();
    setState("transcribing");
    try {
      const total = chunks.reduce((n, c) => n + c.length, 0);
      const merged = new Float32Array(total);
      let offset = 0;
      for (const c of chunks) {
        merged.set(c, offset);
        offset += c.length;
      }
      const samples = resampleTo16k(merged, fromRate);
      const res = await fetch("/api/voice/transcribe", {
        method: "POST",
        headers: { "x-sample-rate": String(TARGET_SAMPLE_RATE) },
        // 包成 Blob，避开 ArrayBufferLike 与 BodyInit 的类型分歧
        body: new Blob([samples.buffer.slice(0) as ArrayBuffer]),
      });
      const data = (await res.json()) as { text?: string; error?: string };
      if (!res.ok) throw new Error(data.error || `转写失败（${res.status}）`);
      const text = (data.text || "").trim();
      if (text) onTextRef.current(text);
    } catch (err) {
      onErrorRef.current?.(err instanceof Error ? err.message : "语音转写失败");
    } finally {
      setState("idle");
    }
  }, [state, cleanup]);

  const toggle = useCallback(() => {
    if (state === "recording") {
      void stop();
    } else if (state === "idle") {
      void start();
    }
  }, [state, start, stop]);

  return { state, seconds, start, stop, toggle };
}
