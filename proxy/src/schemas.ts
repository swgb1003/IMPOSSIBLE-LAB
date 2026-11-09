// Claude の構造化出力スキーマと、アプリが読む形式（発明DNA・改良提案）への変換。
// 数値の範囲チェックはアプリ側の DnaJson / RevisionEngine が必ず行う（ここでは形だけ保証する）。
import { z } from "zod";
import type { Catalog } from "./catalog.ts";

function enumOf(values: string[]) {
  if (values.length === 0) throw new Error("empty enum");
  return z.enum(values as [string, ...string[]]);
}

export function buildSchemas(c: Catalog) {
  const partType = enumOf(c.partTypes.map((p) => p.type));
  const behaviorType = enumOf(c.behaviors.map((b) => b.type));
  const category = enumOf(c.categories.map((x) => x.id));
  const param = z.object({ name: z.string(), value: z.number() });
  const part = z.object({
    id: z.string(),
    type: partType,
    position: z.array(z.number()).describe("[x, y, z] meters, local to the body center"),
    scale: z.array(z.number()).describe("[x, y, z] meters"),
  });
  const behavior = z.object({
    type: behaviorType,
    targetPart: z.string().describe("part id; empty string when the behavior needs no target"),
    parameters: z.array(param),
  });

  const design = z.object({
    status: z.enum(["ok", "unsupported"]),
    message: z.string().describe("Japanese. Why unsupported, or one-line summary when ok"),
    suggestions: z.array(z.string()).describe("Japanese. Up to 3 example requests the user could try instead"),
    design: z
      .object({
        name: z.string(),
        category,
        purpose: z.string(),
        parts: z.array(part),
        behaviors: z.array(behavior),
        designPoints: z.array(z.string()).describe("Japanese. 2-4 short reasons for the design choices"),
      })
      .nullable(),
  });

  const operation = z.object({
    op: z.enum(["add_part", "remove_part", "update_parameter"]),
    behavior: z.string().describe("behavior type for update_parameter, else empty string"),
    parameter: z.string().describe("parameter name for update_parameter, else empty string"),
    value: z.number().describe("new value for update_parameter, else 0"),
    partId: z.string().describe("part id for remove_part, else empty string"),
    part: part.nullable().describe("new part for add_part, else null"),
  });
  const revise = z.object({
    status: z.enum(["ok", "unsupported"]),
    summary: z.string().describe("Japanese, one line, what changes and why"),
    notes: z.array(z.string()).describe("Japanese. Up to 3 notes such as trade-offs"),
    operations: z.array(operation),
  });
  return { design, revise };
}

export type DesignOutput = z.infer<ReturnType<typeof buildSchemas>["design"]>;
export type ReviseOutput = z.infer<ReturnType<typeof buildSchemas>["revise"]>;

const SAFE_ID = /^[a-z0-9][a-z0-9_-]{0,47}$/;

/** アプリの安全なID規則（英小文字・数字・_-、48文字以内）に合わせる。 */
export function sanitizeId(raw: string, fallback: string): string {
  let s = (raw ?? "").toLowerCase().replace(/[^a-z0-9_-]+/g, "-").replace(/^[-_]+/, "").slice(0, 48);
  if (!SAFE_ID.test(s)) s = fallback;
  return s;
}

function clip(s: string, n: number): string {
  s = (s ?? "").trim();
  return s.length > n ? s.slice(0, n) : s;
}

export function newInventionId(now = Date.now()): string {
  return "ai-" + now.toString(36) + "-" + Math.floor(Math.random() * 36 ** 3).toString(36);
}

/** Claude の設計出力 → 発明DNA（schemaVersion 1）。 */
export function toDna(d: NonNullable<DesignOutput["design"]>, c: Catalog, id = newInventionId()) {
  const idMap = new Map<string, string>();
  const used = new Set<string>();
  const parts = d.parts.slice(0, c.limits.maxParts).map((p, i) => {
    let pid = sanitizeId(p.id, `part-${i + 1}`);
    while (used.has(pid)) pid = `${pid.slice(0, 44)}-${i + 1}`;
    used.add(pid);
    idMap.set(p.id, pid);
    return { id: pid, type: p.type, position: vec3(p.position), scale: vec3(p.scale, 0.1) };
  });
  const behaviors = d.behaviors.slice(0, c.limits.maxBehaviors).map((b) => {
    const out: Record<string, unknown> = { type: b.type };
    if (b.targetPart) out.targetPart = idMap.get(b.targetPart) ?? sanitizeId(b.targetPart, b.targetPart);
    out.parameters = Object.fromEntries(b.parameters.map((p) => [p.name, p.value]));
    return out;
  });
  return {
    schemaVersion: 1,
    id,
    name: clip(d.name, c.limits.maxNameLength) || "AI設計の発明",
    category: d.category,
    purpose: clip(d.purpose, c.limits.maxPurposeLength) || "（目的未記入）",
    mode: "dream",
    parts,
    behaviors,
    constraints: { maxParts: c.limits.maxParts, maxSpeed: 3.0 },
    revision: 1,
    parentRevision: null,
  };
}

function vec3(v: number[], fill = 0): [number, number, number] {
  const a = (v ?? []).map((x) => (Number.isFinite(x) ? x : fill));
  return [a[0] ?? fill, a[1] ?? fill, a[2] ?? fill];
}

/** Claude の改良出力 → アプリの RevisionProposal JSON。 */
export function toProposal(r: ReviseOutput) {
  // 構造化出力では全項目が必須のため、空の「埋め草」操作（partId が空の remove_part など）が混ざることがある。
  // 中身のない操作だけを取り除く（中身のある不正な操作はアプリ側の検証で止める）。
  const meaningful = r.operations.filter((o) =>
    o.op === "remove_part" ? o.partId.trim() !== ""
    : o.op === "update_parameter" ? o.behavior.trim() !== "" && o.parameter.trim() !== ""
    : o.part != null);
  const operations = meaningful.slice(0, 6).map((o) => {
    if (o.op === "update_parameter") return { op: o.op, behavior: o.behavior, parameter: o.parameter, value: o.value };
    if (o.op === "remove_part") return { op: o.op, partId: o.partId };
    const p = o.part;
    return {
      op: o.op,
      part: p ? { id: sanitizeId(p.id, "added-part"), type: p.type, position: vec3(p.position), scale: vec3(p.scale, 0.1) } : null,
    };
  });
  return { summary: clip(r.summary, 120), notes: r.notes.slice(0, 3).map((n) => clip(n, 120)), operations };
}
