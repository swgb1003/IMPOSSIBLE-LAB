// IMPOSSIBLE LIBRARY のサイトの画面（proxy/site/）と three.js を出す。この PC の中（127.0.0.1）からだけ。
// パスは決めた2つの置き場の中だけを読み、外へ出る指定（..）は受け付けない。
import type { IncomingMessage, ServerResponse } from "node:http";
import { existsSync, readFileSync, statSync } from "node:fs";
import path from "node:path";

const TYPES: Record<string, string> = {
  ".html": "text/html; charset=utf-8", ".css": "text/css; charset=utf-8", ".js": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8", ".glb": "model/gltf-binary", ".jpg": "image/jpeg", ".png": "image/png",
  ".svg": "image/svg+xml", ".woff2": "font/woff2",
};

function isLoopback(req: IncomingMessage) {
  const a = req.socket.remoteAddress ?? "";
  return a === "127.0.0.1" || a === "::1" || a === "::ffff:127.0.0.1";
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

export function createSite(siteDir: string, threeDir: string) {
  const site = path.resolve(siteDir);
  const three = path.resolve(threeDir);
  /** サイトの入口なら返して true。 */
  return function serve(req: IncomingMessage, res: ServerResponse, url: URL): boolean {
    if (req.method !== "GET" && req.method !== "HEAD") return false;
    const p = url.pathname;
    let file: string | null = null;
    if (p === "/") { res.writeHead(302, { location: "/site/index.html" }); res.end(); return true; }
    if (p.startsWith("/site/")) file = resolveInside(site, p.slice("/site/".length) || "index.html");
    else if (p.startsWith("/vendor/three/build/") || p.startsWith("/vendor/three/examples/jsm/")) file = resolveInside(three, p.slice("/vendor/three/".length));
    else return false;
    if (!isLoopback(req)) { res.writeHead(403, { "content-type": "text/plain; charset=utf-8" }); res.end("この PC の中からだけ開けます"); return true; }
    const type = file ? TYPES[path.extname(file).toLowerCase()] : undefined;
    if (!file || !type) { res.writeHead(404, { "content-type": "text/plain; charset=utf-8" }); res.end("not found"); return true; }
    const body = readFileSync(file);
    res.writeHead(200, { "content-type": type, "content-length": body.length, "cache-control": "no-cache", "x-content-type-options": "nosniff" });
    res.end(req.method === "HEAD" ? undefined : body);
    return true;
  };
}
