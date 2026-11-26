// 発明ライブラリの HTTP の入口（docs/PUBLISH_LIBRARY.md 7章）。
// ・サイトの入口（/api/）：この PC の中（127.0.0.1）からだけ。本人はログインの記録（cookie）で決める。
//   書き込みは、送り元のページ（Origin）が同じサイトのときだけ受け付ける。
// ・Quest の入口（/library・/threads）：端末の発案者の鍵を x-ilab-creator で受け取る。ILAB_PROXY_TOKEN を設定したとき（LAN に出すとき）は x-ilab-token も要る。
// URL には ID だけを入れ、鍵・パスワード・本文は入れない（サーバーのログに URL が出るため）。
import type { IncomingMessage, ServerResponse } from "node:http";
import { z } from "zod";
import { HttpError, RateLimiter, readJsonBody } from "../http-util.ts";
import {
  ConfirmBody, describeIssues, EmailBody, LoginBody, MessageBody, PairBody, PasswordBody, ProfileBody, PublishBody,
  ReasonBody, ReportBody, ResetBody, ResolveBody, SignupBody, TakedownBody, TokenBody, VerifyBody, VoteBody,
} from "./schema.ts";
import { creatorHashOf, publicCreatorId, type LibraryStore } from "./store.ts";
import { isLoopback, originAllowed, readCookie, SESSION_COOKIE, setSessionCookie } from "./auth.ts";
import type { Who } from "./members.ts";

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

function parse<T extends z.ZodType>(schema: T, body: unknown): z.infer<T> {
  const r = schema.safeParse(body);
  if (!r.success) throw new HttpError(400, "bad_request", describeIssues(r.error));
  return r.data;
}

/** サイトの入口が求めるログイン：none（だれでも）・any（ログイン）・member（開発者か発案者）・account（開発者）・operator。 */
type Need = "none" | "any" | "member" | "account" | "operator";
type Handler = (ctx: Ctx) => Promise<unknown> | unknown;
interface Ctx { req: IncomingMessage; res: ServerResponse; url: URL; params: string[]; who: Who | null; token: string | null }
interface Route { method: string; pattern: RegExp; access: "api" | "quest" | "read"; need: Need; write: boolean; handler: Handler }

export function createLibraryRoutes(store: LibraryStore, opts: LibraryRouteOptions = {}) {
  const token = opts.token ?? "";
  const limiter = new RateLimiter(opts.writesPer10Min ?? 600, 10 * 60 * 1000);
  const m = store.members;

  const creatorKey = (req: IncomingMessage) => {
    const k = String(req.headers["x-ilab-creator"] ?? "");
    if (!CREATOR_KEY.test(k)) throw new HttpError(401, "no_creator", "端末の発案者キーがありません");
    return k;
  };
  const json = (ctx: Ctx, max = SMALL_BODY) => readJsonBody(ctx.req, max);
  const me = (ctx: Ctx) => ctx.who!;
  const accountId = (ctx: Ctx) => (ctx.who as { id: string }).id;

  /** ログインして、その記録を cookie で渡す。 */
  const signIn = (ctx: Ctx, who: Who) => {
    m.endSession(ctx.token);
    setSessionCookie(ctx.res, m.startSession(who));
    return session(who);
  };
  const session = (who: Who | null) => {
    if (!who) return { user: null, unread: 0 };
    const user = m.me(who);
    return { user: who.kind === "creator" ? { ...user, publicId: user.named ? publicCreatorId(who.hash) : null } : user, unread: m.unread(who) };
  };

  const routes: Route[] = [];
  const add = (method: string, p: string, access: Route["access"], handler: Handler, need: Need = "none") =>
    routes.push({ method, pattern: new RegExp(`^${p}$`), access, need, write: method !== "GET", handler });

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
  add("GET", `/library/(${ID})/images/([0-3])\\.jpg`, "read", ({ res, params }) => {
    const jpg = store.image(params[0], Number(params[1]));
    res.writeHead(200, { "content-type": "image/jpeg", "content-length": jpg.length, "cache-control": "no-store" });
    res.end(jpg);
    return undefined;
  });

  // ---- Quest：公開・取り下げ・試した・ほしい・サイトに入る番号 ----
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
  add("POST", "/library/pair", "quest", ({ req }) => m.pairStart(creatorHashOf(creatorKey(req))));
  // ほしい：Quest（試したあと、端末ごと）とサイト（ブラウザごと）で別に数える
  add("POST", `/library/(${ID})/want`, "read", async (ctx) => {
    if (ctx.req.headers["x-ilab-creator"] != null) return store.wantVr(ctx.params[0], creatorKey(ctx.req));
    if (!isLoopback(ctx.req)) throw new HttpError(403, "site_only", "この PC のサイトからだけ使えます");
    if (!originAllowed(ctx.req)) throw new HttpError(403, "bad_origin", "このサイトの画面から操作してください");
    const b = parse(VoteBody, await json(ctx));
    if (!b.voter) throw new HttpError(400, "bad_request", "voter がありません");
    return store.wantSite(ctx.params[0], b.voter);
  });

  // ---- Quest のやりとり（端末の鍵で読む。サイトは /api/threads） ----
  const questWho = (req: IncomingMessage): Who => ({ kind: "creator", hash: creatorHashOf(creatorKey(req)) });
  add("GET", "/threads", "quest", ({ req }) => ({ items: store.threads(questWho(req)) }));
  add("GET", `/threads/(${ID})`, "quest", ({ req, params }) => store.thread(params[0], questWho(req)));
  add("POST", `/threads/(${ID})/messages`, "quest", async (ctx) => {
    const b = parse(MessageBody, await json(ctx));
    ctx.res.statusCode = 201;
    return store.postMessage(ctx.params[0], questWho(ctx.req), b.text);
  });

  // ---- サイト：ログイン ----
  add("GET", "/api/session", "api", ({ who }) => session(who));
  add("POST", "/api/login", "api", async (ctx) => {
    const b = parse(LoginBody, await json(ctx));
    return signIn(ctx, m.login(b.email, b.password));
  });
  add("POST", "/api/logout", "api", (ctx) => { m.endSession(ctx.token); setSessionCookie(ctx.res, null); return { user: null }; });
  add("POST", "/api/logout-all", "api", (ctx) => { m.endAllSessions(me(ctx)); setSessionCookie(ctx.res, null); return { user: null }; }, "any");
  add("POST", "/api/signup", "api", async (ctx) => {
    const b = parse(SignupBody, await json(ctx));
    if (!b.name.trim()) throw new HttpError(400, "bad_request", "名前を書いてください");
    if (!b.agreed) throw new HttpError(400, "not_agreed", "利用規約と個人情報の扱いに同意してください");
    ctx.res.statusCode = 201;
    return signIn(ctx, store.signup(b));
  });
  add("POST", "/api/pair", "api", async (ctx) => signIn(ctx, m.pairRedeem(parse(PairBody, await json(ctx)).code)));
  add("POST", "/api/verify-email", "api", async (ctx) => m.verifyEmail(parse(TokenBody, await json(ctx)).token));
  add("POST", "/api/verify-email/resend", "api", (ctx) => {
    const a = m.account(accountId(ctx));
    if (a.emailVerified) return { sent: false };
    m.sendVerification(a);
    return { sent: true };
  }, "account");
  add("POST", "/api/password/forgot", "api", async (ctx) => m.forgot(parse(EmailBody, await json(ctx)).email));
  add("POST", "/api/password/reset", "api", async (ctx) => { const b = parse(ResetBody, await json(ctx)); return m.reset(b.token, b.password); });

  // ---- サイト：自分のこと ----
  add("GET", "/api/me", "api", (ctx) => {
    const w = me(ctx);
    if (w.kind === "account") return { ...m.me(w), ...store.accountActivity(w.id) };
    if (w.kind === "creator") return { ...session(w).user, inventions: store.mineByHash(w.hash) };
    return m.me(w);
  }, "any");
  add("POST", "/api/me", "api", async (ctx) => store.updateProfile(accountId(ctx), parse(ProfileBody, await json(ctx))), "account");
  add("POST", "/api/me/password", "api", async (ctx) => {
    const b = parse(PasswordBody, await json(ctx));
    return m.changePassword(me(ctx), b.current, b.next, ctx.token);
  }, "any");
  add("POST", "/api/me/delete", "api", async (ctx) => {
    const r = store.deleteAccount(accountId(ctx), parse(ConfirmBody, await json(ctx)).password);
    setSessionCookie(ctx.res, null);
    return r;
  }, "account");
  add("GET", "/api/notifications", "api", (ctx) => ({ items: m.notices(me(ctx)), unread: m.unread(me(ctx)) }), "any");
  add("POST", "/api/notifications/read", "api", (ctx) => m.readNotices(me(ctx)), "any");

  // ---- サイト：発案者のページ（開発者の一覧と個人のページは出さない。開発者は採用した発明の発案者と運営にだけ見せる） ----
  add("GET", `/api/creators/(${ID})`, "api", ({ params }) => store.creator(params[0]));

  // ---- サイト：詳しい内容つきの1件（ログインした開発者・公開した発案者・運営。開発者が読んだ記録を残す） ----
  add("GET", `/api/library/(${ID})`, "api", (ctx) => store.detail(ctx.params[0], me(ctx)), "any");

  // ---- サイト：検討中・採用・通報 ----
  add("POST", `/api/library/(${ID})/consider`, "api", (ctx) => store.consider(ctx.params[0], accountId(ctx)), "account");
  add("POST", `/api/library/(${ID})/adopt`, "api", async (ctx) => store.adopt(ctx.params[0], accountId(ctx), parse(ReasonBody, await json(ctx)).reason), "account");
  add("POST", `/api/library/(${ID})/report`, "api", async (ctx) => {
    const b = parse(ReportBody, await json(ctx));
    ctx.res.statusCode = 201;
    return store.report(ctx.params[0], b.reason, b.detail);
  });

  // ---- サイト：やりとり（採用した人と発案者の2人だけ） ----
  add("GET", "/api/threads", "api", (ctx) => ({ items: store.threads(me(ctx)) }), "member");
  add("GET", `/api/threads/(${ID})`, "api", (ctx) => store.thread(ctx.params[0], me(ctx)), "member");
  add("POST", `/api/threads/(${ID})/messages`, "api", async (ctx) => {
    const b = parse(MessageBody, await json(ctx));
    ctx.res.statusCode = 201;
    return store.postMessage(ctx.params[0], me(ctx), b.text);
  }, "member");

  // ---- このデモの送ったメール（メールは外へ送らない） ----
  add("GET", "/api/mail", "api", () => ({ items: m.outbox() }));

  // ---- 運営 ----
  add("GET", "/api/admin/library", "api", () => ({ items: store.adminList() }), "operator");
  add("GET", "/api/admin/accounts", "api", () => ({ items: store.adminAccounts() }), "operator");
  add("GET", "/api/admin/reports", "api", () => ({ items: store.reports() }), "operator");
  add("POST", `/api/admin/accounts/(${ID})/verify`, "api", async (ctx) => store.verifyAccount(ctx.params[0], parse(VerifyBody, await json(ctx)).verified), "operator");
  add("POST", `/api/admin/library/(${ID})/takedown`, "api", async (ctx) => store.takedown(ctx.params[0], parse(TakedownBody, await json(ctx)).reason), "operator");
  add("POST", `/api/admin/reports/(${ID})/resolve`, "api", async (ctx) => {
    const b = parse(ResolveBody, await json(ctx));
    return store.resolveReport(ctx.params[0], b.action, b.note);
  }, "operator");

  const allow = (need: Need, who: Who | null) => {
    if (need === "none") return;
    if (!who) throw new HttpError(401, "no_session", "ログインしてください");
    const ok = need === "any" || (need === "operator" ? who.kind === "operator" : need === "account" ? who.kind === "account" : who.kind !== "operator");
    if (!ok) {
      throw new HttpError(403, "forbidden", need === "operator" ? "運営だけが使えます"
        : need === "account" ? "開発者としてログインしてください" : "採用した開発者と発案者だけが使えます");
    }
  };

  /** ライブラリの入口なら処理して true。違えば false（ほかの入口へ）。 */
  return async function handle(req: IncomingMessage, res: ServerResponse, url: URL): Promise<boolean> {
    const p = url.pathname;
    if (!(p === "/library" || p.startsWith("/library/") || p === "/threads" || p.startsWith("/threads/") || p.startsWith("/api/"))) return false;
    let params: string[] = [];
    const sameMethod = routes.filter((r) => r.method === req.method);
    const route = sameMethod.find((r) => { const mm = r.pattern.exec(p); if (mm) params = mm.slice(1); return mm != null; });
    if (!route) {
      const any = routes.some((r) => r.pattern.test(p));
      throw new HttpError(any ? 405 : 404, any ? "method_not_allowed" : "not_found", any ? "この操作はできません" : "not found");
    }
    let who: Who | null = null, sid: string | null = null;
    if (route.access === "api") {
      if (!isLoopback(req)) throw new HttpError(403, "site_only", "この PC のサイトからだけ使えます");
      if (route.write && !originAllowed(req)) throw new HttpError(403, "bad_origin", "このサイトの画面から操作してください");
      sid = readCookie(req, SESSION_COOKIE);
      who = m.whoOf(sid);
      allow(route.need, who);
    }
    if (route.access === "quest" || (route.access === "read" && !isLoopback(req))) {
      if (token && req.headers["x-ilab-token"] !== token) throw new HttpError(401, "unauthorized", "認証に失敗しました");
      if (!token && !isLoopback(req)) throw new HttpError(403, "site_only", "LAN から使うときは ILAB_PROXY_TOKEN を設定してください");
    }
    if (route.write && !limiter.take()) throw new HttpError(429, "rate_limited", "操作が多すぎます。少し待ってから試してください");
    const out = await route.handler({ req, res, url, params, who, token: sid });
    if (out !== undefined) sendJson(res, res.statusCode && res.statusCode !== 200 ? res.statusCode : 200, out);
    return true;
  };
}

/** サイトの画面を出すときに、ログインしている人を調べる（ページの入口で使う）。 */
export function sessionReader(store: LibraryStore) {
  return (req: IncomingMessage): Who | null => store.members.whoOf(readCookie(req, SESSION_COOKIE));
}
