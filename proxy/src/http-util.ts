// 入力の検証とレート制限（Claude を呼ぶ前に弾く）。
import type { IncomingMessage } from "node:http";

export class HttpError extends Error {
  status: number;
  code: string;
  constructor(status: number, code: string, message: string) {
    super(message);
    this.status = status;
    this.code = code;
  }
}

export function checkRequestText(v: unknown, maxLength: number): string {
  if (typeof v !== "string" || v.trim().length === 0) throw new HttpError(400, "empty_request", "指示が空です");
  const t = v.trim();
  if ([...t].length > maxLength) throw new HttpError(400, "request_too_long", `指示が長すぎます（${maxLength}文字以内）`);
  return t;
}

/** 実験結果は既知の数値項目だけを Claude に渡す。 */
export function pickExperiment(v: unknown): Record<string, number | string> | null {
  if (typeof v !== "object" || v === null) return null;
  const src = v as Record<string, unknown>;
  const out: Record<string, number | string> = {};
  for (const k of ["coverageRatio", "wetnessScore", "headHits", "upperHits", "lowerHits", "baselineHits", "deviceHits",
    "driftM", "rainIntensity", "windSpeed", "windDirectionDeg", "runtimeMinutes",
    // 運搬（ObstacleCourse）
    "collisionCount", "arrivalDelay", "followError", "averageFollowDistance", "maxGap", "energyWh", "obstacleCount", "payloadKg",
    // 移動（FlightDeck）
    "completionTime", "stabilityScore", "maxDeviation", "landingError", "maxAltitude", "requiredPowerKW", "availableFlightSec", "targetDistance"]) {
    const x = src[k];
    if (typeof x === "number" && Number.isFinite(x)) out[k] = Math.round(x * 1000) / 1000;
  }
  if (src.mode === "dream" || src.mode === "reality") out.mode = src.mode;
  if (src.environment === "RainLab" || src.environment === "ObstacleCourse" || src.environment === "FlightDeck") out.environment = src.environment;
  return Object.keys(out).length ? out : null;
}

export function readJsonBody(req: IncomingMessage, maxBytes: number): Promise<unknown> {
  return new Promise((resolve, reject) => {
    const type = String(req.headers["content-type"] ?? "");
    if (!type.startsWith("application/json")) return reject(new HttpError(415, "unsupported_media_type", "JSON で送ってください"));
    let size = 0;
    const chunks: Buffer[] = [];
    req.on("data", (c: Buffer) => {
      size += c.length;
      if (size > maxBytes) { reject(new HttpError(413, "too_large", "リクエストが大きすぎます")); req.destroy(); return; }
      chunks.push(c);
    });
    req.on("end", () => {
      try { resolve(JSON.parse(Buffer.concat(chunks).toString("utf8"))); }
      catch { reject(new HttpError(400, "bad_json", "JSON が不正です")); }
    });
    req.on("error", reject);
  });
}

/** 固定時間窓の簡易レート制限（プロキシ全体）。 */
export class RateLimiter {
  private stamps: number[] = [];
  private limit: number;
  private windowMs: number;
  constructor(limit: number, windowMs: number) { this.limit = limit; this.windowMs = windowMs; }
  take(now = Date.now()): boolean {
    this.stamps = this.stamps.filter((t) => now - t < this.windowMs);
    if (this.stamps.length >= this.limit) return false;
    this.stamps.push(now);
    return true;
  }
}
