// IMPOSSIBLE LAB AI設計プロキシ。APIキーはこのサーバーの環境変数だけに置き、アプリ（Quest）には渡さない。
// 起動: npm start（proxy/.env の ANTHROPIC_API_KEY を読む）
import { createServer, type IncomingMessage, type ServerResponse } from "node:http";
import { fileURLToPath } from "node:url";
import path from "node:path";
import Anthropic from "@anthropic-ai/sdk";
import { betaZodOutputFormat } from "@anthropic-ai/sdk/helpers/beta/zod";
import { loadCatalog, supportedCategories } from "./catalog.ts";
import { buildSchemas, toDna, toProposal } from "./schemas.ts";
import type { DesignOutput, ReviseOutput } from "./schemas.ts";
import { designSystemPrompt, reviseSystemPrompt } from "./prompts.ts";
import { checkRequestText, pickExperiment, RateLimiter, readJsonBody, HttpError } from "./http-util.ts";

const here = path.dirname(fileURLToPath(import.meta.url));
const HOST = process.env.ILAB_HOST ?? "127.0.0.1";
const PORT = Number(process.env.ILAB_PORT ?? 8787);
const MODEL = process.env.ILAB_MODEL ?? "claude-opus-5-5";
const TOKEN = process.env.ILAB_PROXY_TOKEN ?? ""; // 設定時のみ x-ilab-token を要求（LAN公開時用）
const CATALOG_PATH = process.env.ILAB_CATALOG ?? path.join(here, "..", "catalog.json");

const catalog = loadCatalog(CATALOG_PATH);
const schemas = buildSchemas(catalog);
const DESIGN_SYSTEM = designSystemPrompt(catalog);
const REVISE_SYSTEM = reviseSystemPrompt(catalog);
const limiter = new RateLimiter(Number(process.env.ILAB_RATE_PER_10MIN ?? 40), 10 * 60 * 1000);

const hasKey = Boolean(process.env.ANTHROPIC_API_KEY || process.env.ANTHROPIC_AUTH_TOKEN);
// 1回あたり60秒、SDK既定の再試行2回（429/5xx/接続エラー）
const client = hasKey ? new Anthropic({ timeout: 60_000, maxRetries: 2 }) : null;

// 拒否（refusal）時は推奨モデルへサーバー側で自動切替
const FALLBACK = { betas: ["server-side-fallback-2026-07-01"], fallbacks: "default" as const };

async function callClaude<T>(system: string, user: string, format: ReturnType<typeof betaZodOutputFormat>) {
  if (!client) throw new HttpError(503, "no_api_key", "プロキシに APIキーが設定されていません");
  const started = Date.now();
  const res = await client.beta.messages.parse({
    model: MODEL,
    max_tokens: 16000,
    ...FALLBACK,
    system,
    output_config: { effort: "medium", format },
    messages: [{ role: "user", content: user }],
  });
  const ms = Date.now() - started;
  console.log(`[claude] model=${res.model} stop=${res.stop_reason} in=${res.usage.input_tokens} out=${res.usage.output_tokens} ${ms}ms`);
  if (res.stop_reason === "refusal") throw new HttpError(422, "refused", "AIがこの指示への回答を控えました。別の言い方で試してください");
  if (res.stop_reason === "max_tokens") throw new HttpError(502, "truncated", "AIの出力が途中で切れました");
  if (res.parsed_output == null) throw new HttpError(502, "unparsable", "AIの出力を読み取れませんでした");
  return { output: res.parsed_output as T, model: res.model, ms };
}

async function handleDesign(body: unknown) {
  const b = (body ?? {}) as Record<string, unknown>;
  const text = checkRequestText(b.request, catalog.limits.maxRequestLength);
  const r = await callClaude<DesignOutput>(
    DESIGN_SYSTEM, `設計してほしい道具：\n<request>\n${text}\n</request>`, betaZodOutputFormat(schemas.design));
  const o = r.output;
  if (o.status !== "ok" || !o.design) {
    return { status: "unsupported", message: o.message || "この指示はこの版では設計できません", suggestions: o.suggestions.slice(0, 3), model: r.model };
  }
  if (!supportedCategories(catalog).some((x) => x.id === o.design!.category)) {
    return { status: "unsupported", message: "このカテゴリはこの版では未対応です", suggestions: o.suggestions.slice(0, 3), model: r.model };
  }
  return { status: "ok", dna: toDna(o.design, catalog), designPoints: o.design.designPoints.slice(0, 4), message: o.message, model: r.model };
}

async function handleRevise(body: unknown) {
  const b = (body ?? {}) as Record<string, unknown>;
  const text = checkRequestText(b.instruction, catalog.limits.maxRequestLength);
  if (typeof b.dna !== "object" || b.dna === null || Array.isArray(b.dna)) throw new HttpError(400, "bad_dna", "dna がありません");
  const dnaJson = JSON.stringify(b.dna);
  if (dnaJson.length > 16_000) throw new HttpError(413, "dna_too_large", "dna が大きすぎます");
  const exp = pickExperiment(b.lastResult);
  const user = `現在の発明DNA：\n<dna>\n${dnaJson}\n</dna>\n` +
    (exp ? `直近の実験結果：\n<experiment>\n${JSON.stringify(exp)}\n</experiment>\n` : "直近の実験結果：なし\n") +
    `改良の指示：\n<instruction>\n${text}\n</instruction>`;
  const r = await callClaude<ReviseOutput>(REVISE_SYSTEM, user, betaZodOutputFormat(schemas.revise));
  const o = r.output;
  if (o.status !== "ok" || o.operations.length === 0) return { status: "unsupported", message: o.summary || "この指示では改良案を作れませんでした", model: r.model };
  return { status: "ok", proposal: toProposal(o), model: r.model };
}

function send(res: ServerResponse, status: number, obj: unknown) {
  const body = JSON.stringify(obj);
  res.writeHead(status, { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" });
  res.end(body);
}

async function route(req: IncomingMessage, res: ServerResponse) {
  const url = new URL(req.url ?? "/", "http://localhost");
  if (req.method === "GET" && url.pathname === "/health") {
    return send(res, 200, { ok: true, model: MODEL, apiKey: hasKey, categories: supportedCategories(catalog).map((x) => x.id) });
  }
  if (req.method !== "POST" || (url.pathname !== "/v1/design" && url.pathname !== "/v1/revise")) {
    throw new HttpError(404, "not_found", "not found");
  }
  if (TOKEN && req.headers["x-ilab-token"] !== TOKEN) throw new HttpError(401, "unauthorized", "認証に失敗しました");
  if (!limiter.take()) throw new HttpError(429, "rate_limited", "リクエストが多すぎます。少し待ってから試してください");
  const body = await readJsonBody(req, 32 * 1024);
  const out = url.pathname === "/v1/design" ? await handleDesign(body) : await handleRevise(body);
  send(res, 200, out);
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
  if (e instanceof Anthropic.BadRequestError) { console.error("[claude] bad request:", e.message); return [502, "upstream_bad_request", "AIへのリクエストが不正でした"]; }
  if (e instanceof Anthropic.APIConnectionTimeoutError) return [504, "upstream_timeout", "AIの応答がタイムアウトしました"];
  if (e instanceof Anthropic.APIConnectionError) return [504, "upstream_unreachable", "AIサービスに接続できません"];
  if (e instanceof Anthropic.APIError) { console.error("[claude] api error:", e.status, e.message); return [502, "upstream_error", "AIサービスでエラーが発生しました"]; }
  console.error("[proxy] unexpected:", e);
  return [500, "internal", "プロキシ内部エラー"];
}

server.listen(PORT, HOST, () => {
  console.log(`IMPOSSIBLE LAB proxy listening on http://${HOST}:${PORT} model=${MODEL} apiKey=${hasKey ? "set" : "MISSING"}`);
  if (!hasKey) console.log("  proxy/.env に ANTHROPIC_API_KEY=... を書いてから再起動してください");
});
