// サイトの利用者：開発者の登録とログイン、運営、発案者のログイン（Quest の番号・見本はパスワード）、
// ログインの記録、メール（このデモでは外へ送らず、送ったメールの一覧に入れる）、知らせ、通報。
// パスワードとログインの記録の札はハッシュだけを保存する。メールアドレスは本人と運営にだけ見せる。
import { HttpError } from "../http-util.ts";
import { checkPassword, FailureGate, hashPassword, newPairCode, newToken, SESSION_DAYS, tokenHash } from "./auth.ts";
import type { AccountRecord, Db } from "./store.ts";

/** ログインしている人。 */
export type Who = { kind: "account"; id: string } | { kind: "creator"; hash: string } | { kind: "operator"; id: string };
export const whoKey = (w: Who) => (w.kind === "creator" ? `creator:${w.hash}` : `${w.kind}:${w.id}`);

export interface OperatorRecord { id: string; name: string; email: string; passwordHash: string; createdAt: string; sample: boolean }
export interface CreatorLoginRecord { creatorHash: string; email: string; passwordHash: string; createdAt: string }
export interface SessionRecord { tokenHash: string; who: Who; createdAt: string; expiresAt: string }
export interface MailTokenRecord { tokenHash: string; kind: "verify" | "reset"; who: Who; email: string; expiresAt: string; used: boolean }
export interface MailRecord { id: string; to: string; subject: string; body: string; link: string; at: string }
export type NoticeKind = "adopted" | "considering" | "message" | "verified" | "takedown";
export interface NoticeRecord {
  id: string; to: string; kind: NoticeKind; at: string; read: boolean;
  pubId?: string; pubName?: string; threadId?: string; fromName?: string; note?: string;
}
export interface ReportRecord {
  id: string; pubId: string; reason: string; detail: string; at: string;
  status: "open" | "dismissed" | "takedown"; resolvedAt?: string; note?: string;
}

export const REPORT_REASONS = {
  rights: "他人の権利を侵している",
  personal: "個人を特定できる情報が載っている",
  danger: "危険な使い方を勧めている",
  other: "そのほか",
} as const;

const EMAIL = /^[^\s@]{1,64}@[^\s@]+\.[^\s@]{2,}$/;
export const normEmail = (s: string) => s.trim().toLowerCase();
const LIMIT = { outbox: 200, notices: 2000, reports: 500, sessions: 2000 };

export interface MemberCtx { db: () => Db; save: () => void; now: () => string; newId: (p: string) => string }

export class Members {
  private loginGate = new FailureGate(5, 15 * 60_000);
  private pairGate = new FailureGate(10, 10 * 60_000);
  private pairs = new Map<string, { creatorHash: string; expiresAt: number }>();

  private c: MemberCtx;
  constructor(c: MemberCtx) { this.c = c; }
  private get db() { return this.c.db(); }

  // ---------------- 登録とログイン ----------------
  checkEmail(raw: string) {
    const email = normEmail(raw);
    if (email.length > 254 || !EMAIL.test(email)) throw new HttpError(400, "bad_email", "メールアドレスの形が正しくありません");
    return email;
  }
  checkNewPassword(pw: string) {
    if ([...pw].length < 8) throw new HttpError(400, "weak_password", "パスワードは 8 文字以上にしてください");
    if ([...pw].length > 128) throw new HttpError(400, "bad_password", "パスワードが長すぎます（128 文字まで）");
  }
  emailTaken(email: string, except?: Who) {
    const e = normEmail(email);
    const ex = except ? whoKey(except) : "";
    return this.db.accounts.some((a) => a.email === e && `account:${a.id}` !== ex)
      || (this.db.operators ?? []).some((o) => o.email === e && `operator:${o.id}` !== ex)
      || (this.db.creatorLogins ?? []).some((l) => l.email === e && `creator:${l.creatorHash}` !== ex);
  }

  /** メールアドレスとパスワードで入る（開発者・運営・見本の発案者）。どれが違うかは言わない。 */
  login(rawEmail: string, password: string): Who {
    const email = normEmail(rawEmail);
    if (this.loginGate.blocked(email)) throw new HttpError(429, "login_locked", "ログインの失敗が続いたため、15 分ほど待ってからもう一度試してください");
    const a = this.db.accounts.find((x) => x.email === email);
    const o = (this.db.operators ?? []).find((x) => x.email === email);
    const l = (this.db.creatorLogins ?? []).find((x) => x.email === email);
    const stored = a?.passwordHash ?? o?.passwordHash ?? l?.passwordHash;
    if (!checkPassword(password, stored)) {
      this.loginGate.fail(email);
      throw new HttpError(401, "bad_login", "メールアドレスかパスワードが違います");
    }
    this.loginGate.clear(email);
    return a ? { kind: "account", id: a.id } : o ? { kind: "operator", id: o.id } : { kind: "creator", hash: l!.creatorHash };
  }

  startSession(who: Who): string {
    const now = this.c.now();
    const token = newToken();
    const live = (this.db.sessions ?? []).filter((s) => s.expiresAt > now);
    this.db.sessions = [...live.slice(-(LIMIT.sessions - 1)), {
      tokenHash: tokenHash(token), who, createdAt: now,
      expiresAt: new Date(Date.parse(now) + SESSION_DAYS * 86400_000).toISOString(),
    }];
    this.c.save();
    return token;
  }

  /** ログインの記録から本人を決める（期限切れ・消えた登録は null）。 */
  whoOf(token: string | null): Who | null {
    if (!token) return null;
    const h = tokenHash(token);
    const s = (this.db.sessions ?? []).find((x) => x.tokenHash === h);
    if (!s || s.expiresAt <= this.c.now() || !this.exists(s.who)) return null;
    return s.who;
  }

  endSession(token: string | null) {
    if (!token) return;
    const h = tokenHash(token);
    const before = (this.db.sessions ?? []).length;
    this.db.sessions = (this.db.sessions ?? []).filter((s) => s.tokenHash !== h);
    if (this.db.sessions.length !== before) this.c.save();
  }

  /** この人のログインをすべて終える（パスワードの変更・すべての端末からログアウト・退会）。keep の記録だけ残す。 */
  endAllSessions(who: Who, keepToken?: string | null) {
    const k = whoKey(who), keep = keepToken ? tokenHash(keepToken) : "";
    this.db.sessions = (this.db.sessions ?? []).filter((s) => whoKey(s.who) !== k || s.tokenHash === keep);
    this.c.save();
  }

  exists(who: Who) {
    if (who.kind === "account") return this.db.accounts.some((a) => a.id === who.id);
    if (who.kind === "operator") return (this.db.operators ?? []).some((o) => o.id === who.id);
    return this.db.pubs.some((p) => p.creatorHash === who.hash) || (this.db.creatorLogins ?? []).some((l) => l.creatorHash === who.hash);
  }

  account(id: string) {
    const a = this.db.accounts.find((x) => x.id === id);
    if (!a) throw new HttpError(404, "no_account", "登録が見つかりません");
    return a;
  }

  /** 本人に見せる自分の情報（メールアドレスを含む）。 */
  me(who: Who) {
    if (who.kind === "account") {
      const a = this.account(who.id);
      return {
        kind: "account" as const, id: a.id, name: a.name, affiliation: a.affiliation, field: a.field, intro: a.intro,
        email: a.email ?? "", emailVerified: a.emailVerified ?? false,
        verified: a.verified, createdAt: a.createdAt, sample: a.sample,
      };
    }
    if (who.kind === "operator") {
      const o = (this.db.operators ?? []).find((x) => x.id === who.id);
      if (!o) throw new HttpError(401, "no_session", "ログインしてください");
      return { kind: "operator" as const, id: o.id, name: o.name, email: o.email, sample: o.sample };
    }
    const named = this.db.pubs.filter((p) => p.creatorHash === who.hash && !p.anonymous).sort((a, b) => b.updatedAt.localeCompare(a.updatedAt))[0];
    const login = (this.db.creatorLogins ?? []).find((l) => l.creatorHash === who.hash);
    return { kind: "creator" as const, name: named?.displayName ?? "発案者", named: Boolean(named), email: login?.email ?? "" };
  }

  // ---------------- 確認のメールとパスワードの再設定 ----------------
  private issue(kind: MailTokenRecord["kind"], who: Who, email: string, minutes: number) {
    const token = newToken();
    const now = this.c.now();
    this.db.mailTokens = [...(this.db.mailTokens ?? []).filter((t) => t.expiresAt > now && !(t.kind === kind && whoKey(t.who) === whoKey(who))), {
      tokenHash: tokenHash(token), kind, who, email, used: false,
      expiresAt: new Date(Date.parse(now) + minutes * 60_000).toISOString(),
    }];
    return token;
  }

  private take(kind: MailTokenRecord["kind"], token: string) {
    const h = tokenHash(String(token));
    const t = (this.db.mailTokens ?? []).find((x) => x.tokenHash === h && x.kind === kind);
    if (!t || t.used || t.expiresAt <= this.c.now() || !this.exists(t.who)) {
      throw new HttpError(400, "bad_token", kind === "verify" ? "このリンクは使えません。期限が切れたか、すでに使われています" : "このリンクは使えません。もう一度、再設定のメールを送ってください");
    }
    t.used = true;
    return t;
  }

  /** メールアドレスの確認のメールを送る（24 時間有効）。 */
  sendVerification(a: AccountRecord) {
    if (!a.email) return;
    const token = this.issue("verify", { kind: "account", id: a.id }, a.email, 24 * 60);
    this.mail(a.email, "メールアドレスの確認",
      `${a.name} 様\n\nIMPOSSIBLE LIBRARY への登録を受け付けました。\n次のリンクを開くと、メールアドレスの確認が終わります。リンクは 24 時間有効です。`,
      `/verify#token=${token}`,
      "心当たりがない場合は、このメールを破棄してください。");
    this.c.save();
  }

  verifyEmail(token: string) {
    const t = this.take("verify", token);
    if (t.who.kind === "account") {
      const a = this.account(t.who.id);
      if (a.email === t.email) a.emailVerified = true;
    }
    this.c.save();
    return { verified: true };
  }

  /** 再設定のメールを送る（30 分有効）。登録があるかどうかは答えない。 */
  forgot(rawEmail: string) {
    const email = normEmail(rawEmail);
    const a = this.db.accounts.find((x) => x.email === email);
    const o = (this.db.operators ?? []).find((x) => x.email === email);
    const l = (this.db.creatorLogins ?? []).find((x) => x.email === email);
    const who: Who | null = a ? { kind: "account", id: a.id } : o ? { kind: "operator", id: o.id } : l ? { kind: "creator", hash: l.creatorHash } : null;
    if (who) {
      const token = this.issue("reset", who, email, 30);
      this.mail(email, "パスワードの再設定",
        "IMPOSSIBLE LIBRARY のパスワードの再設定を受け付けました。\n次のリンクを開いて、新しいパスワードを決めてください。リンクは 30 分有効です。",
        `/reset#token=${token}`,
        "心当たりがない場合は、このメールを破棄してください。パスワードは変わりません。");
      this.c.save();
    }
    return { sent: true };
  }

  reset(token: string, password: string) {
    this.checkNewPassword(password);
    const t = this.take("reset", token);
    this.setPassword(t.who, password);
    this.endAllSessions(t.who);
    return { reset: true };
  }

  private setPassword(who: Who, password: string) {
    const h = hashPassword(password);
    if (who.kind === "account") this.account(who.id).passwordHash = h;
    else if (who.kind === "operator") { const o = (this.db.operators ?? []).find((x) => x.id === who.id); if (o) o.passwordHash = h; }
    else { const l = (this.db.creatorLogins ?? []).find((x) => x.creatorHash === who.hash); if (l) l.passwordHash = h; }
    this.c.save();
  }

  private passwordOf(who: Who) {
    if (who.kind === "account") return this.account(who.id).passwordHash;
    if (who.kind === "operator") return (this.db.operators ?? []).find((x) => x.id === who.id)?.passwordHash;
    return (this.db.creatorLogins ?? []).find((x) => x.creatorHash === who.hash)?.passwordHash;
  }

  confirmPassword(who: Who, password: string) {
    if (!checkPassword(password, this.passwordOf(who))) throw new HttpError(403, "bad_password", "パスワードが違います");
  }

  /** パスワードを変える。ほかの端末のログインは終える（この端末は続ける）。 */
  changePassword(who: Who, current: string, next: string, keepToken: string | null) {
    if (!this.passwordOf(who)) throw new HttpError(409, "no_password", "Quest の番号で入ったときは、パスワードはありません");
    this.confirmPassword(who, current);
    this.checkNewPassword(next);
    this.setPassword(who, next);
    this.endAllSessions(who, keepToken);
    return { changed: true };
  }

  // ---------------- 運営と見本の発案者 ----------------
  createOperator(input: { name: string; email: string; password: string; sample?: boolean }) {
    const email = this.checkEmail(input.email);
    this.checkNewPassword(input.password);
    if (this.emailTaken(email)) throw new HttpError(409, "email_taken", "このメールアドレスはすでに登録されています");
    const o: OperatorRecord = { id: this.c.newId("o"), name: input.name.trim(), email, passwordHash: hashPassword(input.password), createdAt: this.c.now(), sample: input.sample ?? false };
    this.db.operators = [...(this.db.operators ?? []), o];
    this.c.save();
    return { id: o.id, name: o.name, email };
  }

  /** 発案者にメールとパスワードのログインを付ける（見本の発案者用。本物の発案者は Quest の番号で入る）。 */
  setCreatorLogin(creatorHash: string, rawEmail: string, password: string) {
    const email = this.checkEmail(rawEmail);
    this.checkNewPassword(password);
    if (this.emailTaken(email, { kind: "creator", hash: creatorHash })) throw new HttpError(409, "email_taken", "このメールアドレスはすでに登録されています");
    const list = (this.db.creatorLogins ?? []).filter((l) => l.creatorHash !== creatorHash);
    this.db.creatorLogins = [...list, { creatorHash, email, passwordHash: hashPassword(password), createdAt: this.c.now() }];
    this.c.save();
  }

  // ---------------- Quest の番号で入る ----------------
  /** Quest が出す6桁の番号を作る（10 分有効。同じ端末の前の番号は使えなくする）。 */
  pairStart(creatorHash: string) {
    const now = Date.now();
    for (const [k, v] of this.pairs) if (v.expiresAt <= now || v.creatorHash === creatorHash) this.pairs.delete(k);
    let code = newPairCode();
    while (this.pairs.has(tokenHash(code))) code = newPairCode();
    const expiresAt = now + 10 * 60_000;
    this.pairs.set(tokenHash(code), { creatorHash, expiresAt });
    return { code, expiresAt: new Date(expiresAt).toISOString() };
  }

  pairRedeem(code: string): Who {
    if (this.pairGate.blocked("pair")) throw new HttpError(429, "pair_locked", "番号の入力の失敗が続いたため、しばらく待ってからもう一度試してください");
    const k = tokenHash(String(code).replace(/\s/g, ""));
    const p = this.pairs.get(k);
    if (!p || p.expiresAt <= Date.now()) {
      this.pairGate.fail("pair");
      throw new HttpError(401, "bad_code", "番号が違うか、期限が切れています。Quest で新しい番号を出してください");
    }
    this.pairs.delete(k);
    return { kind: "creator", hash: p.creatorHash };
  }

  // ---------------- メール（送ったメールの一覧に入れるだけ） ----------------
  mail(to: string, subject: string, body: string, link: string, after = "") {
    const m: MailRecord = { id: this.c.newId("mail"), to, subject, body: after ? `${body}\n\n${link}\n\n${after}` : `${body}\n\n${link}`, link, at: this.c.now() };
    this.db.outbox = [...(this.db.outbox ?? []), m].slice(-LIMIT.outbox);
  }

  outbox() { return [...(this.db.outbox ?? [])].reverse(); }

  // ---------------- 知らせ ----------------
  notify(to: Who, n: Omit<NoticeRecord, "id" | "to" | "at" | "read">) {
    this.db.notices = [...(this.db.notices ?? []), { ...n, id: this.c.newId("n"), to: whoKey(to), at: this.c.now(), read: false }].slice(-LIMIT.notices);
  }

  notices(who: Who) {
    const k = whoKey(who);
    return (this.db.notices ?? []).filter((n) => n.to === k).reverse().slice(0, 100).map(({ to: _to, ...n }) => n);
  }

  unread(who: Who) {
    const k = whoKey(who);
    return (this.db.notices ?? []).filter((n) => n.to === k && !n.read).length;
  }

  readNotices(who: Who, threadId?: string) {
    const k = whoKey(who);
    let changed = false;
    for (const n of this.db.notices ?? []) {
      if (n.to === k && !n.read && (!threadId || n.threadId === threadId)) { n.read = true; changed = true; }
    }
    if (changed) this.c.save();
    return { unread: this.unread(who) };
  }

  dropNoticesOf(who: Who) {
    const k = whoKey(who);
    this.db.notices = (this.db.notices ?? []).filter((n) => n.to !== k);
  }

  // ---------------- 通報 ----------------
  report(pubId: string, reason: keyof typeof REPORT_REASONS, detail: string) {
    const open = (this.db.reports ?? []).filter((r) => r.status === "open");
    if (open.length >= LIMIT.reports) throw new HttpError(507, "too_many", "通報の数の上限です");
    const r: ReportRecord = { id: this.c.newId("r"), pubId, reason: REPORT_REASONS[reason], detail: detail.trim(), at: this.c.now(), status: "open" };
    this.db.reports = [...(this.db.reports ?? []), r];
    this.c.save();
    return { id: r.id };
  }

  reportOrThrow(id: string) {
    const r = (this.db.reports ?? []).find((x) => x.id === id);
    if (!r) throw new HttpError(404, "not_found", "通報が見つかりません");
    return r;
  }
}

/** 採用で結びついた発案者と運営に見せる開発者（メールアドレスは出さない）。ほかの人には見せない。 */
export function accountView(a: AccountRecord) {
  return {
    id: a.id, name: a.name, verified: a.verified, sample: a.sample, createdAt: a.createdAt,
    affiliation: a.affiliation, field: a.field, intro: a.intro,
  };
}
