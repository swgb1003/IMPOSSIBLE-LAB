// 発明ライブラリ（IMPOSSIBLE LIBRARY）が受け取るデータの形（docs/PUBLISH_LIBRARY.md 3・7章）。
// Quest が公開ページの中身（表示する文・計算済みの値）を作って送り、ここでは形と大きさだけを確かめる。
// 知らない欄は捨てる。文字はそのまま保存し、サイトで表示するときに必ずエスケープする。
import { z } from "zod";

// 検証の失敗の文を日本語にする（Quest の画面と、サイトにそのまま出す。プロセス全体の設定）
z.config(z.locales.ja());

const text = (max: number) => z.string().max(max);
const line = (max: number) => z.string().max(max).refine((s) => !/[\r\n]/.test(s), "改行は使えません");
const num = (min: number, max: number) => z.number().finite().min(min).max(max);
const optNum = (min: number, max: number) => num(min, max).nullable();
const int = (min: number, max: number) => z.number().int().min(min).max(max);
/** 部品・発明の ID（アプリの DnaJson.IsSafeId と同じ文字）。 */
export const safeId = z.string().regex(/^[A-Za-z0-9_-]{1,64}$/, "ID に使えない文字があります");
const labeled = z.object({ id: line(32), label: line(40) });

/** 要望の確かめ方（アプリの FunctionNeed.Status と同じ。demonstrated は動作プログラムの動きで見せる）。 */
export const FunctionStatus = z.enum(["simulated", "simplified", "demonstrated", "unverifiable"]);

const need = z.object({
  need: line(120),
  status: FunctionStatus,
  blocks: z.array(labeled).max(8),
  how: text(300),
  note: text(300),
});

const part = z.object({
  id: safeId,
  type: line(32),
  label: line(40),
  role: text(300),
  shape: line(40),
  material: line(40),
  sizeM: z.tuple([num(0, 10), num(0, 10), num(0, 10)]),
  massKg: optNum(0, 1000),
});

const experiment = z.object({
  revision: int(1, 100000),
  title: line(80),
  mode: z.enum(["dream", "reality"]),
  conditions: z.array(z.object({ label: line(40), value: line(40) })).max(12),
  metrics: z.array(z.object({
    key: line(40),
    label: line(40),
    value: num(-1e9, 1e9),
    unit: line(12),
    display: line(24),
    lowerIsBetter: z.boolean(),
    primary: z.boolean(),
  })).max(16),
  passed: z.boolean().nullable(),
  criteria: text(200),
  seed: int(0, 1e9),
  notes: z.array(text(160)).max(10),
  at: line(32),
});

const revision = z.object({
  revision: int(1, 100000),
  parent: int(0, 100000),
  /** unknown：依頼文を保存する前の版（何を変えたかは版どうしの差分から作った）。 */
  source: z.enum(["ai", "offline", "manual", "unknown"]),
  request: text(400),
  summary: text(120),
  changes: z.array(z.object({ label: line(80), before: line(80), after: line(80) })).max(12),
  resultChanged: z.boolean(),
});

export const Invention = z.object({
  id: safeId,
  revision: int(1, 100000),
  name: line(60).refine((s) => s.trim().length > 0, "名前が空です"),
  summary: text(400),
  purpose: text(200),
  tags: z.array(labeled).max(6),
  /** 使う場面のタグ（9章の8で足す。今は空でよい）。 */
  sceneTags: z.array(labeled).max(6).default([]),
  mount: labeled,
  /** 最初の依頼文の原文。前に保存した発明では空（ページには「記録なし」）。 */
  request: text(400),
  designSource: z.enum(["ai", "offline", ""]),
  needs: z.array(need).max(12),
  parts: z.array(part).min(1).max(24),
  experiments: z.array(experiment).max(40),
  revisions: z.array(revision).max(40),
  research: z.object({
    unverifiable: z.array(z.object({ need: line(120), reason: text(300) })).max(12),
    assumptions: z.array(text(200)).max(20),
    simplified: z.array(text(200)).max(20),
  }),
  developer: z.object({
    totalMassKg: optNum(0, 10000),
    tipAngleDeg: optNum(0, 90),
    powerW: optNum(0, 1e7),
    runtimeMin: optNum(0, 1e6),
    challenges: z.array(text(200)).max(12),
  }),
});
export type Invention = z.infer<typeof Invention>;

/**
 * 同意の文面の版。サイトの利用規約と VR の同意の画面で同じ版を使う。
 * 2026-11-26：詳しい内容（依頼の原文・部品としくみ・実験の条件・改良の履歴）は、ログインした開発者だけに見せる。
 * 前の版（2026-11-25、だれでも読めるページに載せる）で同意した公開も受け付ける（見せる範囲は狭くなっただけのため）。
 */
export const CONSENT_VERSION = "2026-11-26";
export const CONSENT_VERSIONS = ["2026-11-25", CONSENT_VERSION] as const;

export const Publication = z.object({
  format: z.literal(1),
  creator: z.object({
    /** 表示名。匿名なら空でよい。 */
    displayName: line(40),
    anonymous: z.boolean(),
  }),
  consent: z.object({
    version: z.enum(CONSENT_VERSIONS),
    agreed: z.literal(true),
    agreedAt: line(32),
  }),
  invention: Invention,
});
export type Publication = z.infer<typeof Publication>;

/** 公開ページに載せる写真の種類：発明台に置いた試作品・実験の様子・研究所の結果の画面。 */
export const ImageKind = z.enum(["stage", "experiment", "result"]);

export const PublishBody = z.object({
  publication: Publication,
  /** 試作品の 3D（GLB）を base64 で。 */
  glbBase64: z.string().max(1_400_000),
  /** 写真（JPEG を base64 で、1枚 300KB まで、4枚まで）。無くてもよい。 */
  images: z.array(z.object({
    kind: ImageKind,
    caption: line(80),
    revision: int(1, 100000),
    jpgBase64: z.string().max(410_000),
  })).max(4).default([]),
});

/** 開発者の登録の内容。名前・所属・専門・紹介は、採用した発明の発案者と運営にだけ見せる。 */
export const AccountInput = z.object({
  name: line(40).refine((s) => s.trim().length > 0, "名前が空です"),
  affiliation: line(60),
  field: line(60),
  intro: text(200),
});

/** サイトの新規登録：紹介、メールアドレスとパスワード、利用規約と個人情報の扱いへの同意。 */
export const SignupBody = AccountInput.extend({
  email: z.string().max(254),
  password: z.string().max(128),
  agreed: z.boolean(),
});

export const LoginBody = z.object({ email: z.string().max(254), password: z.string().max(128) });
export const ProfileBody = AccountInput;
export const PasswordBody = z.object({ current: z.string().max(128), next: z.string().max(128) });
export const ConfirmBody = z.object({ password: z.string().max(128) });
export const EmailBody = z.object({ email: z.string().max(254) });
const mailToken = z.string().regex(/^[A-Za-z0-9_-]{20,100}$/, "リンクの形が正しくありません");
export const TokenBody = z.object({ token: mailToken });
export const ResetBody = z.object({ token: mailToken, password: z.string().max(128) });
export const PairBody = z.object({ code: z.string().regex(/^\d{6}$/, "6 桁の数字を入れてください") });

export const ReasonBody = z.object({
  reason: text(200).refine((s) => s.trim().length > 0, "理由を書いてください"),
});

export const ReportBody = z.object({ reason: z.enum(["rights", "personal", "danger", "other"]), detail: text(400) });
export const ResolveBody = z.object({ action: z.enum(["dismiss", "takedown"]), note: text(200) });

export const VoteBody = z.object({
  /** site：サイトのブラウザごとの ID。vr のときは使わない（端末の発案者キーで数える）。 */
  voter: z.string().regex(/^[A-Za-z0-9_-]{8,64}$/).optional(),
});

export const MessageBody = z.object({ text: text(500).refine((s) => s.trim().length > 0, "本文が空です") });

export const VerifyBody = z.object({ verified: z.boolean() });

export const TakedownBody = z.object({ reason: text(200) });

/** zod の検証の失敗を、画面に出せる短い文にする。 */
export function describeIssues(e: z.ZodError): string {
  return e.issues.slice(0, 3).map((i) => `${i.path.join(".") || "本文"}：${i.message}`).join(" / ");
}
