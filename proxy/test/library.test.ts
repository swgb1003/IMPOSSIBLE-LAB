// 発明ライブラリ（公開・一覧・ほしい・採用・登録・やりとり・取り下げ）のテスト。API は呼ばない。 npm test
import { test } from "node:test";
import assert from "node:assert/strict";
import { createServer, type Server } from "node:http";
import type { AddressInfo } from "node:net";
import { mkdtempSync, readFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { LibraryStore, checkGlb, creatorHashOf, publicCreatorId, resultSummary } from "../src/library/store.ts";
import { createLibraryRoutes, sendJson } from "../src/library/routes.ts";
import { Publication, CONSENT_VERSION } from "../src/library/schema.ts";
import { HttpError } from "../src/http-util.ts";

const KEY_A = "questA-0123456789abcdef";
const KEY_B = "questB-0123456789abcdef";

/** 最小の GLB（JSON の区切りだけ）。 */
function tinyGlb(): Buffer {
  let json = Buffer.from(JSON.stringify({ asset: { version: "2.0", generator: "test" }, scene: 0, scenes: [{ nodes: [] }] }), "utf8");
  const pad = (4 - (json.length % 4)) % 4;
  json = Buffer.concat([json, Buffer.alloc(pad, 0x20)]);
  const head = Buffer.alloc(20);
  head.writeUInt32LE(0x46546c67, 0); head.writeUInt32LE(2, 4); head.writeUInt32LE(20 + json.length, 8);
  head.writeUInt32LE(json.length, 12); head.writeUInt32LE(0x4e4f534a, 16);
  return Buffer.concat([head, json]);
}

function publication(over: { id?: string; name?: string; displayName?: string; anonymous?: boolean; tag?: string; request?: string } = {}) {
  return {
    format: 1,
    creator: { displayName: over.displayName ?? "あおい", anonymous: over.anonymous ?? false },
    consent: { version: CONSENT_VERSION, agreed: true, agreedAt: "2026-11-25T20:00:00" },
    invention: {
      id: over.id ?? "rain-1", revision: 3, name: over.name ?? "ハンズフリー雨よけ装置",
      summary: "通学中に両手がふさがっていても、頭の上で雨を防ぐ。", purpose: "手を使わずに雨を防ぐ",
      tags: [{ id: over.tag ?? "protection", label: "防護・安全" }], sceneTags: [],
      mount: { id: "head", label: "頭" },
      request: over.request ?? "手を使わず雨を防ぐ装置を作って", designSource: "offline",
      needs: [{ need: "手を使わない", status: "simulated", blocks: [{ id: "follow_head", label: "頭上に追従" }], how: "頭の上に浮いてついてくる", note: "" },
        { need: "濡れない", status: "simplified", blocks: [{ id: "rain_shield", label: "雨よけ" }], how: "", note: "雨粒の数で概算" }],
      parts: [
        { id: "core", type: "body", label: "本体", role: "中心の部品", shape: "cylinder", material: "plastic", sizeM: [0.12, 0.05, 0.12], massKg: 0.28 },
        { id: "shield", type: "canopy", label: "キャノピー", role: "雨を受ける", shape: "cylinder", material: "fabric", sizeM: [1.6, 0.02, 1.6], massKg: null },
      ],
      experiments: [{
        revision: 1, title: "横風の雨", mode: "dream", conditions: [{ label: "雨量", value: "30 mm/h" }],
        metrics: [{ key: "coverageRatio", label: "防護率", value: 0.409, unit: "%", display: "40.9%", lowerIsBetter: false, primary: true }],
        passed: false, criteria: "防護率 70% 以上", seed: 2026, notes: [], at: "2026-11-25T19:00:00",
      }],
      revisions: [{ revision: 2, parent: 1, source: "offline", request: "横からの雨も防いで", summary: "側面カバーを足す",
        changes: [{ label: "側面カバーの長さ", before: "0.00", after: "0.25" }], resultChanged: true }],
      research: { unverifiable: [], assumptions: ["雨粒はまっすぐ落ちる"], simplified: ["風は一定"] },
      developer: { totalMassKg: 0.52, tipAngleDeg: null, powerW: 12, runtimeMin: 45, challenges: ["浮かぶしくみは現実には無い"] },
    },
  };
}

function freshStore() {
  const dir = mkdtempSync(path.join(tmpdir(), "ilab-lib-"));
  let n = 0, t = 0;
  const store = new LibraryStore({
    dir,
    now: () => new Date(Date.UTC(2026, 10, 25, 12, 0, t++)).toISOString(),
    newId: (p) => `${p}-${String(++n).padStart(4, "0")}`,
  });
  return { dir, store };
}

const pub = (over?: Parameters<typeof publication>[0]) => Publication.parse(publication(over));

function code(fn: () => unknown): string {
  try { fn(); } catch (e) { if (e instanceof HttpError) return `${e.status} ${e.code}`; throw e; }
  return "ok";
}

// ---------------- 形の検証 ----------------
test("publication: the sample passes and unknown fields are dropped", () => {
  const raw = publication() as Record<string, unknown>;
  (raw.invention as Record<string, unknown>).script = "rm -rf /";
  const p = Publication.parse(raw);
  assert.equal((p.invention as Record<string, unknown>).script, undefined);
});

test("publication: rejects missing consent, wrong consent version, long text, bad ids, too many parts", () => {
  const bad = (f: (p: ReturnType<typeof publication>) => void) => { const p = publication(); f(p); return Publication.safeParse(p).success; };
  assert.equal(bad((p) => { (p.consent as { agreed: boolean }).agreed = false; }), false);
  assert.equal(bad((p) => { p.consent.version = "old"; }), false);
  assert.equal(bad((p) => { p.invention.request = "あ".repeat(401); }), false);
  assert.equal(bad((p) => { p.invention.parts[0].id = "../core"; }), false);
  assert.equal(bad((p) => { p.invention.name = "  "; }), false);
  assert.equal(bad((p) => { p.invention.parts = Array.from({ length: 25 }, (_, i) => ({ ...p.invention.parts[0], id: "p" + i })); }), false);
  assert.equal(bad((p) => { p.invention.experiments[0].metrics[0].value = Number.NaN; }), false);
  assert.equal(bad((p) => { p.invention.name = "改行\nあり"; }), false);
  assert.equal(bad(() => {}), true);
});

test("checkGlb: accepts a GLB and rejects other files", () => {
  assert.equal(checkGlb(tinyGlb()), null);
  assert.match(checkGlb(Buffer.from("not a glb at all, just some text here"))!, /glTF/);
  const cut = tinyGlb().subarray(0, 30);
  assert.ok(checkGlb(Buffer.from(cut)));
  assert.match(checkGlb(Buffer.alloc(2 * 1024 * 1024))!, /大きすぎ/);
});

// ---------------- 公開・更新・取り下げ ----------------
test("publish: creates once per device and invention, republish updates and keeps counts", () => {
  const { store } = freshStore();
  const a = store.publish(KEY_A, pub(), tinyGlb());
  assert.deepEqual(a, { id: "p-0001", created: true });
  store.wantSite(a.id, "browser-aaaaaaaa");
  const again = store.publish(KEY_A, pub({ name: "雨よけ 改" }), tinyGlb());
  assert.deepEqual(again, { id: "p-0001", created: false });
  assert.equal(store.get(a.id).invention.name, "雨よけ 改");
  assert.equal(store.get(a.id).wants, 1, "更新してもほしいの数は残る");
  // 別の端末の同じ発明 ID は別の公開
  assert.equal(store.publish(KEY_B, pub(), tinyGlb()).created, true);
  assert.equal(store.list().items.length, 2);
});

test("publish: rejects a broken GLB and duplicate part ids", () => {
  const { store } = freshStore();
  assert.equal(code(() => store.publish(KEY_A, pub(), Buffer.from("x".repeat(40)))), "400 bad_glb");
  const p = pub();
  p.invention.parts[1].id = "core";
  assert.equal(code(() => store.publish(KEY_A, p, tinyGlb())), "400 bad_publication");
});

test("withdraw: only the same device; withdrawn pages disappear and return 410; republish restores", () => {
  const { store } = freshStore();
  const { id } = store.publish(KEY_A, pub(), tinyGlb());
  assert.equal(code(() => store.withdraw(id, KEY_B)), "403 not_owner");
  store.withdraw(id, KEY_A);
  assert.equal(store.list().items.length, 0);
  assert.equal(code(() => store.get(id)), "410 withdrawn");
  assert.equal(store.mine(KEY_A)[0].status, "withdrawn");
  store.publish(KEY_A, pub(), tinyGlb());
  assert.equal(store.list().items.length, 1);
});

test("takedown by the operator cannot be undone by republishing", () => {
  const { store } = freshStore();
  const { id } = store.publish(KEY_A, pub(), tinyGlb());
  store.takedown(id, "規約9に当たる");
  assert.equal(code(() => store.publish(KEY_A, pub(), tinyGlb())), "403 taken_down");
  assert.equal(store.adminList()[0].withdrawnBy, "admin");
});

// ---------------- ほしい・試した ----------------
test("wants: site once per browser, VR only after trying and once per device", () => {
  const { store } = freshStore();
  const { id } = store.publish(KEY_A, pub(), tinyGlb());
  assert.equal(store.wantSite(id, "browser-aaaaaaaa").added, true);
  assert.equal(store.wantSite(id, "browser-aaaaaaaa").added, false);
  assert.equal(code(() => store.wantVr(id, KEY_B)), "409 not_tried");
  store.tried(id, KEY_B);
  store.tried(id, KEY_B);
  assert.equal(store.wantVr(id, KEY_B).wants, 2, "ほしいは VR とサイトを合わせた人数");
  assert.equal(store.wantVr(id, KEY_B).added, false);
  assert.equal(store.get(id).wants, 2);
  assert.equal(store.mine(KEY_A)[0].tried, 1, "試した数は発案者にだけ返す");
});

// ---------------- 一覧の並びと絞り込み ----------------
test("list: sorts by wants, new, name and creator adoptions, and filters by tag and status", () => {
  const { store } = freshStore();
  const r1 = store.publish(KEY_A, pub({ id: "a", name: "うちわ", tag: "comfort" }), tinyGlb()).id;
  const r2 = store.publish(KEY_A, pub({ id: "b", name: "あまがさ", tag: "protection" }), tinyGlb()).id;
  const r3 = store.publish(KEY_B, pub({ id: "c", name: "かばん", tag: "carrier" }), tinyGlb()).id;
  store.wantSite(r2, "browser-11111111"); store.wantSite(r2, "browser-22222222"); store.wantSite(r3, "browser-11111111");
  const u = store.createAccount({ name: "山田", affiliation: "大学", field: "材料", intro: "" });
  store.adopt(r3, u.id, "研究テーマに近い");
  const ids = (sort: string) => store.list({ sort }).items.map((x) => x.id);
  assert.deepEqual(ids("wants"), [r2, r3, r1]);
  assert.deepEqual(ids("new"), [r3, r2, r1]);
  assert.deepEqual(ids("name"), [r2, r1, r3], "名前順（あ・う・か）");
  assert.equal(ids("adoptions")[0], r3, "採用された発案者が先");
  assert.deepEqual(store.list({ tag: "protection" }).items.map((x) => x.id), [r2]);
  assert.deepEqual(store.list({ status: "adopted" }).items.map((x) => x.id), [r3]);
  assert.equal(store.list({ sort: "drop table" }).sort, "wants");
  // 取り付け位置（頭の上のものは、サイトの 3D で床に置かずに見上げる）
  assert.deepEqual(store.list().items[0].mount, { id: "head", label: "頭" });
});

// ---------------- 検討中・採用・やりとり ----------------
test("adopt: needs a registered account and a reason; creates a private thread for the two", () => {
  const { store } = freshStore();
  const { id } = store.publish(KEY_A, pub(), tinyGlb());
  const res = store.createAccount({ name: "山田", affiliation: "大学", field: "流体", intro: "雨の研究" });
  const dev = store.createAccount({ name: "佐藤", affiliation: "メーカー", field: "雨具", intro: "" });
  assert.equal(code(() => store.adopt(id, "u-9999", "理由")), "404 no_account");
  store.consider(id, dev.id);
  assert.equal(store.get(id).status, "considering");
  const a = store.adopt(id, res.id, "雨粒の当たり方の研究に使える");
  assert.equal(a.status, "adopted");
  assert.equal(code(() => store.adopt(id, res.id, "もう一度")), "409 already_adopted");
  const b = store.adopt(id, dev.id, "製品化を検討したい");
  assert.notEqual(a.threadId, b.threadId, "採用した人ごとに別のやりとり");

  const creator = { kind: "creator", hash: creatorHashOf(KEY_A) } as const;
  const researcher = { kind: "account", id: res.id } as const;
  const developer = { kind: "account", id: dev.id } as const;
  store.postMessage(a.threadId, researcher, "はじめまして");
  store.postMessage(a.threadId, creator, "ありがとうございます");
  assert.deepEqual(store.thread(a.threadId, creator).messages.map((m) => m.from), ["adopter", "creator"]);
  assert.equal(store.threads(creator).length, 2);
  assert.equal(store.threads(developer).length, 1);
  // 相手でない人には見えない（あることも分からない）
  assert.equal(code(() => store.thread(a.threadId, developer)), "404 not_found");
  assert.equal(code(() => store.postMessage(a.threadId, { kind: "creator", hash: creatorHashOf(KEY_B) }, "割り込み")), "404 not_found");
  // Quest の自分の公開：採用の知らせ・やりとりの数
  const mine = store.mine(KEY_A)[0];
  assert.equal(mine.status, "adopted");
  assert.equal(mine.adoptions.length, 2);
  assert.equal(mine.adoptions[0].reason, "雨粒の当たり方の研究に使える");
  assert.equal(mine.adoptions[0].messageCount, 2);
  assert.equal(mine.adoptions[0].lastFrom, "creator");
  // 採用した人の所属と紹介は、発案者（やりとりの相手）には見える
  assert.equal(mine.adoptions[0].adopter.affiliation, "大学");
  assert.equal(store.thread(a.threadId, creator).adopter.intro, "雨の研究");
  assert.deepEqual(store.accountActivity(res.id).adopted.map((x) => x.id), [id]);
});

/** 詳しい内容つきの発明（だれでも見られる形には無い欄を読む）。 */
const full = (page: { invention: unknown }) => page.invention as { request: string; parts: unknown[] };

test("privacy: the public page has no adopter names; details are for developers, the owner and operators", () => {
  const { store } = freshStore();
  const { id } = store.publish(KEY_A, pub(), tinyGlb());
  const res = store.createAccount({ name: "山田", affiliation: "北辰大学", field: "流体", intro: "雨の研究" });
  const dev = store.createAccount({ name: "佐藤", affiliation: "メーカー", field: "雨具", intro: "" });
  store.consider(id, dev.id);
  store.adopt(id, res.id, "研究に使える");
  // だれでも見られるページ：採用と検討中は数だけ。依頼の原文・部品・改良の履歴は載らない
  const pub1 = store.get(id);
  assert.equal(pub1.adoptions, 1);
  assert.equal(pub1.considering, 1);
  const open = JSON.stringify([pub1, store.list()]);
  for (const secret of ["山田", "北辰大学", "佐藤", "手を使わず雨を防ぐ装置を作って", '"parts"', '"revisions"', '"conditions"']) {
    assert.ok(!open.includes(secret), `だれでも見られる所に ${secret} が出ない`);
  }
  assert.equal(pub1.invention.partCount, 2);
  assert.ok(pub1.invention.experiments.every((e) => e.metrics.every((m) => m.primary)), "実験は主な計測と合否だけ");
  // 検討中の知らせには、だれかを書かない
  const notes = store.members.notices({ kind: "creator", hash: creatorHashOf(KEY_A) });
  assert.equal(notes.find((n) => n.kind === "considering")?.fromName, undefined);
  assert.equal(notes.find((n) => n.kind === "adopted")?.fromName, "山田");
  // 詳しい内容：開発者・公開した発案者・運営は読める。ほかの発案者は読めない
  const d = store.detail(id, { kind: "account", id: dev.id });
  assert.equal(full(d).request, "手を使わず雨を防ぐ装置を作って");
  assert.deepEqual(d.mine, { adopted: false, threadId: null, considering: true });
  assert.equal(store.detail(id, { kind: "account", id: res.id }).mine?.adopted, true);
  assert.equal(full(store.detail(id, { kind: "creator", hash: creatorHashOf(KEY_A) })).parts.length, 2);
  const other = store.detail(id, { kind: "creator", hash: creatorHashOf(KEY_B) });
  assert.equal(other.detail, false, "ほかの発案者には詳しい内容を見せない");
  assert.ok(!JSON.stringify(other).includes("手を使わず雨を防ぐ装置を作って"));
  // 読んだ記録は、運営の一覧に残る（同じ人は1回）
  store.detail(id, { kind: "account", id: dev.id });
  const viewers = store.adminList()[0].viewers;
  assert.deepEqual(viewers.map((v) => v.name), ["佐藤", "山田"]);
  assert.equal(store.adminAccounts().find((a) => a.id === dev.id)?.viewed, 1);
});

test("anonymous creators are shown as 発案者 and have no public page", () => {
  const { store } = freshStore();
  const anon = store.publish(KEY_A, pub({ id: "x", anonymous: true }), tinyGlb()).id;
  assert.deepEqual(store.get(anon).creator, { name: "発案者", publicId: null });
  assert.equal(code(() => store.creator(publicCreatorId(creatorHashOf(KEY_A)))), "404 not_found");
  // 表示名で公開した分だけで発案者のページを作る
  store.publish(KEY_A, pub({ id: "y", displayName: "あおい" }), tinyGlb());
  const page = store.creator(publicCreatorId(creatorHashOf(KEY_A)));
  assert.equal(page.published, 1);
  assert.equal(page.displayName, "あおい");
  // 発案者のハッシュや鍵は外に出ない
  const all = JSON.stringify([store.list(), store.get(anon), page]);
  assert.ok(!all.includes(creatorHashOf(KEY_A)) && !all.includes(KEY_A));
});

test("verify mark and persistence across restarts", () => {
  const { dir, store } = freshStore();
  const { id } = store.publish(KEY_A, pub(), tinyGlb());
  const u = store.createAccount({ name: "山田", affiliation: "大学", field: "", intro: "" });
  store.verifyAccount(u.id, true);
  store.adopt(id, u.id, "研究に使う");
  const again = new LibraryStore({ dir });
  assert.equal(again.mine(KEY_A)[0].adoptions[0].adopter.verified, true);
  assert.equal(full(again.detail(id, { kind: "account", id: u.id })).request, "手を使わず雨を防ぐ装置を作って");
  assert.ok(checkGlb(again.model(id)) === null);
  // 鍵はファイルにも残さない
  assert.ok(!readFileSync(path.join(dir, "db.json"), "utf8").includes(KEY_A));
});

// ---------------- HTTP ----------------
async function withServer(fn: (base: string) => Promise<void>, token = "") {
  const { store } = freshStore();
  const handle = createLibraryRoutes(store, { token });
  const server: Server = createServer((req, res) => {
    const url = new URL(req.url ?? "/", "http://localhost");
    handle(req, res, url)
      .then((ok) => { if (!ok) sendJson(res, 404, { status: "error", code: "not_found" }); })
      .catch((e: unknown) => {
        if (e instanceof HttpError) sendJson(res, e.status, { status: "error", code: e.code, message: e.message });
        else { sendJson(res, 500, { status: "error", code: "internal" }); }
      });
  });
  await new Promise<void>((r) => server.listen(0, "127.0.0.1", r));
  const base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
  try { await fn(base); } finally { server.close(); }
}

/** ログインの記録（cookie）を、次の要求に付ける形で取り出す。 */
const sid = (r: Response) => /ilab_sid=[^;]+/.exec(r.headers.get("set-cookie") ?? "")?.[0] ?? "";

const post = (base: string, p: string, body: unknown, headers: Record<string, string> = {}) =>
  fetch(base + p, { method: "POST", headers: { "content-type": "application/json", ...headers }, body: JSON.stringify(body) });

test("http: publish → list → page → model → want → register → adopt → messages → Quest status", async () => {
  await withServer(async (base) => {
    const glbBase64 = tinyGlb().toString("base64");
    let r = await post(base, "/library", { publication: publication(), glbBase64 });
    assert.equal(r.status, 401, "端末の鍵が要る");
    r = await post(base, "/library", { publication: publication(), glbBase64 }, { "x-ilab-creator": KEY_A });
    assert.equal(r.status, 201);
    const { id } = await r.json() as { id: string };

    const list = await (await fetch(base + "/library?sort=new")).json() as { items: { id: string; creator: { name: string } }[] };
    assert.equal(list.items[0].id, id);
    assert.equal(list.items[0].creator.name, "あおい");
    const page = await (await fetch(`${base}/library/${id}`)).json() as { detail: boolean; invention: { name: string; request?: string } };
    assert.equal(page.detail, false);
    assert.equal(page.invention.request, undefined, "依頼の原文は、だれでも見られるページに載せない");
    assert.equal((await fetch(`${base}/api/library/${id}`)).status, 401, "詳しい内容はログインが要る");
    const model = await fetch(`${base}/library/${id}/model.glb`);
    assert.equal(model.headers.get("content-type"), "model/gltf-binary");
    assert.equal(checkGlb(Buffer.from(await model.arrayBuffer())), null);

    r = await post(base, `/library/${id}/want`, { voter: "browser-aaaaaaaa" });
    assert.equal(((await r.json()) as { wants: unknown }).wants, 1);
    r = await post(base, `/library/${id}/want`, {}, { "x-ilab-creator": KEY_B });
    assert.equal(r.status, 409, "VR は試してから");
    await post(base, `/library/${id}/tried`, {}, { "x-ilab-creator": KEY_B });
    r = await post(base, `/library/${id}/want`, {}, { "x-ilab-creator": KEY_B });
    assert.equal(((await r.json()) as { wants: unknown }).wants, 2);

    r = await post(base, `/api/library/${id}/adopt`, { reason: "研究テーマにしたい" });
    assert.equal(r.status, 401, "採用はログインが要る");
    r = await post(base, "/api/signup", { name: "山田", affiliation: "大学", field: "流体", intro: "雨の研究", email: "Yamada@Example.jp", password: "rain-research", agreed: true });
    assert.equal(r.status, 201);
    const cookie = sid(r);
    const full = await (await fetch(`${base}/api/library/${id}`, { headers: { cookie } })).json() as { detail: boolean; invention: { request: string } };
    assert.equal(full.detail, true);
    assert.equal(full.invention.request, "手を使わず雨を防ぐ装置を作って");
    r = await post(base, `/api/library/${id}/adopt`, { reason: " " }, { cookie });
    assert.equal(r.status, 400, "理由は必須");
    r = await post(base, `/api/library/${id}/adopt`, { reason: "研究テーマにしたい" }, { cookie });
    const { threadId } = await r.json() as { threadId: string };

    r = await post(base, `/api/threads/${threadId}/messages`, { text: "はじめまして" }, { cookie });
    assert.equal(r.status, 201);
    r = await post(base, `/threads/${threadId}/messages`, { text: "よろしくお願いします" }, { "x-ilab-creator": KEY_A });
    assert.equal(r.status, 201);
    r = await post(base, `/threads/${threadId}/messages`, { text: "のぞき見" }, { "x-ilab-creator": KEY_B });
    assert.equal(r.status, 404);
    // サイトでは、Quest が出した番号で発案者として入る
    const { code: pairCode } = await (await post(base, "/library/pair", {}, { "x-ilab-creator": KEY_A })).json() as { code: string };
    assert.match(pairCode, /^\d{6}$/);
    const asCreatorCookie = sid(await post(base, "/api/pair", { code: pairCode }));
    const asCreator = await (await fetch(`${base}/api/threads/${threadId}`, { headers: { cookie: asCreatorCookie } })).json() as { messages: { from: string }[] };
    assert.deepEqual(asCreator.messages.map((m) => m.from), ["adopter", "creator"]);
    assert.equal((await post(base, "/api/pair", { code: pairCode })).status, 401, "番号は1回だけ");

    const mine = await (await fetch(base + "/library/mine", { headers: { "x-ilab-creator": KEY_A } })).json() as { items: { status: string; adoptions: { messageCount: number }[] }[] };
    assert.equal(mine.items[0].status, "adopted");
    assert.equal(mine.items[0].adoptions[0].messageCount, 2);

    r = await post(base, `/library/${id}/withdraw`, {}, { "x-ilab-creator": KEY_A });
    assert.equal(r.status, 200);
    assert.equal((await fetch(`${base}/library/${id}`)).status, 410);
  });
});

test("http: bad input gets a short reason; unknown paths and methods are refused", async () => {
  await withServer(async (base) => {
    const p = publication();
    p.invention.request = "あ".repeat(500);
    let r = await post(base, "/library", { publication: p, glbBase64: tinyGlb().toString("base64") }, { "x-ilab-creator": KEY_A });
    assert.equal(r.status, 400);
    assert.match(((await r.json()) as { message: string }).message, /invention\.request/);
    r = await fetch(base + "/library", { method: "POST", headers: { "content-type": "text/plain", "x-ilab-creator": KEY_A }, body: "x" });
    assert.equal(r.status, 415);
    assert.equal((await fetch(base + "/library/p-9999")).status, 404);
    assert.equal((await fetch(base + "/library/P_bad!")).status, 404);
    assert.equal((await fetch(base + "/api/session", { method: "DELETE" })).status, 405);
    assert.equal((await fetch(base + "/threads")).status, 401, "端末の鍵が要る");
    assert.equal((await fetch(base + "/api/threads")).status, 401, "ログインが要る");
  });
});

test("http: with a token set, Quest calls need it", async () => {
  await withServer(async (base) => {
    const body = { publication: publication(), glbBase64: tinyGlb().toString("base64") };
    assert.equal((await post(base, "/library", body, { "x-ilab-creator": KEY_A })).status, 401);
    assert.equal((await post(base, "/library", body, { "x-ilab-creator": KEY_A, "x-ilab-token": "secret" })).status, 201);
    // サイトの読み出し（この PC の中から）はトークン無しでよい
    assert.equal((await fetch(base + "/library")).status, 200);
  }, "secret");
});

test("list: each item carries the first and last tested revision of its main experiment", () => {
  const { store } = freshStore();
  const p = publication();
  p.invention.experiments.push({ ...p.invention.experiments[0], revision: 3, passed: true,
    metrics: [{ ...p.invention.experiments[0].metrics[0], value: 0.664, display: "66.4" }] });
  p.invention.experiments.push({ ...p.invention.experiments[0], revision: 3, mode: "reality", title: "横風の雨",
    metrics: [{ ...p.invention.experiments[0].metrics[0], value: 0.307, display: "30.7" }] });
  const { id } = store.publish(KEY_A, Publication.parse(p), tinyGlb());
  const r = store.list().items.find((x) => x.id === id)!.result!;
  assert.equal(r.mode, "dream", "DREAM を先に見る");
  assert.deepEqual(r.before, { revision: 1, display: "40.9%", passed: false });
  assert.deepEqual(r.after, { revision: 3, display: "66.4", passed: true });
  assert.equal(r.label, "防護率");
  // 実験が1回だけなら、前は無い
  const one = Publication.parse(publication());
  assert.equal(resultSummary(one.invention)!.before, null);
  // 実験が無ければ要約も無い
  const none = Publication.parse(publication()); none.invention.experiments = [];
  assert.equal(resultSummary(none.invention), null);
});

// ---------------- 写真 ----------------
const tinyJpeg = () => Buffer.concat([Buffer.from([0xff, 0xd8, 0xff, 0xe0]), Buffer.alloc(60, 1), Buffer.from([0xff, 0xd9])]);

test("images: stored with the publication, listed with urls, replaced on republish, and checked", () => {
  const { store } = freshStore();
  const { id } = store.publish(KEY_A, pub(), tinyGlb(), [
    { kind: "stage", caption: "発明台に置いた Rev.3", revision: 3, jpg: tinyJpeg() },
    { kind: "experiment", caption: "Rev.1 の実験", revision: 1, jpg: tinyJpeg() },
  ]);
  const page = store.get(id);
  assert.deepEqual(page.images.map((m) => [m.kind, m.url]), [["stage", `/library/${id}/images/0.jpg`], ["experiment", `/library/${id}/images/1.jpg`]]);
  assert.equal(store.list().items[0].thumb, `/library/${id}/images/0.jpg`, "一覧には発明台の写真");
  assert.equal(store.image(id, 1)[0], 0xff);
  store.publish(KEY_A, pub(), tinyGlb(), []);
  assert.equal(store.get(id).images.length, 0, "公開し直すと入れ替わる");
  assert.equal(code(() => store.image(id, 0)), "404 not_found");
  assert.equal(code(() => store.publish(KEY_A, pub(), tinyGlb(), [{ kind: "stage", caption: "", revision: 1, jpg: Buffer.from("PNG.....") }])), "400 bad_image");
  assert.equal(code(() => store.publish(KEY_A, pub(), tinyGlb(), [{ kind: "stage", caption: "", revision: 1, jpg: Buffer.concat([tinyJpeg(), Buffer.alloc(400 * 1024)]) }])), "400 bad_image");
});

test("http: photos come with the publish request and are served as JPEG", async () => {
  await withServer(async (base) => {
    const r = await post(base, "/library", {
      publication: publication(), glbBase64: tinyGlb().toString("base64"),
      images: [{ kind: "stage", caption: "発明台", revision: 3, jpgBase64: tinyJpeg().toString("base64") }],
    }, { "x-ilab-creator": KEY_A });
    assert.equal(r.status, 201);
    const { id } = await r.json() as { id: string };
    const img = await fetch(`${base}/library/${id}/images/0.jpg`);
    assert.equal(img.headers.get("content-type"), "image/jpeg");
    assert.equal((await fetch(`${base}/library/${id}/images/3.jpg`)).status, 404);
    const bad = await post(base, "/library", {
      publication: publication(), glbBase64: tinyGlb().toString("base64"),
      images: [{ kind: "selfie", caption: "", revision: 1, jpgBase64: "" }],
    }, { "x-ilab-creator": KEY_A });
    assert.equal(bad.status, 400, "知らない種類の写真は受け付けない");
  });
});

// ---------------- Unity（C# の PublicationBuilder）が作った公開データ ----------------
test("contract: a publication built by the app passes the library schema", () => {
  const raw = JSON.parse(readFileSync(path.join(import.meta.dirname, "fixtures", "publication-rain.json"), "utf8"));
  const r = Publication.safeParse(raw);
  assert.ok(r.success, r.success ? "" : JSON.stringify(r.error.issues.slice(0, 3)));
  const inv = r.data!.invention;
  assert.equal(inv.request, "手を使わず雨を防ぐ装置を作って");
  assert.ok(inv.experiments.some((e) => e.mode === "reality"));
  assert.ok(inv.revisions.every((x) => x.revision > x.parent));
  // 一覧の要約も作れる（DREAM の Rev.1 → Rev.3）
  const s = resultSummary(inv)!;
  assert.equal(s.before?.revision, 1);
  assert.equal(s.after.revision, 3);
});

test("operator's account list carries adoption counts; creators list only named, live creators", () => {
  const { store } = freshStore();
  const named = store.publish(KEY_A, pub({ id: "n1", displayName: "あおい" }), tinyGlb()).id;
  store.publish(KEY_B, pub({ id: "a1", anonymous: true }), tinyGlb());
  const res = store.createAccount({ name: "山田", affiliation: "大学", field: "", intro: "" });
  assert.equal(store.adminAccounts()[0].adoptions, 0);
  store.adopt(named, res.id, "研究に使える");
  assert.equal(store.adminAccounts()[0].adoptions, 1);
  assert.deepEqual(store.listCreators(), [{ publicId: publicCreatorId(creatorHashOf(KEY_A)), name: "あおい", published: 1 }]);
  // 取り下げた発明は、採用の数にも発案者の一覧にも入らない
  store.withdraw(named, KEY_A);
  assert.equal(store.adminAccounts()[0].adoptions, 0);
  assert.deepEqual(store.listCreators(), []);
});

test("samples: loaded under a named sample creator, sample accounts are added once", async () => {
  const { loadSamples, loadSampleAccounts, SAMPLE_ACCOUNTS, SAMPLE_CREATOR_NAME } = await import("../tools/load-samples.ts");
  const { mkdirSync, writeFileSync } = await import("node:fs");
  const from = mkdtempSync(path.join(tmpdir(), "ilab-samples-"));
  const dir = path.join(from, "inv-handsfree-rain");
  mkdirSync(dir);
  writeFileSync(path.join(dir, "publication.json"), readFileSync(path.join(import.meta.dirname, "fixtures", "publication-rain.json")));
  writeFileSync(path.join(dir, "model.glb"), tinyGlb());
  const { store } = freshStore();
  const [id] = loadSamples(from, store);
  assert.equal(store.get(id).creator.name, SAMPLE_CREATOR_NAME);
  assert.equal(store.get(id).sample, true);
  assert.equal(loadSamples(from, store)[0], id, "入れ直しても増えない");
  loadSampleAccounts(store);
  loadSampleAccounts(store);
  const accounts = store.adminAccounts();
  assert.equal(accounts.length, SAMPLE_ACCOUNTS.length, "何度入れても増えない");
  assert.ok(accounts.every((a) => a.sample));
  assert.deepEqual(accounts.map((a) => a.verified), SAMPLE_ACCOUNTS.map((a) => a.verified));
});

test("http: a named creator has a public page", async () => {
  await withServer(async (base) => {
    await post(base, "/library", { publication: publication(), glbBase64: tinyGlb().toString("base64") }, { "x-ilab-creator": KEY_A });
    const page = await (await fetch(`${base}/api/creators/${publicCreatorId(creatorHashOf(KEY_A))}`)).json() as { displayName: string };
    assert.equal(page.displayName, "あおい");
  });
});
