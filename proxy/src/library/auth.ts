// サイトのログインに使う道具：パスワードのハッシュ、ログインの記録（cookie）、宛先名と送り元の確認。
// パスワードはそのまま保存せず、scrypt で元に戻せない形にする。ログインの記録の札もハッシュだけを保存する。
import { createHash, randomBytes, randomInt, scryptSync, timingSafeEqual } from "node:crypto";
import type { IncomingMessage, ServerResponse } from "node:http";

const N = 16384, R = 8, P = 1, KEYLEN = 32;

export function hashPassword(password: string): string {
  const salt = randomBytes(16);
  const h = scryptSync(password.normalize("NFKC"), salt, KEYLEN, { N, r: R, p: P });
  return `scrypt$${N}$${R}$${P}$${salt.toString("base64url")}$${h.toString("base64url")}`;
}

export function checkPassword(password: string, stored: string | undefined): boolean {
  const m = /^scrypt\$(\d+)\$(\d+)\$(\d+)\$([A-Za-z0-9_-]+)\$([A-Za-z0-9_-]+)$/.exec(stored ?? "");
  if (!m) {
    // 登録が無いときも同じだけ時間をかける（あるかどうかを時間で見分けられないように）
    scryptSync(password.normalize("NFKC"), "no-account", KEYLEN, { N, r: R, p: P });
    return false;
  }
  const want = Buffer.from(m[5], "base64url");
  const got = scryptSync(password.normalize("NFKC"), Buffer.from(m[4], "base64url"), want.length, { N: Number(m[1]), r: Number(m[2]), p: Number(m[3]) });
  return got.length === want.length && timingSafeEqual(got, want);
}

/** ログインの記録・確認のメール・再設定の札（推測できない乱数）。 */
export const newToken = () => randomBytes(32).toString("base64url");
export const tokenHash = (t: string) => createHash("sha256").update("token:" + t, "utf8").digest("hex");
/** Quest に出す6桁の番号。 */
export const newPairCode = () => String(randomInt(0, 1_000_000)).padStart(6, "0");

export const SESSION_COOKIE = "ilab_sid";
export const SESSION_DAYS = 14;

export function readCookie(req: IncomingMessage, name: string): string | null {
  for (const part of String(req.headers.cookie ?? "").split(";")) {
    const i = part.indexOf("=");
    if (i > 0 && part.slice(0, i).trim() === name) return part.slice(i + 1).trim() || null;
  }
  return null;
}

/** ログインの記録を渡す。ページのプログラムからは読めず（HttpOnly）、ほかのサイトからの要求には付かない（SameSite=Strict）。 */
export function setSessionCookie(res: ServerResponse, token: string | null) {
  const v = token
    ? `${SESSION_COOKIE}=${token}; Path=/; HttpOnly; SameSite=Strict; Max-Age=${SESSION_DAYS * 86400}`
    : `${SESSION_COOKIE}=; Path=/; HttpOnly; SameSite=Strict; Max-Age=0`;
  res.setHeader("set-cookie", v);
}

export function isLoopback(req: IncomingMessage) {
  const a = req.socket.remoteAddress ?? "";
  return a === "127.0.0.1" || a === "::1" || a === "::ffff:127.0.0.1";
}

const LOOPBACK_HOST = /^(127\.0\.0\.1|localhost|\[::1\])(:\d{1,5})?$/i;

/**
 * この PC の中からの要求は、宛先名（Host）がこの PC の名前のときだけ受け付ける。
 * ほかの名前をこの PC に向ける手口（DNS リバインディング）で、外のサイトからデータを読まれないようにする。
 */
export function hostAllowed(req: IncomingMessage): boolean {
  if (!isLoopback(req)) return true; // LAN からの要求はトークンで確かめる
  return LOOPBACK_HOST.test(String(req.headers.host ?? ""));
}

/** 書き込みの要求で送り元のページ（Origin）が付いていれば、同じサイトのページのときだけ受け付ける。 */
export function originAllowed(req: IncomingMessage): boolean {
  const origin = req.headers.origin;
  if (origin == null) return true; // Quest・コマンドからの要求（ブラウザは書き込みに必ず付ける）
  try { return new URL(String(origin)).host.toLowerCase() === String(req.headers.host ?? "").toLowerCase(); } catch { return false; }
}

/** 失敗が続いたら、しばらく待たせる（ログインと番号の入力）。 */
export class FailureGate {
  private fails = new Map<string, number[]>();
  private max: number;
  private windowMs: number;
  constructor(max: number, windowMs: number) { this.max = max; this.windowMs = windowMs; }
  blocked(key: string, now = Date.now()) {
    const xs = (this.fails.get(key) ?? []).filter((t) => now - t < this.windowMs);
    this.fails.set(key, xs);
    return xs.length >= this.max;
  }
  fail(key: string, now = Date.now()) { this.fails.set(key, [...(this.fails.get(key) ?? []).filter((t) => now - t < this.windowMs), now]); }
  clear(key: string) { this.fails.delete(key); }
}
