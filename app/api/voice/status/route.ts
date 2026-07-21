/**
 * 语音模型状态：GET 返回 { ready, modelDir, missing }，
 * 前端据此在麦克风按钮上引导用户下载模型。
 */

import { getVoiceModelStatus } from "@/lib/voice";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export async function GET() {
  return Response.json(getVoiceModelStatus());
}
