/**
 * 语音转写服务（SenseVoice / sherpa-onnx-node）。
 *
 * 识别器是重资源（239MB 模型），做成进程级单例并挂在 globalThis 上，
 * 遵循 AGENTS.md 的抗 Next.js 热重载约定。
 *
 * 模型目录：环境变量 PIX_VOICE_MODEL_DIR 可覆盖，默认 <repo>/models/sensevoice-small-int8。
 * 模型来源：https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17
 */

import fs from "fs";
import path from "path";
import { createRequire } from "module";

const require = createRequire(import.meta.url);

// eslint-disable-next-line @typescript-eslint/no-explicit-any
type Recognizer = any;

const globalForVoice = globalThis as unknown as { __voiceRecognizer?: Recognizer };

export function getVoiceModelDir(): string {
  return (
    process.env.PIX_VOICE_MODEL_DIR ||
    path.join(process.cwd(), "models", "sensevoice-small-int8")
  );
}

/** 模型就绪状态：用于 UI 引导用户下载模型 */
export function getVoiceModelStatus(): {
  ready: boolean;
  modelDir: string;
  missing: string[];
} {
  const modelDir = getVoiceModelDir();
  const missing = ["model.int8.onnx", "tokens.txt"].filter(
    (file) => !fs.existsSync(path.join(modelDir, file)),
  );
  return { ready: missing.length === 0, modelDir, missing };
}

/** 加载（或复用）SenseVoice 识别器单例。模型缺失时抛错。 */
export function getVoiceRecognizer(): Recognizer {
  if (globalForVoice.__voiceRecognizer) {
    return globalForVoice.__voiceRecognizer;
  }
  const modelDir = getVoiceModelDir();
  const model = path.join(modelDir, "model.int8.onnx");
  const tokens = path.join(modelDir, "tokens.txt");
  if (!fs.existsSync(model) || !fs.existsSync(tokens)) {
    throw new Error(
      `语音模型缺失：${modelDir}（需要 model.int8.onnx 和 tokens.txt，` +
        `可用 PIX_VOICE_MODEL_DIR 指定模型目录）`,
    );
  }
  // 原生 .node 模块，运行时加载（next.config.ts 已标 serverExternalPackages）
  const so = require("sherpa-onnx-node");
  const recognizer = new so.OfflineRecognizer({
    featConfig: { sampleRate: 16000, featureDim: 80 },
    modelConfig: {
      senseVoice: { model, useInverseTextNormalization: 1 },
      tokens,
      numThreads: 4,
      provider: "cpu",
      debug: 0,
    },
  });
  globalForVoice.__voiceRecognizer = recognizer;
  return recognizer;
}

/**
 * 转写一段 PCM 音频，返回识别文本。
 * samples 为 Float32 单声道，取值 [-1.0, 1.0]；sampleRate 通常 16000。
 */
export function transcribePcm(samples: Float32Array, sampleRate: number): string {
  const recognizer = getVoiceRecognizer();
  const stream = recognizer.createStream();
  stream.acceptWaveform({ sampleRate, samples });
  recognizer.decode(stream);
  return (recognizer.getResult(stream).text || "").trim();
}
