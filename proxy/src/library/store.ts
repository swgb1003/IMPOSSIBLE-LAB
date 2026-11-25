// 発明ライブラリの保存と操作（docs/PUBLISH_LIBRARY.md 3・7章）。PC の中だけで動かし、ネットには出さない。
// 保存先：<dir>/db.json（状態・数・登録・やりとり）と <dir>/pubs/<公開ID>.json・.glb（公開データと 3D）。
// 発案者の端末の鍵（creatorKey）はそのまま保存せず、ハッシュだけを持つ。依頼文などの本文はログに書かない。
import { createHash, randomBytes } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync, rmSync } from "node:fs";
import path from "node:path";
import { HttpError } from "../http-util.ts";
import type { Publication, Role } from "./schema.ts";

export const LIMITS = {
  maxPublications: 500,
  maxPerCreator: 100,
  maxAccounts: 200,
  maxThreads: 500,
  maxMessagesPerThread: 200,
  maxGlbBytes: 1024 * 1024,
};

export type PubStatus = "open" | "considering" | "adopted";
export type SortKey = "wants" | "new" | "adoptions" | "name";

interface Adoption { id: string; accountId: string; reason: string; at: string; threadId: string }
interface Considering { accountId: string; at: string }
interface PubRecord {
  id: string;
  creatorHash: string;
  inventionId: string;
  displayName: string;
  anonymous: boolean;
  consentVersion: string;
  consentAt: string;
  publishedAt: string;
  updatedAt: string;
  withdrawn: boolean;
  withdrawnBy: "creator" | "admin" | null;
  withdrawReason: string;
  /** ほしい：サイトはブラウザごと、VR は端末ごと（どちらもハッシュ）。 */
  wantsSite: string[];
  wantsVr: string[];
  /** VR で呼び出して試した端末。 */
  tried: string[];
  considering: Considering[];
  adoptions: Adoption[];
  /** 見本（見本と明記して出す）。 */
  sample: boolean;
}
export interface AccountRecord {
  id: string; name: string; affiliation: string; role: Role; field: string; intro: string;
  verified: boolean; createdAt: string; sample: boolean;
}
interface Message { id: string; from: "adopter" | "creator"; text: string; at: string }
interface ThreadRecord { id: string; pubId: string; accountId: string; creatorHash: string; createdAt: string; messages: Message[] }
interface Db { version: 1; pubs: PubRecord[]; accounts: AccountRecord[]; threads: ThreadRecord[] }

/** やりとりの相手として名乗る人。Quest は端末の鍵、サイトは選んだアカウントか発案者（ログインは形だけ）。 */
export type Identity = { kind: "creator"; hash: string } | { kind: "account"; id: string };

export interface StoreOptions {
  dir: string;
  now?: () => string;
  newId?: (prefix: string) => string;
}

const hash = (s: string) => createHash("sha256").update(s, "utf8").digest("hex");
/** 端末の鍵から、公開してよい発案者の ID を作る（鍵は戻せない）。 */
export const publicCreatorId = (creatorHash: string) => "c-" + hash("public:" + creatorHash).slice(0, 12);
export const creatorHashOf = (creatorKey: string) => hash("creator:" + creatorKey);

/** GLB の見出しと JSON の区切りを確かめる（中身の三角形までは見ない）。 */
export function checkGlb(b: Buffer): string | null {
  if (b.length < 28) return "3D のファイルが短すぎます";
  if (b.length > LIMITS.maxGlbBytes) return `3D のファイルが大きすぎます（${LIMITS.maxGlbBytes / 1024} KB まで）`;
  if (b.readUInt32LE(0) !== 0x46546c67) return "glTF（GLB）のファイルではありません";
  if (b.readUInt32LE(4) !== 2) return "glTF の版が 2 ではありません";
  if (b.readUInt32LE(8) !== b.length) return "GLB の長さが合いません";
  const jsonLen = b.readUInt32LE(12);
  if (b.readUInt32LE(16) !== 0x4e4f534a || 20 + jsonLen > b.length) return "GLB の JSON の区切りが正しくありません";
  try {
    const j = JSON.parse(b.subarray(20, 20 + jsonLen).toString("utf8")) as { asset?: { version?: string } };
    if (j?.asset?.version !== "2.0") return "glTF の版が 2.0 ではありません";
  } catch { return "GLB の JSON を読めません"; }
  return null;
}

export class LibraryStore {
  private dir: string;
  private now: () => string;
  private newId: (prefix: string) => string;
  private db: Db = { version: 1, pubs: [], accounts: [], threads: [] };
  private pubData = new Map<string, Publication>();

  constructor(o: StoreOptions) {
    this.dir = o.dir;
    this.now = o.now ?? (() => new Date().toISOString());
    this.newId = o.newId ?? ((p) => `${p}-${randomBytes(5).toString("hex")}`);
    mkdirSync(path.join(this.dir, "pubs"), { recursive: true });
    const dbPath = path.join(this.dir, "db.json");
    if (existsSync(dbPath)) {
      const raw = JSON.parse(readFileSync(dbPath, "utf8")) as Db;
      if (raw?.version !== 1) throw new Error("library/db.json の形式が非対応です");
      this.db = { version: 1, pubs: raw.pubs ?? [], accounts: raw.accounts ?? [], threads: raw.threads ?? [] };
      for (const p of this.db.pubs) {
        const f = this.pubPath(p.id, ".json");
        if (existsSync(f)) this.pubData.set(p.id, JSON.parse(readFileSync(f, "utf8")) as Publication);
      }
      // 公開データが無い記録（手で消したなど）は出さない
      this.db.pubs = this.db.pubs.filter((p) => this.pubData.has(p.id));
    }
  }

  // ---------------- 保存 ----------------
  private pubPath(id: string, ext: string) {
    if (!/^[a-z0-9-]{3,40}$/.test(id)) throw new HttpError(400, "bad_id", "公開 ID が不正です");
    return path.join(this.dir, "pubs", id + ext);
  }
  private writeAtomic(file: string, data: string | Buffer) {
    const tmp = file + ".tmp";
    writeFileSync(tmp, data);
    renameSync(tmp, file);
  }
  private save() { this.writeAtomic(path.join(this.dir, "db.json"), JSON.stringify(this.db, null, 1)); }

  // ---------------- 公開（Quest から） ----------------
  /** 公開する。同じ端末の同じ発明はページを更新する（ほしい・採用の数は残し、取り下げていたら戻す）。 */
  publish(creatorKey: string, publication: Publication, glb: Buffer): { id: string; created: boolean } {
    const err = checkGlb(glb);
    if (err) throw new HttpError(400, "bad_glb", err);
    const creatorHash = creatorHashOf(creatorKey);
    const inv = publication.invention;
    const partIds = new Set(inv.parts.map((p) => p.id));
    if (partIds.size !== inv.parts.length) throw new HttpError(400, "bad_publication", "部品の ID が重なっています");
    let rec = this.db.pubs.find((p) => p.creatorHash === creatorHash && p.inventionId === inv.id);
    const now = this.now();
    const created = !rec;
    if (!rec) {
      if (this.db.pubs.length >= LIMITS.maxPublications) throw new HttpError(507, "library_full", "ライブラリがいっぱいです");
      if (this.db.pubs.filter((p) => p.creatorHash === creatorHash).length >= LIMITS.maxPerCreator) throw new HttpError(429, "too_many", "この端末から公開できる数の上限です");
      rec = {
        id: this.newId("p"), creatorHash, inventionId: inv.id, displayName: "", anonymous: true,
        consentVersion: "", consentAt: "", publishedAt: now, updatedAt: now,
        withdrawn: false, withdrawnBy: null, withdrawReason: "",
        wantsSite: [], wantsVr: [], tried: [], considering: [], adoptions: [], sample: false,
      };
    }
    if (rec.withdrawn && rec.withdrawnBy === "admin") throw new HttpError(403, "taken_down", "運営が取り下げた発明は公開し直せません");
    rec.displayName = publication.creator.anonymous ? "" : publication.creator.displayName.trim();
    rec.anonymous = publication.creator.anonymous || rec.displayName === "";
    rec.consentVersion = publication.consent.version;
    rec.consentAt = publication.consent.agreedAt;
    rec.updatedAt = now;
    rec.withdrawn = false; rec.withdrawnBy = null; rec.withdrawReason = "";
    this.writeAtomic(this.pubPath(rec.id, ".glb"), glb);
    this.writeAtomic(this.pubPath(rec.id, ".json"), JSON.stringify(publication));
    this.pubData.set(rec.id, publication);
    if (created) this.db.pubs.push(rec);
    this.save();
    return { id: rec.id, created };
  }

  withdraw(id: string, creatorKey: string) {
    const rec = this.pubOrThrow(id, true);
    if (rec.creatorHash !== creatorHashOf(creatorKey)) throw new HttpError(403, "not_owner", "この端末で公開した発明ではありません");
    if (!rec.withdrawn) { rec.withdrawn = true; rec.withdrawnBy = "creator"; rec.updatedAt = this.now(); this.save(); }
    return { id, withdrawn: true };
  }

  /** Quest が自分の公開の状態・採用の知らせ・やりとりを取りにくる。 */
  mine(creatorKey: string) {
    const h = creatorHashOf(creatorKey);
    return this.db.pubs.filter((p) => p.creatorHash === h).map((p) => ({
      id: p.id,
      inventionId: p.inventionId,
      revision: this.pubData.get(p.id)!.invention.revision,
      name: this.pubData.get(p.id)!.invention.name,
      status: p.withdrawn ? "withdrawn" : this.status(p),
      withdrawnBy: p.withdrawnBy,
      wants: { vr: p.wantsVr.length, site: p.wantsSite.length },
      tried: p.tried.length,
      adoptions: p.adoptions.map((a) => ({ id: a.id, at: a.at, reason: a.reason, adopter: this.accountPublic(a.accountId), threadId: a.threadId, ...this.threadCounts(a.threadId) })),
      considering: p.considering.length,
    }));
  }

  // ---------------- 一覧と1件（サイト・VR のライブラリ） ----------------
  list(q: { sort?: string; tag?: string; scene?: string; status?: string } = {}) {
    const adoptionsByCreator = new Map<string, number>();
    for (const p of this.db.pubs) adoptionsByCreator.set(p.creatorHash, (adoptionsByCreator.get(p.creatorHash) ?? 0) + p.adoptions.length);
    let items = this.db.pubs.filter((p) => !p.withdrawn).map((p) => ({ rec: p, inv: this.pubData.get(p.id)!.invention }));
    if (q.tag) items = items.filter((x) => x.inv.tags.some((t) => t.id === q.tag));
    if (q.scene) items = items.filter((x) => x.inv.sceneTags.some((t) => t.id === q.scene));
    if (q.status) items = items.filter((x) => this.status(x.rec) === q.status);
    const sort = (["wants", "new", "adoptions", "name"] as const).includes(q.sort as SortKey) ? (q.sort as SortKey) : "wants";
    const wants = (p: PubRecord) => p.wantsVr.length + p.wantsSite.length;
    const newer = (a: PubRecord, b: PubRecord) => b.publishedAt.localeCompare(a.publishedAt);
    items.sort((a, b) => {
      switch (sort) {
        case "new": return newer(a.rec, b.rec);
        case "name": return a.inv.name.localeCompare(b.inv.name, "ja") || newer(a.rec, b.rec);
        case "adoptions": return (adoptionsByCreator.get(b.rec.creatorHash)! - adoptionsByCreator.get(a.rec.creatorHash)!) || newer(a.rec, b.rec);
        default: return (wants(b.rec) - wants(a.rec)) || (b.rec.wantsVr.length - a.rec.wantsVr.length) || newer(a.rec, b.rec);
      }
    });
    return {
      sort,
      items: items.map(({ rec, inv }) => ({
        id: rec.id, name: inv.name, summary: inv.summary, request: inv.request,
        tags: inv.tags, sceneTags: inv.sceneTags, status: this.status(rec), revision: inv.revision,
        creator: this.creatorPublic(rec), creatorAdoptions: adoptionsByCreator.get(rec.creatorHash) ?? 0,
        wants: { vr: rec.wantsVr.length, site: rec.wantsSite.length }, tried: rec.tried.length,
        adoptions: rec.adoptions.length, considering: rec.considering.length,
        publishedAt: rec.publishedAt, updatedAt: rec.updatedAt, sample: rec.sample,
      })),
    };
  }

  get(id: string) {
    const rec = this.pubOrThrow(id);
    return {
      id: rec.id, status: this.status(rec), sample: rec.sample,
      publishedAt: rec.publishedAt, updatedAt: rec.updatedAt, consentVersion: rec.consentVersion,
      creator: this.creatorPublic(rec),
      wants: { vr: rec.wantsVr.length, site: rec.wantsSite.length }, tried: rec.tried.length,
      adoptions: rec.adoptions.map((a) => ({ at: a.at, reason: a.reason, adopter: this.accountPublic(a.accountId) })),
      considering: rec.considering.map((c) => ({ at: c.at, account: this.accountPublic(c.accountId) })),
      invention: this.pubData.get(rec.id)!.invention,
    };
  }

  model(id: string): Buffer {
    const rec = this.pubOrThrow(id);
    return readFileSync(this.pubPath(rec.id, ".glb"));
  }

  // ---------------- ほしい・試した ----------------
  /** サイトのほしい（ブラウザごとに1回）。 */
  wantSite(id: string, voter: string) {
    const rec = this.pubOrThrow(id);
    const h = hash("site:" + voter);
    const added = !rec.wantsSite.includes(h);
    if (added) { rec.wantsSite.push(h); this.save(); }
    return { added, wants: { vr: rec.wantsVr.length, site: rec.wantsSite.length } };
  }

  /** VR で呼び出して試した（端末ごとに1回）。 */
  tried(id: string, creatorKey: string) {
    const rec = this.pubOrThrow(id);
    const h = creatorHashOf(creatorKey);
    const added = !rec.tried.includes(h);
    if (added) { rec.tried.push(h); this.save(); }
    return { added, tried: rec.tried.length };
  }

  /** VR のほしい（試した端末だけ、端末ごとに1回）。 */
  wantVr(id: string, creatorKey: string) {
    const rec = this.pubOrThrow(id);
    const h = creatorHashOf(creatorKey);
    if (!rec.tried.includes(h)) throw new HttpError(409, "not_tried", "VR で試してから、ほしいを押してください");
    const added = !rec.wantsVr.includes(h);
    if (added) { rec.wantsVr.push(h); this.save(); }
    return { added, wants: { vr: rec.wantsVr.length, site: rec.wantsSite.length } };
  }

  // ---------------- 検討中・採用（登録した研究者・開発者） ----------------
  consider(id: string, accountId: string) {
    const rec = this.pubOrThrow(id);
    this.accountOrThrow(accountId);
    if (!rec.considering.some((c) => c.accountId === accountId)) { rec.considering.push({ accountId, at: this.now() }); this.save(); }
    return { status: this.status(rec) };
  }

  /** 採用する。採用した人と発案者の2人だけが読めるやりとりの場を作る（採用した人ごとに1つ）。 */
  adopt(id: string, accountId: string, reason: string) {
    const rec = this.pubOrThrow(id);
    this.accountOrThrow(accountId);
    if (rec.adoptions.some((a) => a.accountId === accountId)) throw new HttpError(409, "already_adopted", "すでに採用しています");
    if (this.db.threads.length >= LIMITS.maxThreads) throw new HttpError(507, "library_full", "やりとりの数の上限です");
    const now = this.now();
    const thread: ThreadRecord = { id: this.newId("t"), pubId: rec.id, accountId, creatorHash: rec.creatorHash, createdAt: now, messages: [] };
    const adoption: Adoption = { id: this.newId("a"), accountId, reason: reason.trim(), at: now, threadId: thread.id };
    rec.adoptions.push(adoption);
    this.db.threads.push(thread);
    this.save();
    return { status: this.status(rec), adoptionId: adoption.id, threadId: thread.id };
  }

  // ---------------- 研究者・開発者の登録 ----------------
  createAccount(input: { name: string; affiliation: string; role: Role; field: string; intro: string }) {
    if (this.db.accounts.length >= LIMITS.maxAccounts) throw new HttpError(507, "library_full", "登録の数の上限です");
    const a: AccountRecord = {
      id: this.newId("u"), name: input.name.trim(), affiliation: input.affiliation.trim(), role: input.role,
      field: input.field.trim(), intro: input.intro.trim(), verified: false, createdAt: this.now(), sample: false,
    };
    this.db.accounts.push(a);
    this.save();
    return a;
  }

  listAccounts() { return this.db.accounts.map((a) => ({ ...a })); }

  getAccount(id: string) {
    const a = this.accountOrThrow(id);
    const adopted = this.db.pubs.filter((p) => !p.withdrawn && p.adoptions.some((x) => x.accountId === id))
      .map((p) => ({ id: p.id, name: this.pubData.get(p.id)!.invention.name, at: p.adoptions.find((x) => x.accountId === id)!.at }));
    return { ...a, adopted };
  }

  /** 発案者のページ：表示名で公開した発明だけで作る（匿名の公開は数えない・出さない）。 */
  creator(publicId: string) {
    const pubs = this.db.pubs.filter((p) => !p.withdrawn && !p.anonymous && publicCreatorId(p.creatorHash) === publicId);
    if (pubs.length === 0) throw new HttpError(404, "not_found", "発案者が見つかりません");
    const latest = [...pubs].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt))[0];
    return {
      publicId, displayName: latest.displayName,
      published: pubs.length, adopted: pubs.filter((p) => p.adoptions.length > 0).length,
      inventions: pubs.map((p) => ({ id: p.id, name: this.pubData.get(p.id)!.invention.name, status: this.status(p), publishedAt: p.publishedAt })),
    };
  }

  // ---------------- やりとり ----------------
  /** サイトで「発案者として見る」ときの名乗り（表示名で公開した発案者だけ選べる）。 */
  creatorIdentityFromPublic(publicId: string): Identity {
    const p = this.db.pubs.find((x) => !x.anonymous && publicCreatorId(x.creatorHash) === publicId);
    if (!p) throw new HttpError(404, "not_found", "発案者が見つかりません");
    return { kind: "creator", hash: p.creatorHash };
  }

  threads(who: Identity) {
    return this.db.threads.filter((t) => this.isMember(t, who)).map((t) => this.threadView(t));
  }

  thread(id: string, who: Identity) { const t = this.threadFor(id, who); return { ...this.threadView(t), messages: t.messages.map((m) => ({ ...m })) }; }

  postMessage(id: string, who: Identity, text: string) {
    const t = this.threadFor(id, who);
    if (t.messages.length >= LIMITS.maxMessagesPerThread) throw new HttpError(429, "too_many", "このやりとりの上限に達しました");
    const m: Message = { id: this.newId("m"), from: who.kind === "creator" ? "creator" : "adopter", text: text.trim(), at: this.now() };
    t.messages.push(m);
    this.save();
    return m;
  }

  // ---------------- 運営 ----------------
  verifyAccount(id: string, verified: boolean) {
    const a = this.accountOrThrow(id);
    a.verified = verified;
    this.save();
    return { id, verified };
  }

  /** 規約に反する公開を運営が取り下げる（発案者は公開し直せない）。 */
  takedown(id: string, reason: string) {
    const rec = this.pubOrThrow(id, true);
    rec.withdrawn = true; rec.withdrawnBy = "admin"; rec.withdrawReason = reason.trim(); rec.updatedAt = this.now();
    this.save();
    return { id, withdrawn: true };
  }

  adminList() {
    return this.db.pubs.map((p) => ({
      id: p.id, name: this.pubData.get(p.id)!.invention.name, status: p.withdrawn ? "withdrawn" : this.status(p),
      withdrawnBy: p.withdrawnBy, withdrawReason: p.withdrawReason, creator: this.creatorPublic(p),
      publishedAt: p.publishedAt, adoptions: p.adoptions.length, sample: p.sample,
    }));
  }

  // ---------------- 内部 ----------------
  private status(p: PubRecord): PubStatus {
    return p.adoptions.length > 0 ? "adopted" : p.considering.length > 0 ? "considering" : "open";
  }

  private pubOrThrow(id: string, includeWithdrawn = false) {
    const rec = this.db.pubs.find((p) => p.id === id);
    if (!rec) throw new HttpError(404, "not_found", "公開された発明が見つかりません");
    if (rec.withdrawn && !includeWithdrawn) throw new HttpError(410, "withdrawn", "この発明は公開を取り下げられました");
    return rec;
  }

  private accountOrThrow(id: string) {
    const a = this.db.accounts.find((x) => x.id === id);
    if (!a) throw new HttpError(404, "no_account", "登録が見つかりません");
    return a;
  }

  private creatorPublic(p: PubRecord) {
    return p.anonymous ? { name: "発案者", publicId: null } : { name: p.displayName, publicId: publicCreatorId(p.creatorHash) };
  }

  private accountPublic(id: string) {
    const a = this.db.accounts.find((x) => x.id === id);
    return a
      ? { id: a.id, name: a.name, affiliation: a.affiliation, role: a.role, field: a.field, verified: a.verified }
      : { id, name: "（登録が見つかりません）", affiliation: "", role: "researcher" as Role, field: "", verified: false };
  }

  private isMember(t: ThreadRecord, who: Identity) {
    return who.kind === "creator" ? t.creatorHash === who.hash : t.accountId === who.id;
  }

  private threadFor(id: string, who: Identity) {
    const t = this.db.threads.find((x) => x.id === id);
    // 相手でない人には、やりとりがあるかどうかも見せない
    if (!t || !this.isMember(t, who)) throw new HttpError(404, "not_found", "やりとりが見つかりません");
    return t;
  }

  private threadCounts(threadId: string) {
    const t = this.db.threads.find((x) => x.id === threadId);
    const last = t?.messages[t.messages.length - 1];
    return { messageCount: t?.messages.length ?? 0, lastFrom: last?.from ?? null, lastAt: last?.at ?? null };
  }

  private threadView(t: ThreadRecord) {
    const rec = this.db.pubs.find((p) => p.id === t.pubId);
    const inv = this.pubData.get(t.pubId)?.invention;
    return {
      id: t.id, pubId: t.pubId, pubName: inv?.name ?? "", createdAt: t.createdAt,
      adopter: this.accountPublic(t.accountId),
      creator: rec ? this.creatorPublic(rec) : { name: "発案者", publicId: null },
      reason: rec?.adoptions.find((a) => a.threadId === t.id)?.reason ?? "",
      ...this.threadCounts(t.id),
    };
  }

  /** テスト・見本用：公開を見本として印を付ける。 */
  markSample(id: string, sample = true) { const r = this.pubOrThrow(id, true); r.sample = sample; this.save(); }
  markSampleAccount(id: string, sample = true) { const a = this.accountOrThrow(id); a.sample = sample; this.save(); }

  /** テスト用：保存先を消す。 */
  static wipe(dir: string) { rmSync(dir, { recursive: true, force: true }); }
}
