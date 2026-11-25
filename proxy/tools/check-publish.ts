// Quest が送る公開の本文（POST /library）を、PC 側の形と中身の確かめで通るか調べる。
// Unity の通し検証が、保存の画面から公開したときの本文を Logs/verify/library-publish-body.json に残す。
//
//   npm run check-publish                     （既定：../Logs/verify/library-publish-body.json）
//   node tools/check-publish.ts <本文の JSON>
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { LibraryStore } from "../src/library/store.ts";
import { PublishBody, describeIssues } from "../src/library/schema.ts";

const here = path.dirname(fileURLToPath(import.meta.url));
const file = path.resolve(process.argv[2] ?? path.join(here, "..", "..", "Logs", "verify", "library-publish-body.json"));

export function checkPublish(raw: unknown) {
  const parsed = PublishBody.safeParse(raw);
  if (!parsed.success) throw new Error(`本文が PC 側の形に合いません：${describeIssues(parsed.error)}`);
  const b = parsed.data;
  const dir = mkdtempSync(path.join(tmpdir(), "ilab-check-"));
  try {
    const store = new LibraryStore({ dir });
    const images = b.images.map((im) => ({ kind: im.kind, caption: im.caption, revision: im.revision, jpg: Buffer.from(im.jpgBase64, "base64") }));
    const { id } = store.publish("ilab-check-creator-0001", b.publication, Buffer.from(b.glbBase64, "base64"), images);
    const page = store.get(id);
    return { id, name: page.invention.name, revision: page.invention.revision, creator: page.creator.name, images: page.images.length, experiments: page.invention.experiments.length };
  } finally { rmSync(dir, { recursive: true, force: true }); }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const r = checkPublish(JSON.parse(readFileSync(file, "utf8")));
  console.log(`通りました：${r.name}（Rev.${r.revision}、発案者 ${r.creator}、実験 ${r.experiments} 件、写真 ${r.images} 枚）`);
}
