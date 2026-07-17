import { timingSafeEqual } from "crypto";

/** Validate the per-launch random token supplied only to the native launcher. */
export function isLauncherAuthorized(req: Request): boolean {
  const expected = process.env.PI_WEB_LAUNCHER_TOKEN;
  const authorization = req.headers.get("authorization");
  const provided = authorization?.startsWith("Bearer ")
    ? authorization.slice("Bearer ".length)
    : "";

  if (!expected || !provided) return false;
  const expectedBytes = Buffer.from(expected);
  const providedBytes = Buffer.from(provided);
  return expectedBytes.length === providedBytes.length
    && timingSafeEqual(expectedBytes, providedBytes);
}
