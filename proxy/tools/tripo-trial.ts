// 生成外装の試用1回（仕様 29章・ゲート G-B1、docs/DECISIONS.md D-18〜D-24）。
// アプリ・プロキシ本体には組み込まない。発明DNA から外装用の文章を組み立て、Tripo に1回だけ依頼し、
// GLB と結果（時間・消費クレジット・大きさ）を Logs/tripo_trial/ に保存する。
//
// 使い方（proxy フォルダで）：
//   node --env-file=.env tools/tripo-trial.ts <DNA の JSON>            … 依頼せず、送る内容と料金の目安だけ表示
//   node --env-file=.env tools/tripo-trial.ts <DNA の JSON> --yes      … 実際に依頼する（約30クレジット）
//   … --image <PNG> を付けると、文章ではなく画像（基本形状の試作品を書き出したもの）から生成する（約40クレジット）
//   … --restyle <PNG> は 3D を作らず、基本形状の画像を製品デザイン風の画像に描き直すだけ（image-to-image）
//   … --image-task <task_id> は、描き直した画像（image-to-image の task_id）から 3D を生成する（約40クレジット）
// API キーは環境変数 TRIPO_API_KEY からだけ読み、画面にもファイルにも書かない。

import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { join, resolve } from "node:path";

const API = "https://openapi.tripo3d.ai/v3";
const TIMEOUT_MS = 10 * 60 * 1000; // 試用1回目は5分11秒かかったため、仕様 29.3 の初期値5分から延ばす
const MAX_PROMPT = 1024;

type Part = { id: string; type: string; shape?: string; material?: string; position: number[]; scale: number[] };
type Dna = { name: string; purpose?: string; mount?: string; parts: Part[] };

const PART_EN: Record<string, string> = {
  body: "main body", wheel: "wheel", container: "storage box", sensor: "small sensor", arm: "arm", canopy: "wide canopy",
  thruster: "thruster", stabilizer: "stabilizer unit", connector: "connector", frame: "frame", panel: "panel", light: "lamp",
  heater: "heating pad", cooler: "cooling unit", motor: "joint motor", gripper: "gripper", cushion: "cushion", battery: "battery pack",
  solar: "solar panel", speaker: "speaker", strap: "strap",
};

/** 描き直し（image-to-image）の文章。形・比率・配置を変えないことを強く指定する。 */
export function buildRestylePrompt(dna: Dna): string {
  return (
    "Redesign this rough grey-box block-out into a polished industrial product design concept render. " +
    "Keep EXACTLY the same silhouette, proportions, part layout and camera angle as the input; do not add, remove, move or resize parts. " +
    `The object: ${buildPrompt(dna).replace(/^A single industrial product design of a gadget, no person, no ground, no text\. /, "").split(" Keep the same")[0]} ` +
    "Refine every part into a believable manufactured product: matte white plastic housings with soft fillets and subtle seam lines, " +
    "the wide canopy as a thin translucent light-blue shell with a slim rim, small status lights. " +
    "Single object only, plain light grey studio background, soft even lighting, no person, no text, no logo."
  ).slice(0, MAX_PROMPT);
}

/** 外装用の文章（英語）。利用者の自由な文章は使わず、DNA の部品の配置から組み立てる（仕様 29.2）。 */
export function buildPrompt(dna: Dna): string {
  const base = dna.parts[0];
  const parts = dna.parts.map((p, i) => {
    const [x, y, z] = p.scale.map((v) => Math.round(v * 100));
    // 先頭の部品からの位置（上下・前後・左右）
    const where: string[] = [];
    if (i > 0) {
      const d = [0, 1, 2].map((k) => p.position[k] - base.position[k]);
      if (d[1] > 0.03) where.push("above"); else if (d[1] < -0.03) where.push("below");
      if (d[2] > 0.03) where.push("in front of"); else if (d[2] < -0.03) where.push("behind");
      if (d[0] > 0.03) where.push("right of"); else if (d[0] < -0.03) where.push("left of");
    }
    const rel = i === 0 ? "" : where.length ? `, ${where.join(" and ")} the ${PART_EN[base.type] ?? "first part"}` : ", centered on it";
    return `${PART_EN[p.type] ?? "part"} (${x}x${y}x${z} cm${rel})`;
  });
  const text =
    "A single industrial product design of a gadget, no person, no ground, no text. " +
    `It consists of: ${parts.join(", ")}. ` +
    "Keep the same overall proportions and layout. Clean modern look, matte white plastic with light grey accents and a translucent canopy where present. " +
    "Smooth simple surfaces suitable for a real-time VR app.";
  return text.slice(0, MAX_PROMPT);
}

async function api(path: string, key: string, init?: RequestInit): Promise<any> {
  const res = await fetch(API + path, { ...init, headers: { Authorization: `Bearer ${key}`, "Content-Type": "application/json", ...(init?.headers ?? {}) } });
  const body = await res.json().catch(() => ({}));
  if (!res.ok || body.code !== 0) throw new Error(`HTTP ${res.status} code=${body.code ?? "?"} ${body.message ?? body.error_message ?? ""}`.trim());
  return body.data;
}

async function download(url: string, file: string): Promise<number> {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`download HTTP ${res.status}`);
  const buf = Buffer.from(await res.arrayBuffer());
  writeFileSync(file, buf);
  return buf.length;
}

async function main() {
  const args = process.argv.slice(2);
  const dnaPath = args[0];
  const flag = args.includes("--yes") ? "--yes" : "";
  const imageArg = args.indexOf("--image");
  const imagePath = imageArg >= 0 ? resolve(args[imageArg + 1] ?? "") : null;
  const restyleArg = args.indexOf("--restyle");
  const restylePath = restyleArg >= 0 ? resolve(args[restyleArg + 1] ?? "") : null;
  const taskArg = args.indexOf("--image-task");
  const imageTask = taskArg >= 0 ? (args[taskArg + 1] ?? "") : null;
  const mode = restylePath ? "restyle" : imageTask ? "image-task" : imagePath ? "image" : "text";
  const uploadPath = restylePath ?? imagePath;
  if (!dnaPath) { console.error("DNA の JSON を指定してください"); process.exit(2); }
  const dna: Dna = JSON.parse(readFileSync(resolve(dnaPath), "utf8"));
  const request: Record<string, unknown> = mode === "restyle" ? {
    model: "seedream_v5",
    prompt: buildRestylePrompt(dna),
    size: "2K",
    output_format: "png",
  } : {
    ...(mode === "text" ? { prompt: buildPrompt(dna) } : {}),
    ...(mode === "image-task" ? { input: imageTask } : {}),
    model: "v3.1-20260211",
    texture: true,
    pbr: false,               // Quest 向けに標準のテクスチャだけ
    texture_quality: "standard",
    smart_low_poly: true,     // 三角形を face_limit 以下に
    face_limit: 10000,        // 仕様の上限 2万に余裕を持たせた初期値
  };
  console.log("発明：", dna.name);
  console.log("送る内容：", JSON.stringify(request, null, 2));
  if (uploadPath) console.log("元の画像：", uploadPath);
  console.log(mode === "restyle" ? "料金の目安：画像の描き直し（seedream_v5）約5.5クレジット（料金表の読み取りが不確か。最大でも20程度）"
    : mode === "text" ? "料金の目安：標準テクスチャ 20 ＋ 低ポリゴン化 10 ＝ 30クレジット（約$0.30）"
    : "料金の目安：画像から・標準テクスチャ 30 ＋ 低ポリゴン化 10 ＝ 40クレジット（約$0.40）");
  if (flag !== "--yes") { console.log("（--yes がないので依頼していません）"); return; }

  const key = process.env.TRIPO_API_KEY;
  if (!key) { console.error("TRIPO_API_KEY がありません（proxy/.env）"); process.exit(2); }

  const stamp = new Date().toISOString().replace(/[:.]/g, "-");
  const outDir = resolve("..", "Logs", "tripo_trial", stamp);
  mkdirSync(outDir, { recursive: true });
  const record: any = { dna: dna.name, source: mode, image: uploadPath, imageTask, request, startedAt: new Date().toISOString(), stages: [] as any[] };
  const save = () => writeFileSync(join(outDir, "result.json"), JSON.stringify(record, null, 2));
  const t0 = Date.now();
  const stage = (s: string) => { record.stages.push({ stage: s, sec: +((Date.now() - t0) / 1000).toFixed(1) }); console.log(`[${((Date.now() - t0) / 1000).toFixed(0)}s] ${s}`); };

  try {
    if (uploadPath) {
      // 画像をアップロード → file_token を input に渡す
      const form = new FormData();
      form.append("file", new Blob([readFileSync(uploadPath)], { type: "image/png" }), "input.png");
      const up = await fetch(API + "/files", { method: "POST", headers: { Authorization: `Bearer ${key}` }, body: form });
      const ub = await up.json().catch(() => ({}));
      if (!up.ok || ub.code !== 0) throw new Error(`upload HTTP ${up.status} code=${ub.code ?? "?"} ${ub.message ?? ""}`.trim());
      request.input = ub.data.file_token;
      stage("画像のアップロード");
    }
    const endpoint = mode === "restyle" ? "/generation/image-to-image" : mode === "text" ? "/generation/text-to-model" : "/generation/image-to-model";
    const created = await api(endpoint, key, { method: "POST", body: JSON.stringify(request) });
    record.taskId = created.task_id;
    stage("受付");
    let wait = 2000, task: any, last = "";
    while (true) {
      if (Date.now() - t0 > TIMEOUT_MS) throw new Error("タイムアウト（10分）");
      await new Promise((r) => setTimeout(r, wait));
      task = await api(`/tasks/${created.task_id}`, key);
      if (task.status !== last) { last = task.status; stage(`状態 ${task.status}`); }
      if (["success", "failed", "cancelled", "banned", "expired"].includes(task.status)) break;
      wait = Math.min(wait * 1.5, 10000);
    }
    record.status = task.status;
    record.creditsConsumed = task.credits_consumed;
    record.error = task.error_message ?? task.error_code;
    if (task.status !== "success") throw new Error(`生成に失敗：${task.status} ${record.error ?? ""}`);
    if (mode === "restyle") {
      record.imageBytes = await download(task.output.generated_image_url, join(outDir, "restyled.png"));
      console.log("描き直した画像の task_id（3D 生成に使う）：", created.task_id);
    } else record.glbBytes = await download(task.output.model_url, join(outDir, "model.glb"));
    if (task.output.rendered_image_url) record.previewBytes = await download(task.output.rendered_image_url, join(outDir, "preview" + (task.output.rendered_image_url.includes(".webp") ? ".webp" : ".png")));
    stage("受け取り完了");
  } catch (e) {
    record.failure = String(e instanceof Error ? e.message : e);
    console.error("失敗：", record.failure);
    process.exitCode = 1;
  } finally {
    record.totalSec = +((Date.now() - t0) / 1000).toFixed(1);
    save();
    console.log("記録：", join(outDir, "result.json"));
  }
}

await main();
