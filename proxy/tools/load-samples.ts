// 発明ライブラリに見本を入れる。Unity の通し検証が書き出した Logs/verify/library/<発明ID>/ を読み、
// 公開データを PC 側の形（schema.ts）で確かめてからライブラリに公開し、見本の印を付ける。
// あわせて、サイトがライブラリにつながらないとき（共有ページなど）に読む見本のファイルを site/sample/ に作る。
//
//   npm run samples                          （既定：../Logs/verify/library → library/、site/sample/）
//   node tools/load-samples.ts --from <dir> --to <libraryDir> --static <siteSampleDir>
//   npm run samples -- --logins-only         （見本の人のログインだけを入れる。公開し直さない）
// 見本の人（開発者・発案者・運営）は、メールアドレス〇〇@sample.invalid と共通のパスワード SAMPLE_PASSWORD で入れる。
import { existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { creatorHashOf, LibraryStore, type ImageInput } from "../src/library/store.ts";
import { hashPassword } from "../src/library/auth.ts";
import { Publication, ImageKind, describeIssues } from "../src/library/schema.ts";

const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (name: string, def: string) => { const i = process.argv.indexOf(name); return i > 0 ? process.argv[i + 1] : def; };
const FROM = path.resolve(arg("--from", path.join(here, "..", "..", "Logs", "verify", "library")));
const TO = path.resolve(arg("--to", process.env.ILAB_LIBRARY_DIR ?? path.join(here, "..", "library")));
const STATIC = path.resolve(arg("--static", path.join(here, "..", "site", "sample")));
/** 見本の発案者の端末の鍵（見本どうしは同じ発案者）。サイトで発案者として名乗ってやりとりを試せるよう、表示名で公開する。 */
const SAMPLE_KEY = "ilab-sample-creator-0001";
export const SAMPLE_CREATOR_NAME = "見本の発案者";
/** 見本の人の共通のパスワード（PC の中だけのデモ用。proxy/README.md に書いてある）。 */
export const SAMPLE_PASSWORD = "mihon-2026";
export const SAMPLE_CREATOR_EMAIL = "creator@sample.invalid";
export const SAMPLE_OPERATOR = { name: "見本の運営", email: "unei@sample.invalid" };

/** 見本の開発者（実在しない人と所属）。名前で見分け、何度入れても増えない。 */
export const SAMPLE_ACCOUNTS = [
  { name: "高瀬 真央", affiliation: "北辰工科大学 人間支援ロボティクス研究室", field: "体に付けて動作を助ける機器",
    intro: "体に付けて動きを助ける機器を研究しています。日常の困りごとから研究の題材を探しています。", verified: true, email: "takase@sample.invalid" },
  { name: "森下 圭", affiliation: "白砂器械株式会社 製品開発部", field: "電池で動く小型の機器の量産設計",
    intro: "家庭向けの小さな電動機器を製品にしています。重さと電池で動く時間を気にして読みます。", verified: false, email: "morishita@sample.invalid" },
  { name: "野々村 透", affiliation: "個人で開発", field: "雨具と屋外の道具",
    intro: "雨や日差しをしのぐ道具を一人で作って売っています。", verified: false, email: "nonomura@sample.invalid" },
] as const;

export function loadSampleAccounts(store: LibraryStore) {
  const have = store.adminAccounts();
  return SAMPLE_ACCOUNTS.map((s) => {
    let a = have.find((x) => x.sample && x.name === s.name);
    if (!a) {
      const created = store.createAccount({ name: s.name, affiliation: s.affiliation, field: s.field, intro: s.intro, email: s.email, password: SAMPLE_PASSWORD });
      store.markSampleAccount(created.id);
      a = { ...created, sample: true, email: s.email, emailVerified: false, adoptions: 0, viewed: 0 };
    } else {
      store.setSampleLogin(a.id, s.email, hashPassword(SAMPLE_PASSWORD)); // 前に入れた見本にもログインを付ける
    }
    if (a.verified !== s.verified) store.verifyAccount(a.id, s.verified);
    return a.id;
  });
}

/** 見本の発案者（Quest が無いので、メールとパスワードで入る）と見本の運営のログイン。 */
export function loadSampleLogins(store: LibraryStore) {
  store.members.setCreatorLogin(creatorHashOf(SAMPLE_KEY), SAMPLE_CREATOR_EMAIL, SAMPLE_PASSWORD);
  if (!store.members.emailTaken(SAMPLE_OPERATOR.email)) store.members.createOperator({ ...SAMPLE_OPERATOR, password: SAMPLE_PASSWORD, sample: true });
}

export function loadSamples(from: string, store: LibraryStore) {
  if (!existsSync(from)) throw new Error(`見本の書き出しが見つかりません：${from}（Unity の通し検証を先に実行してください）`);
  const ids: string[] = [];
  for (const dir of readdirSync(from, { withFileTypes: true }).filter((d) => d.isDirectory()).map((d) => path.join(from, d.name)).sort()) {
    const parsed = Publication.safeParse(JSON.parse(readFileSync(path.join(dir, "publication.json"), "utf8")));
    if (!parsed.success) throw new Error(`${path.basename(dir)}：公開データが PC 側の形に合いません：${describeIssues(parsed.error)}`);
    const glb = readFileSync(path.join(dir, "model.glb"));
    const metaFile = path.join(dir, "images.json");
    const meta = existsSync(metaFile) ? JSON.parse(readFileSync(metaFile, "utf8")) as { file: string; kind: string; caption: string; revision: number }[] : [];
    const images: ImageInput[] = meta.map((m) => ({ kind: ImageKind.parse(m.kind), caption: m.caption, revision: m.revision, jpg: readFileSync(path.join(dir, m.file)) }));
    const pub = { ...parsed.data, creator: { displayName: SAMPLE_CREATOR_NAME, anonymous: false } };
    const { id } = store.publish(SAMPLE_KEY, pub, glb, images);
    store.markSample(id);
    ids.push(id);
    console.log(`見本 ${id}：${parsed.data.invention.name}（Rev.${parsed.data.invention.revision}、写真 ${images.length} 枚）`);
  }
  return ids;
}

/** 入口でしくみを紹介する見本（部品が多く、形が分かりやすいもの）。 */
const SHOWCASE = /アゲルくん/;

/**
 * ライブラリにつながらないときにサイトが読む見本のファイル（一覧・1件・3D・写真）を書き出す。URL はサイトからの相対に直す。
 * 1件のページは、だれでも見られる形だけ。入口の紹介に使う見本1件だけは、詳しい内容つきで showcase.json に書く。
 */
export function writeStatic(store: LibraryStore, out: string) {
  rmSync(out, { recursive: true, force: true });
  for (const d of ["pages", "models", "images"]) mkdirSync(path.join(out, d), { recursive: true });
  const list = store.list({ sort: "new" });
  const rel = (url: string) => url.replace(/^\/library\/([a-z0-9-]+)\/images\/(\d)\.jpg$/, "/site/sample/images/$1-$2.jpg");
  for (const item of list.items) {
    const page = store.get(item.id);
    writeFileSync(path.join(out, "models", `${item.id}.glb`), store.model(item.id));
    page.images.forEach((im, n) => writeFileSync(path.join(out, "images", `${item.id}-${n}.jpg`), store.image(item.id, n)));
    writeFileSync(path.join(out, "pages", `${item.id}.json`), JSON.stringify({ ...page, images: page.images.map((im) => ({ ...im, url: rel(im.url) })) }, null, 1));
  }
  const samples = list.items.filter((x) => x.sample);
  const feat = samples.find((x) => SHOWCASE.test(x.name)) ?? samples[0];
  if (feat) {
    const sc = store.showcase(feat.id);
    writeFileSync(path.join(out, "showcase.json"), JSON.stringify({ ...sc, images: sc.images.map((im) => ({ ...im, url: rel(im.url) })) }, null, 1));
  }
  writeFileSync(path.join(out, "list.json"), JSON.stringify({
    ...list,
    items: list.items.map((x) => ({ ...x, page: `/inventions/${x.id}`, thumb: x.thumb ? rel(x.thumb) : null })),
  }, null, 1));
  return list.items.length;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  // 見本は同じ発案者・同じ発明として公開し直すので、何度入れても増えない。ほかの公開はそのまま残る
  const store = new LibraryStore({ dir: TO });
  const loginsOnly = process.argv.includes("--logins-only");
  const ids = loginsOnly ? [] : loadSamples(FROM, store);
  const accounts = loadSampleAccounts(store);
  loadSampleLogins(store);
  const n = writeStatic(store, STATIC);
  console.log(`ライブラリ（${TO}）に見本 ${ids.length} 件と見本の開発者 ${accounts.length} 人・見本の発案者と運営のログインを入れ、サイトの見本 ${n} 件を ${STATIC} に書き出しました`);
  console.log(`見本の人のパスワード：${SAMPLE_PASSWORD}（メールアドレスは proxy/README.md）`);
}
