// AI 評価セットを、起動中のプロキシ（/v1/design）に順に送り、応答をそのまま JSONL に保存する。
// 使い方（別ターミナルで npm start しておく）： node eval/run-eval.ts [--only r01,r02] [--concurrency 2]
// 集計・検証はアプリ側（Unity）の AiEvalReport が行う（DNA・実験計画の検証はアプリの検証器が唯一の基準）。
// 実行すると Claude API の料金がかかる（1件あたりの目安はプロキシのログと結果の usage.costUsd）。
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));
const BASE = process.env.ILAB_PROXY_URL ?? "http://127.0.0.1:8787";

interface EvalRequest { id: string; group: string; request: string; expect: { kind: "design" | "unsupported"; blocks: string[] } }

const args = process.argv.slice(2);
const opt = (name: string) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : undefined; };
const only = opt("--only")?.split(",").map((s) => s.trim()).filter(Boolean);
const concurrency = Math.max(1, Math.min(4, Number(opt("--concurrency") ?? 2)));

const set = JSON.parse(readFileSync(path.join(here, "requests.json"), "utf8")) as { requests: EvalRequest[] };
const items = set.requests.filter((r) => !only || only.includes(r.id));

const stamp = new Date().toISOString().replace(/[-:]/g, "").replace("T", "-").slice(0, 13);
const outDir = path.join(here, "results");
mkdirSync(outDir, { recursive: true });
const outPath = path.join(outDir, `design-${stamp}.jsonl`);

async function runOne(item: EvalRequest) {
  const started = Date.now();
  let httpStatus = 0, body = "", transportError = "";
  try {
    const res = await fetch(`${BASE}/v1/design`, {
      method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ request: item.request }),
      signal: AbortSignal.timeout(400_000),
    });
    httpStatus = res.status;
    body = await res.text();
  } catch (e) {
    transportError = e instanceof Error ? e.message : String(e);
  }
  const ms = Date.now() - started;
  let cost = 0, status = "";
  try { const o = JSON.parse(body); cost = o?.usage?.costUsd ?? 0; status = o?.status ?? ""; } catch { /* 集計はアプリ側 */ }
  console.log(`${item.id} ${httpStatus} ${status || transportError} ${(ms / 1000).toFixed(1)}s $${cost.toFixed(4)}`);
  return { id: item.id, group: item.group, request: item.request, expect: item.expect, httpStatus, ms, transportError, body, cost };
}

const results: Awaited<ReturnType<typeof runOne>>[] = [];
let next = 0;
async function worker() {
  while (next < items.length) {
    const item = items[next++];
    results.push(await runOne(item));
    writeFileSync(outPath, results.sort((a, b) => a.id.localeCompare(b.id)).map((r) => JSON.stringify(r)).join("\n") + "\n");
  }
}
await Promise.all(Array.from({ length: concurrency }, worker));
const total = results.reduce((s, r) => s + r.cost, 0);
console.log(`done ${results.length} requests, estimated cost $${total.toFixed(3)} -> ${outPath}`);
