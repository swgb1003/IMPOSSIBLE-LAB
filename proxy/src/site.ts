// IMPOSSIBLE LIBRARY のサイトの画面（proxy/site/）と three.js を出す。この PC の中（127.0.0.1）からだけ。
// 画面はきれいなアドレス（/inventions/<ID> など）で出し、部品（CSS・JS・見本）は /site/ の下から出す。
// ログインが要る画面は、ログインしていなければログインの画面へ送る。パスは決めた置き場の中だけを読み、外へ出る指定（..）は受け付けない。
import type { IncomingMessage, ServerResponse } from "node:http";
import { createHash } from "node:crypto";
import { existsSync, readFileSync, statSync } from "node:fs";
import path from "node:path";
import { isLoopback } from "./library/auth.ts";
import type { Who } from "./library/members.ts";

const TYPES: Record<string, string> = {
  ".html": "text/html; charset=utf-8", ".css": "text/css; charset=utf-8", ".js": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8", ".glb": "model/gltf-binary", ".jpg": "image/jpeg", ".png": "image/png",
  ".svg": "image/svg+xml", ".woff2": "font/woff2",
};

/** 画面を開ける人：guest（ログインしていない人。ログイン済みなら自分のページへ）・any・member（開発者か発案者）・operator。 */
type Gate = "guest" | "any" | "member" | "operator";
const PID = "[a-z0-9-]{3,40}";
const PAGES: [RegExp, string, Gate?][] = [
  [/^\/$/, "index.html"],
  [/^\/inventions$/, "list.html"],
  [new RegExp(`^/inventions/${PID}$`), "invention.html"],
  [new RegExp(`^/creators/${PID}$`), "creator.html"],
  [new RegExp(`^/messages(/${PID})?$`), "threads.html", "member"],
  [/^\/login$/, "login.html", "guest"],
  [/^\/signup$/, "signup.html", "guest"],
  [/^\/pair$/, "pair.html", "guest"],
  [/^\/forgot$/, "forgot.html"],
  [/^\/reset$/, "reset.html"],
  [/^\/verify$/, "verify.html"],
  [/^\/me$/, "me.html", "any"],
  [/^\/settings$/, "settings.html", "any"],
  [/^\/notifications$/, "notifications.html", "any"],
  [/^\/admin$/, "admin.html", "operator"],
  [/^\/terms$/, "terms.html"],
  [/^\/privacy$/, "privacy.html"],
  [/^\/mail$/, "mail.html"],
];

/** 前のアドレス（/site/〇〇.html?id=…）から、今のアドレスへ。 */
function legacy(file: string, q: URLSearchParams): string | null {
  const id = q.get("id") ?? "";
  const ok = new RegExp(`^${PID}$`).test(id);
  switch (file) {
    case "index.html": case "title.html": return "/";
    case "list.html": return "/inventions";
    case "invention.html": return ok ? `/inventions/${id}` : "/inventions";
    case "creator.html": return ok ? `/creators/${id}` : "/";
    case "threads.html": return ok ? `/messages/${id}` : "/messages";
    case "terms.html": return "/terms";
    case "admin.html": return "/admin";
    default: return null;
  }
}

/** root の中のファイルだけを返す（無ければ null）。 */
function resolveInside(root: string, rel: string): string | null {
  let decoded: string;
  try { decoded = decodeURIComponent(rel); } catch { return null; }
  if (decoded.includes("\0")) return null;
  const file = path.resolve(root, "." + path.posix.normalize("/" + decoded));
  if (file !== root && !file.startsWith(root + path.sep)) return null;
  return existsSync(file) && statSync(file).isFile() ? file : null;
}

const sha = (s: string) => `'sha256-${createHash("sha256").update(s, "utf8").digest("base64")}'`;

/** 画面のファイルを読み、中に書いた台本（importmap など）のハッシュを許す CSP を作る。変わったときだけ読み直す。 */
const pageCache = new Map<string, { mtime: number; body: Buffer; csp: string }>();
function page(file: string) {
  const mtime = statSync(file).mtimeMs;
  const hit = pageCache.get(file);
  if (hit && hit.mtime === mtime) return hit;
  const html = readFileSync(file, "utf8");
  const inline = [...html.matchAll(/<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)<\/script>/g)].map((x) => sha(x[1]));
  const csp = [
    "default-src 'self'",
    `script-src 'self' ${inline.join(" ")}`.trim(),
    "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
    "font-src https://fonts.gstatic.com",
    "img-src 'self' data: blob:",
    "connect-src 'self' blob: data:",
    "object-src 'none'", "base-uri 'none'", "form-action 'self'", "frame-ancestors 'none'",
  ].join("; ");
  const v = { mtime, body: Buffer.from(html, "utf8"), csp };
  pageCache.set(file, v);
  return v;
}

const SECURITY = {
  "x-content-type-options": "nosniff", "x-frame-options": "DENY", "referrer-policy": "same-origin",
  "cross-origin-opener-policy": "same-origin", "permissions-policy": "camera=(), microphone=(), geolocation=()",
};

export function createSite(siteDir: string, threeDir: string, sessionOf: (req: IncomingMessage) => Who | null = () => null) {
  const site = path.resolve(siteDir);
  const three = path.resolve(threeDir);

  const sendPage = (req: IncomingMessage, res: ServerResponse, name: string, status = 200) => {
    const file = resolveInside(site, name);
    if (!file) { res.writeHead(404, { "content-type": "text/plain; charset=utf-8" }); res.end("not found"); return; }
    const p = page(file);
    res.writeHead(status, { ...SECURITY, "content-type": TYPES[".html"], "content-length": p.body.length, "cache-control": "no-store", "content-security-policy": p.csp });
    res.end(req.method === "HEAD" ? undefined : p.body);
  };
  const redirect = (res: ServerResponse, to: string, status = 302) => { res.writeHead(status, { location: to, "cache-control": "no-store" }); res.end(); };

  /** サイトの入口なら返して true。 */
  return function serve(req: IncomingMessage, res: ServerResponse, url: URL): boolean {
    if (req.method !== "GET" && req.method !== "HEAD") return false;
    const p = url.pathname;
    if (!isLoopback(req)) { res.writeHead(403, { "content-type": "text/plain; charset=utf-8" }); res.end("この PC の中からだけ開けます"); return true; }

    // 画面（きれいなアドレス）
    const hit = PAGES.find(([re]) => re.test(p));
    if (hit) {
      const [, name, gate] = hit;
      const who = gate ? sessionOf(req) : null;
      const back = encodeURIComponent(p + url.search);
      if (gate === "guest" && who) { redirect(res, who.kind === "operator" ? "/admin" : "/me"); return true; }
      if ((gate === "any" || gate === "member" || gate === "operator") && !who) { redirect(res, `/login?next=${back}`); return true; }
      if (gate === "member" && who?.kind === "operator") { sendPage(req, res, "403.html", 403); return true; }
      if (gate === "operator" && who?.kind !== "operator") { sendPage(req, res, "403.html", 403); return true; }
      sendPage(req, res, name);
      return true;
    }

    // 部品
    let file: string | null = null;
    if (p.startsWith("/site/")) {
      const rel = p.slice("/site/".length);
      if (rel === "" || rel.endsWith(".html")) {
        const to = legacy(rel || "index.html", url.searchParams);
        if (to) { redirect(res, to, 301); return true; }
        sendPage(req, res, "404.html", 404);
        return true;
      }
      file = resolveInside(site, rel);
    } else if (p.startsWith("/vendor/three/build/") || p.startsWith("/vendor/three/examples/jsm/")) {
      file = resolveInside(three, p.slice("/vendor/three/".length));
    } else {
      // どれでもないアドレス：画面として読みに来たなら「見つかりません」の画面
      if (String(req.headers.accept ?? "").includes("text/html")) { sendPage(req, res, "404.html", 404); return true; }
      return false;
    }
    const type = file ? TYPES[path.extname(file).toLowerCase()] : undefined;
    if (!file || !type) { res.writeHead(404, { "content-type": "text/plain; charset=utf-8" }); res.end("not found"); return true; }
    const body = readFileSync(file);
    res.writeHead(200, { "content-type": type, "content-length": body.length, "cache-control": "no-cache", "x-content-type-options": "nosniff" });
    res.end(req.method === "HEAD" ? undefined : body);
    return true;
  };
}
