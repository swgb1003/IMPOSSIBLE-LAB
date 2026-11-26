// 発明ライブラリの保存と操作（docs/PUBLISH_LIBRARY.md 3・7章）。PC の中だけで動かし、ネットには出さない。
// 保存先：<dir>/db.json（状態・数・登録・やりとり）と <dir>/pubs/<公開ID>.json・.glb（公開データと 3D）。
// 発案者の端末の鍵（creatorKey）はそのまま保存せず、ハッシュだけを持つ。依頼文などの本文はログに書かない。
import { createHash, randomBytes } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync, rmSync } from "node:fs";
import path from "node:path";
import { HttpError } from "../http-util.ts";
import type { Invention, Publication } from "./schema.ts";
import { hashPassword } from "./auth.ts";
import {
  accountView, Members,
  type CreatorLoginRecord, type MailRecord, type MailTokenRecord, type NoticeRecord, type OperatorRecord, type ReportRecord,
  type SessionRecord, type Who,
} from "./members.ts";

/** 一覧に出す実験の要約：同じ実験（DREAM を優先）の最初の版と最後の版の主な計測と合否。 */
export interface ResultSummary {
  title: string; mode: "dream" | "reality"; label: string; unit: string; lowerIsBetter: boolean;
  before: { revision: number; display: string; passed: boolean | null } | null;
  after: { revision: number; display: string; passed: boolean | null };
}

export function resultSummary(inv: Invention): ResultSummary | null {
  for (const mode of ["dream", "reality"] as const) {
    const runs = inv.experiments.filter((e) => e.mode === mode && e.metrics.some((m) => m.primary));
    if (runs.length === 0) continue;
    // いちばん多くの版で測った実験を選ぶ（改良の前後を比べられる）
    const byTitle = new Map<string, typeof runs>();
    for (const r of runs) byTitle.set(r.title, [...(byTitle.get(r.title) ?? []), r]);
    const group = [...byTitle.values()].sort((a, b) => new Set(b.map((x) => x.revision)).size - new Set(a.map((x) => x.revision)).size)[0];
    const sorted = [...group].sort((a, b) => a.revision - b.revision);
    const last = sorted[sorted.length - 1];
    const m = last.metrics.find((x) => x.primary)!;
    const first = sorted.find((r) => r.revision < last.revision && r.metrics.some((x) => x.key === m.key));
    const fm = first?.metrics.find((x) => x.key === m.key);
    return {
      title: last.title, mode, label: m.label, unit: m.unit, lowerIsBetter: m.lowerIsBetter,
      before: first && fm ? { revision: first.revision, display: fm.display, passed: first.passed } : null,
      after: { revision: last.revision, display: m.display, passed: last.passed },
    };
  }
  return null;
}

/**
 * だれでも見られる発明の中身：名前・何をするものか・分類と、実験の結果の要点（主な計測と合否、合格の目安）。
 * 依頼の原文・部品としくみ・実験の条件とほかの計測・改良の履歴・計算の前提と課題は、ログインした開発者だけに見せる（detail）。
 */
export function publicInvention(inv: Invention) {
  return {
    id: inv.id, revision: inv.revision, name: inv.name, summary: inv.summary, purpose: inv.purpose,
    tags: inv.tags, sceneTags: inv.sceneTags, mount: inv.mount,
    partCount: inv.parts.length, experimentCount: inv.experiments.length,
    experiments: inv.experiments.map((e) => ({
      revision: e.revision, title: e.title, mode: e.mode, passed: e.passed, criteria: e.criteria,
      metrics: e.metrics.filter((m) => m.primary),
    })),
  };
}

export const LIMITS = {
  maxPublications: 500,
  maxPerCreator: 100,
  maxAccounts: 200,
  maxThreads: 500,
  maxMessagesPerThread: 200,
  maxGlbBytes: 1024 * 1024,
  maxImageBytes: 300 * 1024,
  maxImages: 4,
};

export type PubStatus = "open" | "considering" | "adopted";
export type SortKey = "wants" | "new" | "adoptions" | "name";

interface Adoption { id: string; accountId: string; reason: string; at: string; threadId: string }
export interface ImageInput { kind: "stage" | "experiment" | "result"; caption: string; revision: number; jpg: Buffer }
interface ImageMeta { kind: ImageInput["kind"]; caption: string; revision: number }
interface Considering { accountId: string; at: string }
export interface PubRecord {
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
  /** 詳しい内容を見た開発者（開発者ごとに最初と最後の時刻。運営が確かめる）。 */
  views?: { accountId: string; first: string; last: string }[];
  /** 見本（見本と明記して出す）。 */
  sample: boolean;
  /** 写真（pubs/<公開ID>.img<番号>.jpg）。 */
  images?: ImageMeta[];
}
/** 開発者の登録。名前・所属・専門・紹介は、採用した発明の発案者と運営にだけ見せる。 */
export interface AccountRecord {
  id: string; name: string; affiliation: string; field: string; intro: string;
  verified: boolean; createdAt: string; sample: boolean;
  /** ログイン（小文字にしたメールアドレスと、元に戻せない形のパスワード）。 */
  email?: string; passwordHash?: string; emailVerified?: boolean;
}
interface Message { id: string; from: "adopter" | "creator"; text: string; at: string }
interface ThreadRecord {
  id: string; pubId: string; accountId: string; creatorHash: string; createdAt: string; messages: Message[];
  /** それぞれが最後に読んだ時刻。 */
  readAt?: { adopter?: string; creator?: string };
  /** 採用した人が退会したら終わる。 */
  closed?: boolean;
}
export interface Db {
  version: 1; pubs: PubRecord[]; accounts: AccountRecord[]; threads: ThreadRecord[];
  operators?: OperatorRecord[]; creatorLogins?: CreatorLoginRecord[]; sessions?: SessionRecord[]; mailTokens?: MailTokenRecord[];
  outbox?: MailRecord[]; notices?: NoticeRecord[]; reports?: ReportRecord[];
}

/** やりとりに加わる人。Quest は端末の鍵、サイトはログインした開発者か発案者。 */
export type Identity = Who;

export interface StoreOptions {
  dir: string;
  now?: () => string;
  newId?: (prefix: string) => string;
}

const hash = (s: string) => createHash("sha256").update(s, "utf8").digest("hex");
/** 端末の鍵から、公開してよい発案者の ID を作る（鍵は戻せない）。 */
export const publicCreatorId = (creatorHash: string) => "c-" + hash("public:" + creatorHash).slice(0, 12);
export const creatorHashOf = (creatorKey: string) => hash("creator:" + creatorKey);

/** JPEG の見出しと大きさを確かめる（中身までは見ない）。 */
export function checkJpeg(b: Buffer): string | null {
  if (b.length < 4 || b[0] !== 0xff || b[1] !== 0xd8 || b[2] !== 0xff) return "写真は JPEG で送ってください";
  if (b.length > LIMITS.maxImageBytes) return `写真が大きすぎます（1枚 ${LIMITS.maxImageBytes / 1024} KB まで）`;
  return null;
}

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
  /** サイトの利用者（ログイン・メール・知らせ・通報）。 */
  readonly members: Members;

  constructor(o: StoreOptions) {
    this.dir = o.dir;
    this.now = o.now ?? (() => new Date().toISOString());
    this.newId = o.newId ?? ((p) => `${p}-${randomBytes(5).toString("hex")}`);
    mkdirSync(path.join(this.dir, "pubs"), { recursive: true });
    const dbPath = path.join(this.dir, "db.json");
    if (existsSync(dbPath)) {
      const raw = JSON.parse(readFileSync(dbPath, "utf8")) as Db;
      if (raw?.version !== 1) throw new Error("library/db.json の形式が非対応です");
      this.db = { ...raw, version: 1, pubs: raw.pubs ?? [], accounts: raw.accounts ?? [], threads: raw.threads ?? [] };
      for (const p of this.db.pubs) {
        const f = this.pubPath(p.id, ".json");
        if (existsSync(f)) this.pubData.set(p.id, JSON.parse(readFileSync(f, "utf8")) as Publication);
      }
      // 公開データが無い記録（手で消したなど）は出さない
      this.db.pubs = this.db.pubs.filter((p) => this.pubData.has(p.id));
    }
    this.members = new Members({ db: () => this.db, save: () => this.save(), now: this.now, newId: this.newId });
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
  publish(creatorKey: string, publication: Publication, glb: Buffer, images: ImageInput[] = []): { id: string; created: boolean } {
    const err = checkGlb(glb);
    if (err) throw new HttpError(400, "bad_glb", err);
    if (images.length > LIMITS.maxImages) throw new HttpError(400, "bad_image", `写真は ${LIMITS.maxImages} 枚までです`);
    for (const im of images) { const e = checkJpeg(im.jpg); if (e) throw new HttpError(400, "bad_image", e); }
    const creatorHash = creatorHashOf(creatorKey);
    const inv = publication.invention;
    const partIds = new Set(inv.parts.map((p) => p.id));
    if (partIds.size !== inv.parts.length) throw new HttpError(400, "bad_publication", "部品の ID が重なっています");
    let rec = this.db.pubs.find((p) => p.creatorHash === creatorHash && p.inventionId === inv.id);
    const now = this.now();
    const created = !rec;
    if (!rec) {
      if (this.db.pubs.length >= LIMITS.maxPublications) throw new HttpError(507, "library_full", "ライブラリーがいっぱいです");
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
    // 写真は公開し直すたびに入れ替える
    for (let i = 0; i < LIMITS.maxImages; i++) rmSync(this.pubPath(rec.id, `.img${i}.jpg`), { force: true });
    images.forEach((im, i) => this.writeAtomic(this.pubPath(rec.id, `.img${i}.jpg`), im.jpg));
    rec.images = images.map((im) => ({ kind: im.kind, caption: im.caption, revision: im.revision }));
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
  mine(creatorKey: string) { return this.mineByHash(creatorHashOf(creatorKey)); }

  /** 発案者の公開（Quest と、サイトの自分のページ）。 */
  mineByHash(h: string) {
    return this.db.pubs.filter((p) => p.creatorHash === h).map((p) => ({
      id: p.id,
      inventionId: p.inventionId,
      revision: this.pubData.get(p.id)!.invention.revision,
      name: this.pubData.get(p.id)!.invention.name,
      status: p.withdrawn ? "withdrawn" : this.status(p),
      withdrawnBy: p.withdrawnBy,
      wants: { vr: p.wantsVr.length, site: p.wantsSite.length },
      tried: p.tried.length,
      adoptions: p.adoptions.map((a) => ({ id: a.id, at: a.at, reason: a.reason, adopter: this.partner(a.accountId), threadId: a.threadId, ...this.threadCounts(a.threadId) })),
      considering: p.considering.length, publishedAt: p.publishedAt, sample: p.sample,
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
        id: rec.id, name: inv.name, summary: inv.summary,
        tags: inv.tags, sceneTags: inv.sceneTags, mount: inv.mount, status: this.status(rec), revision: inv.revision,
        creator: this.creatorPublic(rec), creatorAdoptions: adoptionsByCreator.get(rec.creatorHash) ?? 0,
        wants: wants(rec), adoptions: rec.adoptions.length, considering: rec.considering.length,
        publishedAt: rec.publishedAt, updatedAt: rec.updatedAt, sample: rec.sample,
        result: resultSummary(inv),
        thumb: this.imagesOf(rec).find((m) => m.kind === "stage")?.url ?? null,
      })),
    };
  }

  /** だれでも見られる1件：名前・何をするものか・3D と写真・実験の結果の要点・ほしいと採用の数。採用した人の名前は出さない。 */
  get(id: string) {
    const rec = this.pubOrThrow(id);
    return {
      id: rec.id, status: this.status(rec), sample: rec.sample, detail: false,
      publishedAt: rec.publishedAt, updatedAt: rec.updatedAt, consentVersion: rec.consentVersion,
      creator: this.creatorPublic(rec),
      wants: rec.wantsVr.length + rec.wantsSite.length, adoptions: rec.adoptions.length, considering: rec.considering.length,
      images: this.imagesOf(rec),
      invention: publicInvention(this.pubData.get(rec.id)!.invention),
    };
  }

  /**
   * 詳しい内容つきの1件。読めるのは、ログインした開発者・公開した発案者・運営だけ。
   * ほかの発案者には、だれでも見られる1件を返す（detail: false）。
   * 開発者が読んだら、その記録を残す（運営が確かめる）。開発者には、自分の採用と検討中の状態も返す。
   */
  detail(id: string, who: Who) {
    const rec = this.pubOrThrow(id);
    if (who.kind === "creator" && who.hash !== rec.creatorHash) return { ...this.get(id), mine: null };
    const inv = this.pubData.get(rec.id)!.invention;
    const page = { ...this.get(id), detail: true, invention: { ...inv, partCount: inv.parts.length, experimentCount: inv.experiments.length } };
    if (who.kind !== "account") return { ...page, mine: null };
    this.accountOrThrow(who.id);
    const now = this.now();
    const views = (rec.views ??= []);
    const v = views.find((x) => x.accountId === who.id);
    if (v) v.last = now; else views.push({ accountId: who.id, first: now, last: now });
    this.save();
    const a = rec.adoptions.find((x) => x.accountId === who.id);
    return { ...page, mine: { adopted: !!a, threadId: a?.threadId ?? null, considering: rec.considering.some((c) => c.accountId === who.id) } };
  }

  model(id: string): Buffer {
    const rec = this.pubOrThrow(id);
    return readFileSync(this.pubPath(rec.id, ".glb"));
  }

  image(id: string, n: number): Buffer {
    const rec = this.pubOrThrow(id);
    if (!Number.isInteger(n) || n < 0 || n >= (rec.images?.length ?? 0)) throw new HttpError(404, "not_found", "写真が見つかりません");
    return readFileSync(this.pubPath(rec.id, `.img${n}.jpg`));
  }

  private imagesOf(rec: PubRecord) {
    return (rec.images ?? []).map((m, i) => ({ ...m, url: `/library/${rec.id}/images/${i}.jpg` }));
  }

  // ---------------- ほしい・試した ----------------
  /** サイトのほしい（ブラウザごとに1回）。 */
  wantSite(id: string, voter: string) {
    const rec = this.pubOrThrow(id);
    const h = hash("site:" + voter);
    const added = !rec.wantsSite.includes(h);
    if (added) { rec.wantsSite.push(h); this.save(); }
    return { added, wants: rec.wantsVr.length + rec.wantsSite.length };
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
    return { added, wants: rec.wantsVr.length + rec.wantsSite.length };
  }

  // ---------------- 検討中・採用（登録した開発者） ----------------
  consider(id: string, accountId: string) {
    const rec = this.pubOrThrow(id);
    this.accountOrThrow(accountId);
    if (!rec.considering.some((c) => c.accountId === accountId)) {
      rec.considering.push({ accountId, at: this.now() });
      // 検討中は、だれかを伝えない（名前が伝わるのは採用したときだけ）
      this.members.notify({ kind: "creator", hash: rec.creatorHash }, { kind: "considering", pubId: rec.id, pubName: this.nameOf(rec.id) });
      this.save();
    }
    return { status: this.status(rec) };
  }

  /** 採用する。採用した人と発案者の2人だけが読めるやりとりの場を作る（採用した人ごとに1つ）。 */
  adopt(id: string, accountId: string, reason: string) {
    const rec = this.pubOrThrow(id);
    const acc = this.accountOrThrow(accountId);
    if (rec.adoptions.some((a) => a.accountId === accountId)) throw new HttpError(409, "already_adopted", "すでに採用しています");
    if (this.db.threads.length >= LIMITS.maxThreads) throw new HttpError(507, "library_full", "やりとりの数の上限です");
    const now = this.now();
    const thread: ThreadRecord = { id: this.newId("t"), pubId: rec.id, accountId, creatorHash: rec.creatorHash, createdAt: now, messages: [] };
    const adoption: Adoption = { id: this.newId("a"), accountId, reason: reason.trim(), at: now, threadId: thread.id };
    rec.adoptions.push(adoption);
    this.db.threads.push(thread);
    this.members.notify({ kind: "creator", hash: rec.creatorHash }, { kind: "adopted", pubId: rec.id, pubName: this.nameOf(rec.id), threadId: thread.id, fromName: acc.name });
    this.save();
    return { status: this.status(rec), adoptionId: adoption.id, threadId: thread.id };
  }

  // ---------------- 開発者の登録 ----------------
  /** 登録を作る。メールアドレスとパスワードを付けるとログインできる（見本とテストでは付けないこともある）。 */
  createAccount(input: { name: string; affiliation: string; field: string; intro: string; email?: string; password?: string }) {
    if (this.db.accounts.length >= LIMITS.maxAccounts) throw new HttpError(507, "library_full", "登録の数の上限です");
    let email: string | undefined;
    if (input.email != null) {
      email = this.members.checkEmail(input.email);
      if (this.members.emailTaken(email)) throw new HttpError(409, "email_taken", "このメールアドレスはすでに登録されています");
    }
    if (input.password != null) this.members.checkNewPassword(input.password);
    const a: AccountRecord = {
      id: this.newId("u"), name: input.name.trim(), affiliation: input.affiliation.trim(),
      field: input.field.trim(), intro: input.intro.trim(), verified: false, createdAt: this.now(), sample: false,
      ...(email ? { email, emailVerified: false } : {}),
      ...(input.password != null ? { passwordHash: hashPassword(input.password) } : {}),
    };
    this.db.accounts.push(a);
    this.save();
    return accountView(a);
  }

  /** サイトの新規登録：登録してログインし、確認のメールを送る。 */
  signup(input: { name: string; affiliation: string; field: string; intro: string; email: string; password: string }) {
    const v = this.createAccount(input);
    this.members.sendVerification(this.accountOrThrow(v.id));
    return { kind: "account" as const, id: v.id };
  }

  /** 自分の登録を直す（名前・所属・専門・紹介）。 */
  updateProfile(id: string, input: { name: string; affiliation: string; field: string; intro: string }) {
    const a = this.accountOrThrow(id);
    a.name = input.name.trim(); a.affiliation = input.affiliation.trim(); a.field = input.field.trim(); a.intro = input.intro.trim();
    this.save();
    return this.members.me({ kind: "account", id });
  }

  /** 退会：登録とログインを消す。採用の記録は「退会した人」として残し、やりとりは終える。 */
  deleteAccount(id: string, password: string) {
    const who: Who = { kind: "account", id };
    this.members.confirmPassword(who, password);
    this.db.accounts = this.db.accounts.filter((a) => a.id !== id);
    for (const p of this.db.pubs) p.considering = p.considering.filter((c) => c.accountId !== id);
    for (const t of this.db.threads) if (t.accountId === id) t.closed = true;
    this.members.dropNoticesOf(who);
    this.members.endAllSessions(who);
    this.save();
    return { deleted: true };
  }

  /** 運営に見せる開発者の一覧（メールアドレスと確認の状態、採用した数、詳しい内容を見た発明の数）。ほかの人には一覧を見せない。 */
  adminAccounts() {
    const live = this.db.pubs.filter((p) => !p.withdrawn);
    return this.db.accounts.map((a) => ({
      ...accountView(a), email: a.email ?? "", emailVerified: a.emailVerified ?? false,
      adoptions: live.filter((p) => p.adoptions.some((x) => x.accountId === a.id)).length,
      viewed: live.filter((p) => (p.views ?? []).some((x) => x.accountId === a.id)).length,
    }));
  }

  /** 表示名で公開している発案者の一覧。匿名の公開は出さない。 */
  listCreators() {
    const byId = new Map<string, { publicId: string; name: string; published: number }>();
    for (const p of this.db.pubs.filter((x) => !x.withdrawn && !x.anonymous)) {
      const id = publicCreatorId(p.creatorHash);
      const c = byId.get(id) ?? { publicId: id, name: p.displayName, published: 0 };
      c.published++;
      byId.set(id, c);
    }
    return [...byId.values()];
  }

  /** 自分のページ：採用した発明と検討中の発明。 */
  accountActivity(id: string) {
    this.accountOrThrow(id);
    const live = this.db.pubs.filter((p) => !p.withdrawn);
    return {
      adopted: live.filter((p) => p.adoptions.some((x) => x.accountId === id))
        .map((p) => { const a = p.adoptions.find((x) => x.accountId === id)!; return { id: p.id, name: this.nameOf(p.id), at: a.at, threadId: a.threadId }; }),
      considering: live.filter((p) => p.considering.some((x) => x.accountId === id) && !p.adoptions.some((x) => x.accountId === id))
        .map((p) => ({ id: p.id, name: this.nameOf(p.id), at: p.considering.find((x) => x.accountId === id)!.at })),
    };
  }

  /** 発案者のページ：表示名で公開した発明だけで作る（匿名の公開は数えない・出さない）。 */
  creator(publicId: string) {
    const pubs = this.db.pubs.filter((p) => !p.withdrawn && !p.anonymous && publicCreatorId(p.creatorHash) === publicId);
    if (pubs.length === 0) throw new HttpError(404, "not_found", "発案者が見つかりません");
    const latest = [...pubs].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt))[0];
    return {
      publicId, displayName: latest.displayName,
      published: pubs.length, adopted: pubs.filter((p) => p.adoptions.length > 0).length,
      inventions: pubs.map((p) => {
        const inv = this.pubData.get(p.id)!.invention;
        return { id: p.id, name: inv.name, summary: inv.summary, status: this.status(p), publishedAt: p.publishedAt,
          wants: p.wantsVr.length + p.wantsSite.length, adoptions: p.adoptions.length };
      }),
    };
  }

  // ---------------- やりとり ----------------
  threads(who: Identity) {
    return this.db.threads.filter((t) => this.isMember(t, who)).map((t) => this.threadView(t, who));
  }

  /** 1件を読む（読んだ時刻を残し、このやりとりの知らせを読んだことにする）。 */
  thread(id: string, who: Identity) {
    const t = this.threadFor(id, who);
    const view = { ...this.threadView(t, who), messages: t.messages.map((m) => ({ ...m })) };
    t.readAt = { ...(t.readAt ?? {}), [who.kind === "creator" ? "creator" : "adopter"]: this.now() };
    this.members.readNotices(who, t.id);
    this.save();
    return view;
  }

  postMessage(id: string, who: Identity, text: string) {
    const t = this.threadFor(id, who);
    if (t.closed) throw new HttpError(410, "closed", "採用した人が退会したため、このやりとりは終わりました");
    if (t.messages.length >= LIMITS.maxMessagesPerThread) throw new HttpError(429, "too_many", "このやりとりの上限に達しました");
    const from = who.kind === "creator" ? "creator" : "adopter";
    const m: Message = { id: this.newId("m"), from, text: text.trim(), at: this.now() };
    t.messages.push(m);
    t.readAt = { ...(t.readAt ?? {}), [from]: m.at };
    const rec = this.db.pubs.find((p) => p.id === t.pubId);
    const fromName = from === "creator" ? (rec ? this.creatorPublic(rec).name : "発案者") : this.partner(t.accountId).name;
    const to: Who = from === "creator" ? { kind: "account", id: t.accountId } : { kind: "creator", hash: t.creatorHash };
    this.members.notify(to, { kind: "message", pubId: t.pubId, pubName: this.nameOf(t.pubId), threadId: t.id, fromName });
    this.save();
    return m;
  }

  // ---------------- 運営 ----------------
  verifyAccount(id: string, verified: boolean) {
    const a = this.accountOrThrow(id);
    if (verified && !a.verified) this.members.notify({ kind: "account", id }, { kind: "verified" });
    a.verified = verified;
    this.save();
    return { id, verified };
  }

  /** 規約に反する公開を運営が取り下げる（発案者は公開し直せない）。この発明の通報は片づける。 */
  takedown(id: string, reason: string) {
    const rec = this.pubOrThrow(id, true);
    rec.withdrawn = true; rec.withdrawnBy = "admin"; rec.withdrawReason = reason.trim(); rec.updatedAt = this.now();
    for (const r of this.db.reports ?? []) if (r.pubId === id && r.status === "open") { r.status = "takedown"; r.resolvedAt = this.now(); r.note = reason.trim(); }
    this.members.notify({ kind: "creator", hash: rec.creatorHash }, { kind: "takedown", pubId: rec.id, pubName: this.nameOf(rec.id), note: reason.trim() });
    this.save();
    return { id, withdrawn: true };
  }

  /** 通報する（公開中の発明だけ）。 */
  report(id: string, reason: Parameters<Members["report"]>[1], detail: string) {
    const rec = this.pubOrThrow(id);
    return this.members.report(rec.id, reason, detail);
  }

  /** 通報の一覧（新しい順。発明の名前と今の状態つき）。 */
  reports() {
    return [...(this.db.reports ?? [])].reverse().map((r) => {
      const rec = this.db.pubs.find((p) => p.id === r.pubId);
      return { ...r, pubName: this.nameOf(r.pubId), pubStatus: rec ? (rec.withdrawn ? "withdrawn" : this.status(rec)) : "withdrawn" };
    });
  }

  /** 通報を片づける：問題なし、か取り下げ。 */
  resolveReport(reportId: string, action: "dismiss" | "takedown", note: string) {
    const r = this.members.reportOrThrow(reportId);
    if (r.status !== "open") throw new HttpError(409, "resolved", "この通報はすでに片づいています");
    if (action === "takedown") this.takedown(r.pubId, note || r.reason);
    else { r.status = "dismissed"; r.resolvedAt = this.now(); r.note = note.trim(); this.save(); }
    return { id: reportId, status: this.members.reportOrThrow(reportId).status };
  }

  adminList() {
    const open = (this.db.reports ?? []).filter((r) => r.status === "open");
    return this.db.pubs.map((p) => ({
      id: p.id, name: this.nameOf(p.id), status: p.withdrawn ? "withdrawn" : this.status(p),
      withdrawnBy: p.withdrawnBy, withdrawReason: p.withdrawReason, creator: this.creatorPublic(p),
      publishedAt: p.publishedAt, adoptions: p.adoptions.length, sample: p.sample, reports: open.filter((r) => r.pubId === p.id).length,
      viewers: (p.views ?? []).map((v) => ({ ...this.partner(v.accountId), first: v.first, last: v.last })),
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

  /** 採用で結びついた相手（発案者）と運営に見せる開発者。退会した人は名前を出さない。 */
  private partner(id: string) {
    const a = this.db.accounts.find((x) => x.id === id);
    if (!a) return { id, name: "退会した人", affiliation: "", field: "", intro: "", verified: false, gone: true };
    const v = accountView(a);
    return { id: v.id, name: v.name, affiliation: v.affiliation, field: v.field, intro: v.intro, verified: v.verified, gone: false };
  }

  private nameOf(pubId: string) { return this.pubData.get(pubId)?.invention.name ?? ""; }

  private isMember(t: ThreadRecord, who: Identity) {
    return who.kind === "creator" ? t.creatorHash === who.hash : who.kind === "account" ? t.accountId === who.id : false;
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

  private threadView(t: ThreadRecord, who?: Identity) {
    const rec = this.db.pubs.find((p) => p.id === t.pubId);
    const side = who?.kind === "creator" ? "creator" : "adopter";
    const readAt = t.readAt?.[side] ?? "";
    return {
      id: t.id, pubId: t.pubId, pubName: this.nameOf(t.pubId), createdAt: t.createdAt,
      adopter: this.partner(t.accountId),
      creator: rec ? this.creatorPublic(rec) : { name: "発案者", publicId: null },
      reason: rec?.adoptions.find((a) => a.threadId === t.id)?.reason ?? "",
      closed: t.closed ?? false,
      unread: who ? t.messages.filter((m) => m.from !== side && m.at > readAt).length : 0,
      ...this.threadCounts(t.id),
    };
  }

  /**
   * 入口でしくみを紹介するための、見本の発明の詳しい内容（運営が作った見本だけ。実在の発案者の公開は出さない）。
   */
  showcase(id: string) {
    const rec = this.pubOrThrow(id);
    if (!rec.sample) throw new HttpError(403, "not_sample", "入口に出せるのは見本だけです");
    const inv = this.pubData.get(rec.id)!.invention;
    return { ...this.get(id), detail: true, invention: { ...inv, partCount: inv.parts.length, experimentCount: inv.experiments.length } };
  }

  /** テスト・見本用：公開を見本として印を付ける。 */
  markSample(id: string, sample = true) { const r = this.pubOrThrow(id, true); r.sample = sample; this.save(); }
  markSampleAccount(id: string, sample = true) { const a = this.accountOrThrow(id); a.sample = sample; this.save(); }
  /** 見本用：見本の人にログインを付ける（パスワードはハッシュで受け取る）。 */
  setSampleLogin(id: string, email: string, passwordHash: string) {
    const a = this.accountOrThrow(id);
    if (!a.sample) throw new HttpError(400, "not_sample", "見本ではありません");
    a.email = email.toLowerCase(); a.passwordHash = passwordHash; a.emailVerified = true;
    this.save();
  }

  /** テスト用：保存先を消す。 */
  static wipe(dir: string) { rmSync(dir, { recursive: true, force: true }); }
}
