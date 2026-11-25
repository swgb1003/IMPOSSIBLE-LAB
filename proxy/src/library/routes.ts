// 発明ライブラリの HTTP の入口（docs/PUBLISH_LIBRARY.md 7章）。
// ・サイトと運営の入口：この PC の中（127.0.0.1）からだけ受け付ける（サイトは同じプログラムが出す）。
// ・Quest の入口：端末の発案者の鍵を x-ilab-creator で受け取る。ILAB_PROXY_TOKEN を設定したとき（LAN に出すとき）は x-ilab-token も要る。
// URL には ID だけを入れ、鍵・本文は入れない（サーバーのログに URL が出るため）。
import type { IncomingMessage, ServerResponse } from "node:http";
import { z } from "zod";
import { HttpError, RateLimiter, readJsonBody } from "../http-util.ts";
import {
  AccountBody, AccountInput, describeIssues, MessageBody, PublishBody, ReasonBody, TakedownBody, VerifyBody, VoteBody,
} from "./schema.ts";
import { creatorHashOf, type Identity, type LibraryStore } from "./store.ts";

export interface LibraryRouteOptions {
  /** 設定時、Quest の入口に x-ilab-token を要求する。 */
  token?: string;
  /** 書き込みの回数の上限（10分あたり）。 */
  writesPer10Min?: number;
}

const ID = "[a-z0-9-]{3,40}";
const CREATOR_KEY = /^[A-Za-z0-9_-]{16,64}$/;
const SMALL_BODY = 32 * 1024;
const PUBLISH_BODY = 4 * 1024 * 1024;

export function sendJson(res: ServerResponse, status: number, obj: unknown) {
  res.writeHead(status, { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" });
  res.end(JSON.stringify(obj));
}

function isLoopback(req: IncomingMessage) {
  const a = req.socket.remoteAddress ?? "";
  return a === "127.0.0.1" || a === "::1" || a === "::ffff:127.0.0.1";
}

function parse<T extends z.ZodType>(schema: T, body: unknown): z.infer<T> {
  const r = schema.safeParse(body);
  if (!r.success) throw new HttpError(400, "bad_request", describeIssues(r.error));
  return r.data;
}

type Handler = (ctx: Ctx) => Promise<unknown> | unknown;
interface Ctx { req: IncomingMessage; res: ServerResponse; url: URL; params: string[] }
interface Route { method: string; pattern: RegExp; access: "site" | "quest" | "read"; write: boolean; handler: Handler }

export function createLibraryRoutes(store: LibraryStore, opts: LibraryRouteOptions = {}) {
  const token = opts.token ?? "";
  const limiter = new RateLimiter(opts.writesPer10Min ?? 600, 10 * 60 * 1000);

  const creatorKey = (req: IncomingMessage) => {
    const k = String(req.headers["x-ilab-creator"] ?? "");
    if (!CREATOR_KEY.test(k)) throw new HttpError(401, "no_creator", "端末の発案者キーがありません");
    return k;
  };
  /** やりとりの名乗り：Quest は端末の鍵、サイトは x-ilab-as（account:<ID> か creator:<発案者の公開 ID>）。 */
  const identity = (req: IncomingMessage): Identity => {
    if (req.headers["x-ilab-creator"] != null) return { kind: "creator", hash: creatorHashOf(creatorKey(req)) };
    if (!isLoopback(req)) throw new HttpError(403, "site_only", "この PC のサイトからだけ使えます");
    const as = String(req.headers["x-ilab-as"] ?? "");
    const m = /^(account|creator):([A-Za-z0-9-]{3,40})$/.exec(as);
    if (!m) throw new HttpError(401, "no_identity", "立場とアカウントを選んでください");
    return m[1] === "account" ? { kind: "account", id: m[2] } : store.creatorIdentityFromPublic(m[2]);
  };
  const json = (ctx: Ctx, max = SMALL_BODY) => readJsonBody(ctx.req, max);

  const routes: Route[] = [];
  const add = (method: string, p: string, access: Route["access"], handler: Handler) =>
    routes.push({ method, pattern: new RegExp(`^${p}$`), access, write: method !== "GET", handler });

  // ---- 一覧と1件（サイト・Quest のライブラリを試す） ----
  add("GET", "/library", "read", ({ url }) => store.list({
    sort: url.searchParams.get("sort") ?? undefined, tag: url.searchParams.get("tag") ?? undefined,
    scene: url.searchParams.get("scene") ?? undefined, status: url.searchParams.get("status") ?? undefined,
  }));
  add("GET", "/library/mine", "quest", ({ req }) => ({ items: store.mine(creatorKey(req)) }));
  add("GET", `/library/(${ID})`, "read", ({ params }) => store.get(params[0]));
  add("GET", `/library/(${ID})/model\\.glb`, "read", ({ res, params }) => {
    const glb = store.model(params[0]);
    res.writeHead(200, { "content-type": "model/gltf-binary", "content-length": glb.length, "cache-control": "no-store" });
    res.end(glb);
    return undefined;
  });

  add("GET", `/library/(${ID})/images/([0-3])\.jpg`, "read", ({ res, params }) => {
    const jpg = store.image(params[0], Number(params[1]));
    res.writeHead(200, { "content-type": "image/jpeg", "content-length": jpg.length, "cache-control": "no-store" });
    res.end(jpg);
    return undefined;
  });

  // ---- Quest：公開・取り下げ・試した・ほしい ----
  add("POST", "/library", "quest", async (ctx) => {
    const key = creatorKey(ctx.req);
    const b = parse(PublishBody, await json(ctx, PUBLISH_BODY));
    const glb = Buffer.from(b.glbBase64, "base64");
    const images = b.images.map((im) => ({ kind: im.kind, caption: im.caption, revision: im.revision, jpg: Buffer.from(im.jpgBase64, "base64") }));
    const r = store.publish(key, b.publication, glb, images);
    ctx.res.statusCode = r.created ? 201 : 200;
    return { status: "ok", ...r };
  });
  add("POST", `/library/(${ID})/withdraw`, "quest", ({ req, params }) => store.withdraw(params[0], creatorKey(req)));
  add("POST", `/library/(${ID})/tried`, "quest", ({ req, params }) => store.tried(params[0], creatorKey(req)));
  // ほしい：Quest（試したあと、端末ごと）とサイト（ブラウザごと）で別に数える
  add("POST", `/library/(${ID})/want`, "read", async (ctx) => {
    if (ctx.req.headers["x-ilab-creator"] != null) return store.wantVr(ctx.params[0], creatorKey(ctx.req));
    if (!isLoopback(ctx.req)) throw new HttpError(403, "site_only", "この PC のサイトからだけ使えます");
    const b = parse(VoteBody, await json(ctx));
    if (!b.voter) throw new HttpError(400, "bad_request", "voter がありません");
    return store.wantSite(ctx.params[0], b.voter);
  });

  // ---- サイト：検討中・採用・登録・発案者のページ ----
  add("POST", `/library/(${ID})/consider`, "site", async (ctx) => store.consider(ctx.params[0], parse(AccountBody, await json(ctx)).accountId));
  add("POST", `/library/(${ID})/adopt`, "site", async (ctx) => {
    const b = parse(ReasonBody, await json(ctx));
    return store.adopt(ctx.params[0], b.accountId, b.reason);
  });
  add("GET", "/accounts", "site", () => ({ items: store.listAccounts() }));
  add("POST", "/accounts", "site", async (ctx) => { ctx.res.statusCode = 201; return store.createAccount(parse(AccountInput, await json(ctx))); });
  add("GET", `/accounts/(${ID})`, "site", ({ params }) => store.getAccount(params[0]));
  add("GET", "/creators", "site", () => ({ items: store.listCreators() }));
  add("GET", `/creators/(${ID})`, "site", ({ params }) => store.creator(params[0]));

  // ---- やりとり（サイトと Quest の両方） ----
  add("GET", "/threads", "read", ({ req }) => ({ items: store.threads(identity(req)) }));
  add("GET", `/threads/(${ID})`, "read", ({ req, params }) => store.thread(params[0], identity(req)));
  add("POST", `/threads/(${ID})/messages`, "read", async (ctx) => {
    const who = identity(ctx.req);
    const b = parse(MessageBody, await json(ctx));
    ctx.res.statusCode = 201;
    return store.postMessage(ctx.params[0], who, b.text);
  });

  // ---- 運営 ----
  add("GET", "/admin/library", "site", () => ({ items: store.adminList() }));
  add("POST", `/admin/accounts/(${ID})/verify`, "site", async (ctx) => store.verifyAccount(ctx.params[0], parse(VerifyBody, await json(ctx)).verified));
  add("POST", `/admin/library/(${ID})/takedown`, "site", async (ctx) => store.takedown(ctx.params[0], parse(TakedownBody, await json(ctx)).reason));

  /** ライブラリの入口なら処理して true。違えば false（ほかの入口へ）。 */
  return async function handle(req: IncomingMessage, res: ServerResponse, url: URL): Promise<boolean> {
    const p = url.pathname;
    if (!(p === "/library" || p.startsWith("/library/") || p === "/accounts" || p.startsWith("/accounts/")
      || p === "/creators" || p.startsWith("/creators/") || p === "/threads" || p.startsWith("/threads/") || p.startsWith("/admin/"))) return false;
    let params: string[] = [];
    const sameMethod = routes.filter((r) => r.method === req.method);
    const route = sameMethod.find((r) => { const m = r.pattern.exec(p); if (m) params = m.slice(1); return m != null; });
    if (!route) {
      const any = routes.some((r) => r.pattern.test(p));
      throw new HttpError(any ? 405 : 404, any ? "method_not_allowed" : "not_found", any ? "この操作はできません" : "not found");
    }
    if (route.access === "site" && !isLoopback(req)) throw new HttpError(403, "site_only", "この PC のサイトからだけ使えます");
    if (route.access === "quest" || (route.access === "read" && !isLoopback(req))) {
      if (token && req.headers["x-ilab-token"] !== token) throw new HttpError(401, "unauthorized", "認証に失敗しました");
      if (!token && !isLoopback(req)) throw new HttpError(403, "site_only", "LAN から使うときは ILAB_PROXY_TOKEN を設定してください");
    }
    if (route.write && !limiter.take()) throw new HttpError(429, "rate_limited", "操作が多すぎます。少し待ってから試してください");
    const out = await route.handler({ req, res, url, params });
    if (out !== undefined) sendJson(res, res.statusCode && res.statusCode !== 200 ? res.statusCode : 200, out);
    return true;
  };
}
