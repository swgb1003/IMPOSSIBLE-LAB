// サイトのログインと権限（登録・ログイン・ログアウト・パスワード・メールの確認・知らせ・通報・運営・退会・画面の入口）のテスト。 npm test
import { test } from "node:test";
import assert from "node:assert/strict";
import { createServer, request, type IncomingMessage, type Server } from "node:http";
import type { AddressInfo } from "node:net";
import { mkdtempSync, readFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { LibraryStore } from "../src/library/store.ts";
import { createLibraryRoutes, sendJson, sessionReader } from "../src/library/routes.ts";
import { hostAllowed } from "../src/library/auth.ts";
import { createSite } from "../src/site.ts";
import { HttpError } from "../src/http-util.ts";

const KEY_A = "questA-0123456789abcdef";
const here = import.meta.dirname;

function tinyGlb(): Buffer {
  let json = Buffer.from(JSON.stringify({ asset: { version: "2.0" }, scene: 0, scenes: [{ nodes: [] }] }), "utf8");
  json = Buffer.concat([json, Buffer.alloc((4 - (json.length % 4)) % 4, 0x20)]);
  const head = Buffer.alloc(20);
  head.writeUInt32LE(0x46546c67, 0); head.writeUInt32LE(2, 4); head.writeUInt32LE(20 + json.length, 8);
  head.writeUInt32LE(json.length, 12); head.writeUInt32LE(0x4e4f534a, 16);
  return Buffer.concat([head, json]);
}
const publication = () => JSON.parse(readFileSync(path.join(here, "fixtures", "publication-rain.json"), "utf8"));

/** 本番と同じ順（宛先名の確認 → ライブラリ → 画面）で受ける。 */
async function withServer(fn: (base: string, store: LibraryStore) => Promise<void>) {
  const dir = mkdtempSync(path.join(tmpdir(), "ilab-auth-"));
  const store = new LibraryStore({ dir });
  const handle = createLibraryRoutes(store);
  const site = createSite(path.join(here, "..", "site"), path.join(here, "..", "node_modules", "three"), sessionReader(store));
  const server: Server = createServer((req, res) => {
    const url = new URL(req.url ?? "/", "http://localhost");
    (async () => {
      if (!hostAllowed(req)) throw new HttpError(421, "bad_host", "bad host");
      if (await handle(req, res, url)) return;
      if (site(req, res, url)) return;
      sendJson(res, 404, { status: "error", code: "not_found" });
    })().catch((e: unknown) => {
      if (e instanceof HttpError) sendJson(res, e.status, { status: "error", code: e.code, message: e.message });
      else { console.error(e); sendJson(res, 500, { status: "error", code: "internal" }); }
    });
  });
  await new Promise<void>((r) => server.listen(0, "127.0.0.1", r));
  const base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
  try { await fn(base, store); } finally { server.close(); }
}

const sid = (r: Response) => /ilab_sid=[^;]+/.exec(r.headers.get("set-cookie") ?? "")?.[0] ?? "";
const post = (base: string, p: string, body: unknown, headers: Record<string, string> = {}) =>
  fetch(base + p, { method: "POST", headers: { "content-type": "application/json", ...headers }, body: JSON.stringify(body) });
const get = (base: string, p: string, cookie = "") => fetch(base + p, { headers: cookie ? { cookie } : {}, redirect: "manual" });
const SIGNUP = { name: "山田 花", affiliation: "北の大学", field: "流体", intro: "雨の研究", email: "hana@example.jp", password: "rain-research", agreed: true };

async function publishOne(base: string) {
  const r = await post(base, "/library", { publication: publication(), glbBase64: tinyGlb().toString("base64") }, { "x-ilab-creator": KEY_A });
  return (await r.json() as { id: string }).id;
}
async function creatorCookie(base: string) {
  const { code } = await (await post(base, "/library/pair", {}, { "x-ilab-creator": KEY_A })).json() as { code: string };
  return sid(await post(base, "/api/pair", { code }));
}
/** 送ったメールの一覧から、いちばん新しいメールのリンクの札を取り出す。 */
async function lastMailToken(base: string) {
  const { items } = await (await get(base, "/api/mail")).json() as { items: { link: string; to: string }[] };
  return /#token=([A-Za-z0-9_-]+)/.exec(items[0].link)![1];
}

test("signup: checks the email, password and agreement; one email per person", async () => {
  await withServer(async (base) => {
    assert.equal((await post(base, "/api/signup", { ...SIGNUP, password: "short" })).status, 400);
    assert.equal((await post(base, "/api/signup", { ...SIGNUP, email: "not-an-email" })).status, 400);
    assert.equal((await post(base, "/api/signup", { ...SIGNUP, agreed: false })).status, 400);
    const r = await post(base, "/api/signup", SIGNUP);
    assert.equal(r.status, 201);
    assert.match(r.headers.get("set-cookie") ?? "", /HttpOnly; SameSite=Strict/);
    const s = await (await get(base, "/api/session", sid(r))).json() as { user: { name: string; email: string; emailVerified: boolean } };
    assert.equal(s.user.name, "山田 花");
    assert.equal(s.user.email, "hana@example.jp");
    assert.equal(s.user.emailVerified, false);
    assert.equal((await post(base, "/api/signup", { ...SIGNUP, email: "HANA@example.jp" })).status, 409, "大文字でも同じアドレス");
  });
});

test("login: one message for any mistake, a pause after repeated failures, logout ends the session", async () => {
  await withServer(async (base) => {
    await post(base, "/api/signup", SIGNUP);
    const wrong = await (await post(base, "/api/login", { email: SIGNUP.email, password: "wrong-pass" })).json() as { message: string };
    const nobody = await (await post(base, "/api/login", { email: "nobody@example.jp", password: "wrong-pass" })).json() as { message: string };
    assert.equal(wrong.message, nobody.message, "登録があるかどうかを言わない");
    const r = await post(base, "/api/login", { email: " Hana@Example.jp ", password: SIGNUP.password });
    assert.equal(r.status, 200);
    const cookie = sid(r);
    assert.equal((await get(base, "/api/me", cookie)).status, 200);
    await post(base, "/api/logout", {}, { cookie });
    assert.equal((await get(base, "/api/me", cookie)).status, 401, "ログアウトした記録は使えない");
    for (let i = 0; i < 5; i++) await post(base, "/api/login", { email: SIGNUP.email, password: "wrong-pass" });
    assert.equal((await post(base, "/api/login", { email: SIGNUP.email, password: SIGNUP.password })).status, 429, "失敗が続いたら待たせる");
  });
});

test("privacy: developers are shown only to the creator they adopted from and to operators", async () => {
  await withServer(async (base, store) => {
    const cookie = sid(await post(base, "/api/signup", SIGNUP));
    // 開発者の一覧と個人のページは無い
    assert.equal((await get(base, "/api/accounts")).status, 404);
    assert.equal((await get(base, "/people")).status, 404);
    const pubId = await publishOne(base);
    const { threadId } = await (await post(base, `/api/library/${pubId}/adopt`, { reason: "研究に使える" }, { cookie })).json() as { threadId: string };
    const open = JSON.stringify([store.get(pubId), store.list()]) + await (await get(base, `/library/${pubId}`)).text();
    assert.ok(!open.includes("山田") && !open.includes("北の大学") && !open.includes("hana@example.jp"), "だれでも見られる所に、採用した人は出ない");
    // 発案者には、やりとりの相手として名前・所属・紹介が見える（メールアドレスとパスワードは見えない）
    const creator = await creatorCookie(base);
    const t = await (await get(base, `/api/threads/${threadId}`, creator)).text();
    assert.ok(t.includes("北の大学") && t.includes("雨の研究"));
    assert.ok(!t.includes("hana@example.jp") && !t.includes("passwordHash") && !t.includes("scrypt"));
    // 自分のページでは全部見える
    const me = await (await get(base, "/api/me", cookie)).json() as { affiliation: string; adopted: unknown[] };
    assert.equal(me.affiliation, "北の大学");
    assert.equal(me.adopted.length, 1);
  });
});

test("password: change ends other sessions; forgot sends a reset mail; reset links work once", async () => {
  await withServer(async (base) => {
    const a = sid(await post(base, "/api/signup", SIGNUP));
    const b = sid(await post(base, "/api/login", { email: SIGNUP.email, password: SIGNUP.password }));
    assert.equal((await post(base, "/api/me/password", { current: "nope-nope", next: "new-password-1" }, { cookie: a })).status, 403);
    assert.equal((await post(base, "/api/me/password", { current: SIGNUP.password, next: "new-password-1" }, { cookie: a })).status, 200);
    assert.equal((await get(base, "/api/me", a)).status, 200, "変えた端末は続く");
    assert.equal((await get(base, "/api/me", b)).status, 401, "ほかの端末は終わる");

    assert.equal((await post(base, "/api/password/forgot", { email: "nobody@example.jp" })).status, 200, "登録が無くても同じ答え");
    await post(base, "/api/password/forgot", { email: SIGNUP.email });
    const token = await lastMailToken(base);
    assert.equal((await post(base, "/api/password/reset", { token, password: "reset-password-2" })).status, 200);
    assert.equal((await post(base, "/api/password/reset", { token, password: "reset-password-3" })).status, 400, "1回だけ");
    assert.equal((await get(base, "/api/me", a)).status, 401, "再設定したら、すべての端末のログインが終わる");
    assert.equal((await post(base, "/api/login", { email: SIGNUP.email, password: "new-password-1" })).status, 401);
    assert.equal((await post(base, "/api/login", { email: SIGNUP.email, password: "reset-password-2" })).status, 200);
  });
});

test("email: the verification link marks the address as confirmed; mails never carry the token in a server URL", async () => {
  await withServer(async (base) => {
    const cookie = sid(await post(base, "/api/signup", SIGNUP));
    const { items } = await (await get(base, "/api/mail")).json() as { items: { to: string; subject: string; link: string }[] };
    assert.equal(items[0].to, "hana@example.jp");
    assert.match(items[0].link, /^\/verify#token=/, "札は # の後ろ（サーバーに送られない）");
    assert.equal((await post(base, "/api/verify-email", { token: await lastMailToken(base) })).status, 200);
    const s = await (await get(base, "/api/session", cookie)).json() as { user: { emailVerified: boolean } };
    assert.equal(s.user.emailVerified, true);
  });
});

test("notices: adoption and replies reach the other side; reading the thread clears them", async () => {
  await withServer(async (base) => {
    const pubId = await publishOne(base);
    const adopter = sid(await post(base, "/api/signup", SIGNUP));
    const creator = await creatorCookie(base);
    const { threadId } = await (await post(base, `/api/library/${pubId}/adopt`, { reason: "研究に使える" }, { cookie: adopter })).json() as { threadId: string };
    let n = await (await get(base, "/api/notifications", creator)).json() as { unread: number; items: { kind: string; fromName: string }[] };
    assert.equal(n.unread, 1);
    assert.deepEqual([n.items[0].kind, n.items[0].fromName], ["adopted", "山田 花"]);
    await post(base, `/api/threads/${threadId}/messages`, { text: "ありがとうございます" }, { cookie: creator });
    const list = await (await get(base, "/api/threads", adopter)).json() as { items: { unread: number }[] };
    assert.equal(list.items[0].unread, 1);
    await get(base, `/api/threads/${threadId}`, adopter);
    n = await (await get(base, "/api/notifications", adopter)).json() as typeof n;
    assert.equal(n.unread, 0, "読んだら消える");
    assert.equal((await get(base, `/api/threads/${threadId}`, sid(await post(base, "/api/signup", { ...SIGNUP, email: "other@example.jp" })))).status, 404, "当事者でない人には見せない");
  });
});

test("operator: only operators open the admin API; a report can be dismissed or taken down", async () => {
  await withServer(async (base, store) => {
    const pubId = await publishOne(base);
    store.members.createOperator({ name: "運営", email: "ops@example.jp", password: "operator-pass" });
    const user = sid(await post(base, "/api/signup", SIGNUP));
    assert.equal((await get(base, "/api/admin/library")).status, 401);
    assert.equal((await get(base, "/api/admin/library", user)).status, 403);
    const op = sid(await post(base, "/api/login", { email: "ops@example.jp", password: "operator-pass" }));
    assert.equal((await post(base, `/api/library/${pubId}/report`, { reason: "rights", detail: "既存の製品の写真に見える" })).status, 201, "通報はログインしなくてもできる");
    const reports = await (await get(base, "/api/admin/reports", op)).json() as { items: { id: string; reason: string; status: string }[] };
    assert.equal(reports.items[0].status, "open");
    const accounts = await (await get(base, "/api/admin/accounts", op)).json() as { items: { email: string }[] };
    assert.equal(accounts.items[0].email, "hana@example.jp", "運営にはメールアドレスが見える");
    assert.equal((await post(base, `/api/admin/reports/${reports.items[0].id}/resolve`, { action: "takedown", note: "権利の確認ができない" }, { cookie: op })).status, 200);
    assert.equal((await get(base, `/library/${pubId}`)).status, 410);
    const n = await (await get(base, "/api/notifications", await creatorCookie(base))).json() as { items: { kind: string; note: string }[] };
    assert.deepEqual([n.items[0].kind, n.items[0].note], ["takedown", "権利の確認ができない"]);
    assert.equal((await post(base, `/api/library/${pubId}/adopt`, { reason: "x" }, { cookie: op })).status, 403, "運営は採用できない");
  });
});

test("delete account: needs the password; adoptions stay as a left member and the thread is closed", async () => {
  await withServer(async (base, store) => {
    const pubId = await publishOne(base);
    const cookie = sid(await post(base, "/api/signup", SIGNUP));
    const { threadId } = await (await post(base, `/api/library/${pubId}/adopt`, { reason: "研究に使える" }, { cookie })).json() as { threadId: string };
    assert.equal((await post(base, "/api/me/delete", { password: "wrong-pass" }, { cookie })).status, 403);
    assert.equal((await post(base, "/api/me/delete", { password: SIGNUP.password }, { cookie })).status, 200);
    assert.equal((await get(base, "/api/me", cookie)).status, 401);
    assert.equal(store.mine(KEY_A)[0].adoptions[0].adopter.name, "退会した人");
    const creator = await creatorCookie(base);
    assert.equal((await post(base, `/api/threads/${threadId}/messages`, { text: "まだいますか" }, { cookie: creator })).status, 410);
    assert.equal((await post(base, "/api/login", { email: SIGNUP.email, password: SIGNUP.password })).status, 401);
  });
});

test("host and origin: other host names and other sites' pages are refused", async () => {
  const req = (host: string, addr = "127.0.0.1") => ({ headers: { host }, socket: { remoteAddress: addr } }) as unknown as IncomingMessage;
  assert.equal(hostAllowed(req("127.0.0.1:8787")), true);
  assert.equal(hostAllowed(req("localhost:8787")), true);
  assert.equal(hostAllowed(req("evil.example:8787")), false);
  assert.equal(hostAllowed(req("evil.example:8787", "192.168.1.20")), true, "LAN からはトークンで確かめる");
  await withServer(async (base) => {
    // fetch は宛先名を書き換えられないので、http で送る
    const status = await new Promise<number>((resolve, reject) => {
      const u = new URL(base);
      request({ host: u.hostname, port: u.port, path: "/api/session", headers: { host: "evil.example" } }, (res) => { res.resume(); resolve(res.statusCode ?? 0); }).on("error", reject).end();
    });
    assert.equal(status, 421);
    assert.equal((await post(base, "/api/signup", SIGNUP, { origin: "http://evil.example" })).status, 403);
    assert.equal((await post(base, "/api/signup", SIGNUP, { origin: base })).status, 201);
  });
});

test("pages: clean addresses, login gates, old addresses move, unknown pages say not found", async () => {
  await withServer(async (base, store) => {
    const pubId = await publishOne(base);
    let r = await get(base, `/inventions/${pubId}`);
    assert.equal(r.status, 200);
    assert.match(r.headers.get("content-security-policy") ?? "", /script-src 'self' 'sha256-/);
    assert.equal(r.headers.get("x-frame-options"), "DENY");
    r = await get(base, "/me");
    assert.deepEqual([r.status, r.headers.get("location")], [302, "/login?next=%2Fme"]);
    r = await get(base, `/site/invention.html?id=${pubId}`);
    assert.deepEqual([r.status, r.headers.get("location")], [301, `/inventions/${pubId}`]);
    const user = sid(await post(base, "/api/signup", SIGNUP));
    assert.equal((await get(base, "/me", user)).status, 200);
    assert.equal((await get(base, "/login", user)).headers.get("location"), "/me", "ログイン済みなら自分のページへ");
    assert.equal((await get(base, "/admin", user)).status, 403);
    store.members.createOperator({ name: "運営", email: "ops@example.jp", password: "operator-pass" });
    const op = sid(await post(base, "/api/login", { email: "ops@example.jp", password: "operator-pass" }));
    assert.equal((await get(base, "/admin", op)).status, 200);
    assert.equal((await get(base, "/messages", op)).status, 403, "運営はやりとりに加わらない");
    r = await fetch(base + "/no-such-page", { headers: { accept: "text/html" } });
    assert.equal(r.status, 404);
    assert.match(r.headers.get("content-type") ?? "", /text\/html/);
  });
});
