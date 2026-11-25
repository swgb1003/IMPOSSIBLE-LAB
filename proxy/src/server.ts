// IMPOSSIBLE LAB AI設計プロキシ。APIキーはこのサーバーの環境変数だけに置き、アプリ（Quest）には渡さない。
// 起動: npm start（proxy/.env の ANTHROPIC_API_KEY を読む）
import { createServer, type IncomingMessage, type ServerResponse } from "node:http";
import { spawn, execFile, type ChildProcess } from "node:child_process";
import { existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import Anthropic from "@anthropic-ai/sdk";
import { betaZodOutputFormat } from "@anthropic-ai/sdk/helpers/beta/zod";
import { loadCatalog } from "./catalog.ts";
import { buildSchemas, designPoints, toDna, toPlans, toProposal } from "./schemas.ts";
import type { DesignOutput, PlanOutput, ReviseOutput } from "./schemas.ts";
import { designSystemPrompt, planSystemPrompt, reviseSystemPrompt } from "./prompts.ts";
import { checkRequestText, pickExperiment, RateLimiter, readJsonBody, readWavBody, HttpError } from "./http-util.ts";
import { estimateCostUsd, type Usage } from "./cost.ts";
import { LibraryStore } from "./library/store.ts";
import { createLibraryRoutes } from "./library/routes.ts";

const here = path.dirname(fileURLToPath(import.meta.url));
const HOST = process.env.ILAB_HOST ?? "127.0.0.1";
const PORT = Number(process.env.ILAB_PORT ?? 8787);
const MODEL = process.env.ILAB_MODEL ?? "claude-opus-5-5";
const EFFORT = (process.env.ILAB_EFFORT ?? "medium") as "low" | "medium" | "high";
const TOKEN = process.env.ILAB_PROXY_TOKEN ?? ""; // 設定時のみ x-ilab-token を要求（LAN公開時用）
const CATALOG_PATH = process.env.ILAB_CATALOG ?? path.join(here, "..", "catalog.json");

const catalog = loadCatalog(CATALOG_PATH);
const schemas = buildSchemas(catalog);
const DESIGN_SYSTEM = designSystemPrompt(catalog);
const PLAN_SYSTEM = planSystemPrompt(catalog);
const REVISE_SYSTEM = reviseSystemPrompt(catalog);
const METRIC_KEYS = catalog.experiments.metrics.map((m) => m.key);
const SCENARIO_IDS = catalog.experiments.scenarios.map((s) => s.id);
// 音声入力の文字起こし（stt/stt_server.py、PC 上の Whisper）。音声は外部へ送らない
const STT_PORT = Number(process.env.ILAB_STT_PORT ?? 8788);
const STT_URL = `http://127.0.0.1:${STT_PORT}/transcribe`;
let stt: ChildProcess | null = null;
function startStt() {
  if (process.env.ILAB_STT === "off") return;
  const py = path.join(here, "..", "stt", ".venv", process.platform === "win32" ? "Scripts/python.exe" : "bin/python");
  if (!existsSync(py)) { console.log("  音声入力：stt/.venv がないため使えません（proxy/README.md の「音声入力」を参照）"); return; }
  stt = spawn(py, [path.join(here, "..", "stt", "stt_server.py")], { stdio: ["ignore", "inherit", "inherit"], env: { ...process.env, ILAB_STT_PORT: String(STT_PORT), PYTHONIOENCODING: "utf-8" } });
  stt.on("exit", (code) => { console.log(`[stt] stopped (${code})`); stt = null; });
}
// USB でつないだ Quest から 127.0.0.1:PORT でプロキシに届くよう、adb reverse を15秒ごとに張り直す（抜き差し・再起動で消えるため）
const ADB_CANDIDATES = [process.env.ADB, "C:/Program Files/Unity/Hub/Editor/6000.0.61f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe", "adb"].filter(Boolean) as string[];
let questLinked: boolean | null = null;
function keepAdbReverse() {
  if (process.env.ILAB_ADB_REVERSE === "off") return;
  const adb = ADB_CANDIDATES.find((p) => p === "adb" || existsSync(p));
  if (!adb) { console.log("  Quest の中継：adb が見つかりません（環境変数 ADB で指定できます）"); return; }
  const tick = () => execFile(adb, ["reverse", `tcp:${PORT}`, `tcp:${PORT}`], { timeout: 8000 }, (err) => {
    const ok = !err;
    if (ok !== questLinked) console.log(ok ? "[adb] Quest とつながりました（USB 中継 OK）" : "[adb] Quest が見つかりません（USB でつなぐと自動で中継します）");
    questLinked = ok;
  });
  tick();
  setInterval(tick, 15_000).unref();
}

for (const sig of ["SIGINT", "SIGTERM"] as const) process.on(sig, () => { stt?.kill(); process.exit(0); });
process.on("exit", () => stt?.kill());

async function handleTranscribe(req: IncomingMessage) {
  const wav = await readWavBody(req, 2_000_000);
  let res: Response;
  try {
    res = await fetch(STT_URL, { method: "POST", headers: { "content-type": "audio/wav" }, body: new Uint8Array(wav), signal: AbortSignal.timeout(60_000) });
  } catch {
    throw new HttpError(503, "stt_unavailable", "音声入力（文字起こし）が起動していません");
  }
  const o = (await res.json().catch(() => ({}))) as { text?: string; error?: string; ms?: number };
  if (!res.ok) throw new HttpError(res.status === 413 ? 413 : 400, "bad_audio", o.error || "音声を文字にできませんでした");
  const text = (o.text ?? "").trim();
  return { status: "ok", text: [...text].slice(0, catalog.limits.maxRequestLength).join(""), ms: o.ms ?? 0 };
}

const limiter = new RateLimiter(Number(process.env.ILAB_RATE_PER_10MIN ?? 40), 10 * 60 * 1000);

// 発明ライブラリ（IMPOSSIBLE LIBRARY）：公開データは proxy/library/ に保存する（git に入れない。PC の外へは出さない）
const LIBRARY_DIR = process.env.ILAB_LIBRARY_DIR ?? path.join(here, "..", "library");
const library = createLibraryRoutes(new LibraryStore({ dir: LIBRARY_DIR }), { token: TOKEN });

const hasKey = Boolean(process.env.ANTHROPIC_API_KEY || process.env.ANTHROPIC_AUTH_TOKEN);
// 1回あたり120秒、SDK既定の再試行2回（429/5xx/接続エラー）
const client = hasKey ? new Anthropic({ timeout: 120_000, maxRetries: 2 }) : null;

// 拒否（refusal）時は推奨モデルへサーバー側で自動切替
const FALLBACK = { betas: ["server-side-fallback-2026-07-01"], fallbacks: "default" as const };

async function callClaude<T>(system: string, user: string, format: ReturnType<typeof betaZodOutputFormat>) {
  if (!client) throw new HttpError(503, "no_api_key", "プロキシに APIキーが設定されていません");
  const started = Date.now();
  const res = await client.beta.messages.parse({
    model: MODEL,
    max_tokens: 16000,
    ...FALLBACK,
    // システムプロンプト（カタログ込み）は起動中は固定なのでキャッシュする
    system: [{ type: "text", text: system, cache_control: { type: "ephemeral" } }],
    output_config: { effort: EFFORT, format },
    messages: [{ role: "user", content: user }],
  });
  const ms = Date.now() - started;
  const u = res.usage;
  const usage: Usage = {
    input: u.input_tokens, output: u.output_tokens,
    cacheRead: u.cache_read_input_tokens ?? 0, cacheWrite: u.cache_creation_input_tokens ?? 0,
  };
  const costUsd = estimateCostUsd(usage);
  console.log(`[claude] model=${res.model} stop=${res.stop_reason} in=${usage.input} cacheR=${usage.cacheRead} cacheW=${usage.cacheWrite} out=${usage.output} $${costUsd.toFixed(4)} ${ms}ms`);
  if (res.stop_reason === "refusal") throw new HttpError(422, "refused", "AIがこの指示への回答を控えました。別の言い方で試してください");
  if (res.stop_reason === "max_tokens") throw new HttpError(502, "truncated", "AIの出力が途中で切れました");
  if (res.parsed_output == null) throw new HttpError(502, "unparsable", "AIの出力を読み取れませんでした");
  return { output: res.parsed_output as T, model: res.model, ms, usage: { ...usage, ms, costUsd } };
}

function readDna(b: Record<string, unknown>) {
  if (typeof b.dna !== "object" || b.dna === null || Array.isArray(b.dna)) throw new HttpError(400, "bad_dna", "dna がありません");
  const dnaJson = JSON.stringify(b.dna);
  if (dnaJson.length > 16_000) throw new HttpError(413, "dna_too_large", "dna が大きすぎます");
  return dnaJson;
}

async function handleDesign(body: unknown) {
  const b = (body ?? {}) as Record<string, unknown>;
  const text = checkRequestText(b.request, catalog.limits.maxRequestLength);
  const r = await callClaude<DesignOutput>(
    DESIGN_SYSTEM, `設計してほしい道具：\n<request>\n${text}\n</request>`, betaZodOutputFormat(schemas.design));
  const o = r.output;
  if (o.status !== "ok" || !o.design) {
    return { status: "unsupported", message: o.message || "この指示はこの版では設計できません", suggestions: o.suggestions.slice(0, 3), model: r.model, usage: r.usage };
  }
  const { dna, fixes } = toDna(o.design, catalog);
  return {
    status: "ok", dna, designPoints: designPoints(o.design), plans: toPlans(o.design.plans, catalog),
    fixes, message: o.message, model: r.model, usage: r.usage,
  };
}

async function handlePlan(body: unknown) {
  const b = (body ?? {}) as Record<string, unknown>;
  const dnaJson = readDna(b);
  const wish = b.wish == null ? "" : checkRequestText(b.wish, catalog.limits.maxRequestLength);
  const user = `発明DNA：\n<dna>\n${dnaJson}\n</dna>\n` + (wish ? `確かめたいこと：\n<wish>\n${wish}\n</wish>` : "確かめたいこと：指定なし");
  const r = await callClaude<PlanOutput>(PLAN_SYSTEM, user, betaZodOutputFormat(schemas.planOnly));
  const o = r.output;
  if (o.status !== "ok" || o.plans.length === 0) return { status: "unsupported", message: o.message || "この発明に合う実験を作れませんでした", model: r.model, usage: r.usage };
  return { status: "ok", plans: toPlans(o.plans, catalog), message: o.message, model: r.model, usage: r.usage };
}

async function handleRevise(body: unknown) {
  const b = (body ?? {}) as Record<string, unknown>;
  const text = checkRequestText(b.instruction, catalog.limits.maxRequestLength);
  const dnaJson = readDna(b);
  const exp = pickExperiment(b.lastResult, METRIC_KEYS, SCENARIO_IDS);
  const user = `現在の発明DNA：\n<dna>\n${dnaJson}\n</dna>\n` +
    (exp ? `直近の実験結果：\n<experiment>\n${JSON.stringify(exp)}\n</experiment>\n` : "直近の実験結果：なし\n") +
    `改良の指示：\n<instruction>\n${text}\n</instruction>`;
  const r = await callClaude<ReviseOutput>(REVISE_SYSTEM, user, betaZodOutputFormat(schemas.revise));
  const o = r.output;
  if (o.status !== "ok" || o.operations.length === 0) return { status: "unsupported", message: o.summary || "この指示では改良案を作れませんでした", model: r.model, usage: r.usage };
  return { status: "ok", proposal: toProposal(o, catalog), model: r.model, usage: r.usage };
}

function send(res: ServerResponse, status: number, obj: unknown) {
  const body = JSON.stringify(obj);
  res.writeHead(status, { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" });
  res.end(body);
}

const ROUTES: Record<string, (body: unknown) => Promise<unknown>> = {
  "/v1/design": handleDesign,
  "/v1/plan": handlePlan,
  "/v1/revise": handleRevise,
};

async function route(req: IncomingMessage, res: ServerResponse) {
  const url = new URL(req.url ?? "/", "http://localhost");
  if (req.method === "GET" && url.pathname === "/health") {
    return send(res, 200, { ok: true, model: MODEL, effort: EFFORT, apiKey: hasKey, schemaVersion: catalog.schemaVersion ?? 1, behaviors: catalog.behaviors.length });
  }
  // 発明ライブラリの入口（AI を呼ばないので、AI の回数制限とは別に数える）
  if (await library(req, res, url)) return;
  const handler = ROUTES[url.pathname];
  const isStt = url.pathname === "/v1/transcribe";
  if (req.method !== "POST" || (!handler && !isStt)) throw new HttpError(404, "not_found", "not found");
  if (TOKEN && req.headers["x-ilab-token"] !== TOKEN) throw new HttpError(401, "unauthorized", "認証に失敗しました");
  if (!limiter.take()) throw new HttpError(429, "rate_limited", "リクエストが多すぎます。少し待ってから試してください");
  if (isStt) return send(res, 200, await handleTranscribe(req));
  const body = await readJsonBody(req, 32 * 1024);
  send(res, 200, await handler(body));
}

const server = createServer((req, res) => {
  const started = Date.now();
  route(req, res)
    .catch((e: unknown) => {
      const [status, code, message] = toHttpError(e);
      send(res, status, { status: "error", code, message });
    })
    .finally(() => console.log(`${req.method} ${req.url} -> ${res.statusCode} ${Date.now() - started}ms`));
});

function toHttpError(e: unknown): [number, string, string] {
  if (e instanceof HttpError) return [e.status, e.code, e.message];
  if (e instanceof Anthropic.AuthenticationError) return [502, "upstream_auth", "プロキシのAPIキーが無効です"];
  if (e instanceof Anthropic.RateLimitError) return [429, "upstream_rate_limited", "AIサービスが混み合っています"];
  if (e instanceof Anthropic.BadRequestError) {
    console.error("[claude] bad request:", e.message);
    // API の利用残高の不足は「AIが使えない」扱い（アプリはオフラインのサンプル設計に切り替える）
    if (/credit balance/i.test(e.message)) return [503, "upstream_billing", "AIサービスの利用残高が不足しています"];
    return [502, "upstream_bad_request", "AIへのリクエストが不正でした"];
  }
  if (e instanceof Anthropic.APIConnectionTimeoutError) return [504, "upstream_timeout", "AIの応答がタイムアウトしました"];
  if (e instanceof Anthropic.APIConnectionError) return [504, "upstream_unreachable", "AIサービスに接続できません"];
  if (e instanceof Anthropic.APIError) { console.error("[claude] api error:", e.status, e.message); return [502, "upstream_error", "AIサービスでエラーが発生しました"]; }
  console.error("[proxy] unexpected:", e);
  return [500, "internal", "プロキシ内部エラー"];
}

server.listen(PORT, HOST, () => {
  console.log(`IMPOSSIBLE LAB proxy listening on http://${HOST}:${PORT} model=${MODEL} effort=${EFFORT} apiKey=${hasKey ? "set" : "MISSING"}`);
  if (!hasKey) console.log("  proxy/.env に ANTHROPIC_API_KEY=... を書いてから再起動してください");
  startStt();
  keepAdbReverse();
});
