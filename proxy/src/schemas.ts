// Claude の構造化出力スキーマと、アプリが読む形式（発明DNA v2・実験計画・改良提案）への変換。
// 数値の範囲・組み合わせのチェックはアプリ側の DnaJson / ExperimentPlanJson / RevisionEngine が必ず行う（ここでは形だけ保証する）。
import { z } from "zod";
import type { Catalog } from "./catalog.ts";

function enumOf(values: string[]) {
  if (values.length === 0) throw new Error("empty enum");
  return z.enum(values as [string, ...string[]]);
}

export function buildSchemas(c: Catalog) {
  const partType = enumOf(c.partTypes.map((p) => p.type));
  const behaviorType = enumOf(c.behaviors.map((b) => b.type));
  const appearance = enumOf((c.appearances ?? [{ id: "lab_white", labelJa: "" }]).map((x) => x.id));
  const tag = enumOf(c.tags.map((x) => x.id));
  const mount = enumOf(c.mounts.map((x) => x.id));
  const shape = enumOf(c.shapes.map((x) => x.id));
  const material = enumOf(c.materials.map((x) => x.id));
  const status = enumOf(c.functionStatuses);
  const scenario = enumOf(c.experiments.scenarios.map((x) => x.id));
  const moduleType = enumOf(c.experiments.modules.map((x) => x.id));
  const metric = enumOf(c.experiments.metrics.map((x) => x.key));
  const param = z.object({ name: z.string(), value: z.number() });
  // 動作プログラム・関節・期待（仕様 28章）は、構造化出力の文法の大きさの上限に収めるため、アプリの形の JSON を文字列で受け取る。
  // プロキシは JSON として読むだけ（コードとして実行しない）で、id を整えてからアプリの検証器に渡す
  const jsonText = (what: string) => z.string().describe(`${what} as JSON text in the app format described in the system prompt, or empty string`);

  const part = z.object({
    id: z.string(),
    type: partType,
    shape,
    material,
    position: z.array(z.number()).describe("[x, y, z] meters, local to the body center"),
    scale: z.array(z.number()).describe("[x, y, z] meters"),
  });
  const behavior = z.object({
    type: behaviorType,
    targetPart: z.string().describe("part id; empty string when the behavior needs no target"),
    parameters: z.array(param),
  });
  const need = z.object({
    need: z.string().describe("Japanese, one user need in a few words"),
    // 文法の大きさを抑えるため文字列（設計に無い機能は toDna が外す）
    blocks: z.array(z.string()).describe("behavior types (from the design) that realize and test this need; empty when unverifiable"),
    status,
    note: z.string().describe("Japanese. Required for simplified/unverifiable: what is simplified or why it cannot be tested"),
  });
  const plan = z.object({
    title: z.string().describe("Japanese, short name of the experiment"),
    aim: z.string().describe("Japanese, one sentence: which need this experiment checks"),
    scenario,
    mode: z.enum(["dream", "reality"]),
    durationSec: z.number(),
    modules: z.array(z.object({ type: moduleType, params: z.array(param) })),
    metrics: z.array(metric),
    // metric は文字列（計画の metrics に入っているかはアプリの検証器が確認する）
    criteria: z.array(z.object({ metric: z.string().describe("one of this plan's metrics"), op: z.enum([">=", "<="]), value: z.number(), reason: z.string() })),
    expect: jsonText("approach_device only: the expectation"),
  });

  const design = z.object({
    status: z.enum(["ok", "unsupported"]),
    message: z.string().describe("Japanese. Why unsupported, or one-line summary when ok"),
    suggestions: z.array(z.string()).describe("Japanese. Up to 3 example requests the user could try instead"),
    design: z
      .object({
        name: z.string(),
        tags: z.array(tag),
        mount,
        appearance: appearance.describe("color preset id; does not change performance"),
        purpose: z.string(),
        parts: z.array(part),
        behaviors: z.array(behavior),
        functionMap: z.array(need),
        program: jsonText("Motion program (with its joints), only when the request needs conditions or an order of movements;"),
        designPoints: z.array(z.string()).describe("Japanese. 2-4 short reasons for the design choices"),
        plans: z.array(plan).describe("1-3 experiment plans"),
      })
      .nullable(),
  });

  const planOnly = z.object({
    status: z.enum(["ok", "unsupported"]),
    message: z.string().describe("Japanese, one line"),
    plans: z.array(plan),
  });

  const operation = z.object({
    op: z.enum(["add_part", "remove_part", "update_parameter", "add_behavior", "remove_behavior", "add_rule", "remove_rule", "update_rule", "add_joint", "update_joint"]),
    behavior: z.string().describe("behavior type for update_parameter / add_behavior / remove_behavior, else empty string"),
    parameter: z.string().describe("parameter name for update_parameter, else empty string"),
    value: z.number().describe("new value for update_parameter, else 0"),
    partId: z.string().describe("part id for remove_part, else empty string"),
    part: part.nullable().describe("new part for add_part, else null"),
    targetPart: z.string().describe("target part id for add_behavior, else empty string"),
    parameters: z.array(param).describe("parameters for add_behavior, else empty"),
    ruleId: z.string().describe("rule id for remove_rule / update_rule, else empty string"),
    field: z.string().describe("update_rule: enabled|when|target|speed|seconds; update_joint: speed|min|max; else empty string"),
    index: z.number().describe("update_rule: index of the condition (when) or the step (target/speed/seconds), else 0"),
    rule: jsonText("add_rule: the new rule"),
    joint: jsonText("add_joint: the new joint (partId names the part)"),
  });
  const revise = z.object({
    status: z.enum(["ok", "unsupported"]),
    summary: z.string().describe("Japanese, one line, what changes and why"),
    notes: z.array(z.string()).describe("Japanese. Up to 3 notes such as trade-offs"),
    operations: z.array(operation),
  });
  return { design, planOnly, revise };
}

type Schemas = ReturnType<typeof buildSchemas>;
export type DesignOutput = z.infer<Schemas["design"]>;
export type PlanOutput = z.infer<Schemas["planOnly"]>;
export type PlanSpec = PlanOutput["plans"][number];
export type ReviseOutput = z.infer<Schemas["revise"]>;

const SAFE_ID = /^[a-z0-9][a-z0-9_-]{0,47}$/;

/** アプリの安全なID規則（英小文字・数字・_-、48文字以内）に合わせる。 */
export function sanitizeId(raw: string, fallback: string): string {
  let s = (raw ?? "").toLowerCase().replace(/[^a-z0-9_-]+/g, "-").replace(/^[-_]+/, "").slice(0, 48);
  if (!SAFE_ID.test(s)) s = fallback;
  return s;
}

function clip(s: string, n: number): string {
  s = (s ?? "").trim();
  return [...s].length > n ? [...s].slice(0, n).join("") : s;
}

export function newInventionId(now = Date.now()): string {
  return "ai-" + now.toString(36) + "-" + Math.floor(Math.random() * 36 ** 3).toString(36);
}

/**
 * Claude の設計出力 → 発明DNA（schemaVersion 2）。
 * 機能対応表は「アプリの検証で全体が止まる」小さな食い違いだけを直し、直した内容を fixes に返す（評価で数える）。
 */
export function toDna(d: NonNullable<DesignOutput["design"]>, c: Catalog, id = newInventionId()) {
  const fixes: string[] = [];
  const idMap = new Map<string, string>();
  const used = new Set<string>();
  const parts = d.parts.slice(0, c.limits.maxParts).map((p, i) => {
    let pid = sanitizeId(p.id, `part-${i + 1}`);
    while (used.has(pid)) pid = `${pid.slice(0, 44)}-${i + 1}`;
    used.add(pid);
    idMap.set(p.id, pid);
    return { id: pid, type: p.type, shape: p.shape, material: p.material, position: vec3(p.position), scale: vec3(p.scale, 0.1) } as Record<string, unknown>;
  });
  if (d.parts.length > c.limits.maxParts) fixes.push(`部品を${c.limits.maxParts}個までに切り詰め`);
  const seenBehavior = new Set<string>();
  const behaviors = d.behaviors.filter((b) => !seenBehavior.has(b.type) && seenBehavior.add(b.type)).slice(0, c.limits.maxBehaviors).map((b) => {
    const out: Record<string, unknown> = { type: b.type };
    if (b.targetPart) out.targetPart = idMap.get(b.targetPart) ?? sanitizeId(b.targetPart, b.targetPart);
    out.parameters = Object.fromEntries(b.parameters.map((p) => [p.name, p.value]));
    return out;
  });
  const present = new Set(behaviors.map((b) => b.type as string));
  const functionMap = d.functionMap.slice(0, c.limits.maxFunctionNeeds).map((n) => {
    const blocks = [...new Set(n.blocks)].filter((b) => present.has(b));
    if (blocks.length < new Set(n.blocks).size) fixes.push(`「${clip(n.need, 20)}」の機能対応表から、設計に無い機能を外した`);
    let status = n.status;
    let note = clip(n.note, c.limits.maxNoteLength);
    // 「動きを見せる」は動作プログラムがあれば機能なしでよい（アプリの検証器と同じ決まり）
    const shownByProgram = status === "demonstrated" && parseObject(d.program) != null;
    if ((status === "simulated" || status === "simplified" || status === "demonstrated") && blocks.length === 0 && !shownByProgram) {
      status = "unverifiable";
      note = note || "対応する機能がないため検証しない";
      fixes.push(`「${clip(n.need, 20)}」を「検証できない」に変更（対応する機能なし）`);
    }
    if (status === "unverifiable" && !note) { note = "理由の記載なし"; fixes.push(`「${clip(n.need, 20)}」に理由を補った`); }
    return { need: clip(n.need, c.limits.maxNeedLength) || "（要望）", blocks, status, ...(note ? { note } : {}) };
  });
  const tags = [...new Set(d.tags)].slice(0, c.limits.maxTags);
  const prog = toProgram(d.program, idMap, c, fixes);
  const program = prog?.program;
  for (const [pid, j] of Object.entries(prog?.joints ?? {})) {
    const part = parts.find((p) => p.id === ref(pid, idMap));
    if (part) part.joint = toJoint(j, idMap);
    else fixes.push(`関節の部品 ${clip(pid, 20)} が無いので関節を外した`);
  }
  return {
    dna: {
      schemaVersion: 3,
      id,
      name: clip(d.name, c.limits.maxNameLength) || "AI設計の発明",
      purpose: clip(d.purpose, c.limits.maxPurposeLength) || "（目的未記入）",
      tags: tags.length ? tags : ["daily"],
      mount: d.mount,
      appearance: d.appearance,
      mode: "dream",
      parts,
      behaviors,
      functionMap,
      ...(program ? { program } : {}),
      constraints: { maxParts: c.limits.maxParts, maxSpeed: 3.0 },
      revision: 1,
      parentRevision: null,
    },
    fixes,
  };
}

/** Claude の実験計画 → アプリの ExperimentPlan JSON（{ title, aim, plan }）。シードはアプリ側の既定に合わせて固定。 */
export function toPlans(plans: PlanSpec[], c: Catalog) {
  return plans.slice(0, c.limits.maxPlans).map((p) => ({
    title: clip(p.title, 40),
    aim: clip(p.aim, 120),
    plan: {
      scenario: p.scenario,
      mode: p.mode,
      durationSec: Number.isFinite(p.durationSec) ? Math.min(c.limits.maxDurationSec, Math.max(c.limits.minDurationSec, p.durationSec)) : 10,
      seed: 1234,
      modules: p.modules.slice(0, c.limits.maxModules).map((m) => ({ type: m.type, ...Object.fromEntries(m.params.map((x) => [x.name, x.value])) })),
      metrics: [...new Set(p.metrics)].slice(0, c.limits.maxMetrics),
      criteria: p.criteria.slice(0, c.limits.maxCriteria).map((k) => ({ metric: k.metric, op: k.op, value: k.value, reason: clip(k.reason, c.limits.maxReasonLength) })),
      ...(p.scenario === "approach_device" && toExpect(p.expect) ? { expect: toExpect(p.expect) } : {}),
    },
  }));
}

function vec3(v: number[], fill = 0): [number, number, number] {
  const a = (v ?? []).map((x) => (Number.isFinite(x) ? x : fill));
  return [a[0] ?? fill, a[1] ?? fill, a[2] ?? fill];
}

/** Claude の改良出力 → アプリの RevisionProposal JSON。 */
export function toProposal(r: ReviseOutput, c: Catalog) {
  // 構造化出力では全項目が必須のため、空の「埋め草」操作（partId が空の remove_part など）が混ざることがある。
  // 中身のない操作だけを取り除く（中身のある不正な操作はアプリ側の検証で止める）。
  const meaningful = r.operations.filter((o) =>
    o.op === "remove_part" ? o.partId.trim() !== ""
    : o.op === "update_parameter" ? o.behavior.trim() !== "" && o.parameter.trim() !== ""
    : o.op === "add_behavior" || o.op === "remove_behavior" ? o.behavior.trim() !== ""
    : o.op === "remove_rule" || o.op === "update_rule" ? o.ruleId.trim() !== ""
    : o.op === "add_rule" ? parseObject(o.rule) != null
    : o.op === "add_joint" ? parseObject(o.joint) != null && o.partId.trim() !== ""
    : o.op === "update_joint" ? o.partId.trim() !== ""
    : o.part != null);
  const operations = meaningful.slice(0, 6).map((o) => {
    if (o.op === "update_parameter") return { op: o.op, behavior: o.behavior, parameter: o.parameter, value: o.value };
    if (o.op === "remove_part") return { op: o.op, partId: o.partId };
    if (o.op === "remove_behavior") return { op: o.op, behavior: o.behavior };
    if (o.op === "remove_rule") return { op: o.op, ruleId: o.ruleId };
    if (o.op === "update_rule")
      return { op: o.op, ruleId: o.ruleId, field: o.field, index: Math.max(0, Math.round(o.index)), value: o.field === "enabled" ? (o.value >= 0.5 ? 1 : 0) : o.value };
    if (o.op === "add_rule") return { op: o.op, rule: toRule(parseObject(o.rule)!, new Map(), c) };
    if (o.op === "add_joint") return { op: o.op, partId: o.partId, joint: toJoint(parseObject(o.joint)!, new Map()) };
    if (o.op === "update_joint") return { op: o.op, partId: o.partId, field: o.field, value: o.value };
    if (o.op === "add_behavior")
      return { op: o.op, behavior: o.behavior, targetPart: o.targetPart ? sanitizeId(o.targetPart, o.targetPart) : "", parameters: Object.fromEntries(o.parameters.map((p) => [p.name, p.value])) };
    const p = o.part;
    return {
      op: o.op,
      part: p ? { id: sanitizeId(p.id, "added-part"), type: p.type, shape: p.shape, material: p.material, position: vec3(p.position), scale: vec3(p.scale, 0.1) } : null,
    };
  });
  return { summary: clip(r.summary, 120), notes: r.notes.slice(0, 3).map((n) => clip(n, 120)), operations };
}

type Json = Record<string, unknown>;

/** JSON の文字列 → オブジェクト（読めない・オブジェクトでなければ null）。読むだけで、評価はしない。 */
export function parseObject(text: string): Json | null {
  if (!text || !text.trim()) return null;
  try {
    const v = JSON.parse(text);
    return v && typeof v === "object" && !Array.isArray(v) ? (v as Json) : null;
  } catch {
    return null;
  }
}

const ref = (id: unknown, idMap: Map<string, string>) => {
  const s = typeof id === "string" ? id : "";
  return idMap.get(s) ?? sanitizeId(s, s || "x");
};

/** 関節：parent の id を設計の id に合わせ、空の parent は付けない。値の確認はアプリの検証器。 */
export function toJoint(j: Json, idMap: Map<string, string>) {
  const out: Json = { ...j };
  if (typeof j.parent === "string" && j.parent.trim()) out.parent = ref(j.parent, idMap); else delete out.parent;
  return out;
}

/** ルール：id を安全な形にし、関節の参照を設計の id に合わせる。ほかの項目はそのまま（未知の項目はアプリが拒否する）。 */
export function toRule(r: Json, idMap: Map<string, string>, c: Catalog) {
  const fix = (x: unknown) => {
    if (!x || typeof x !== "object" || Array.isArray(x)) return x;
    const o = { ...(x as Json) };
    if (typeof o.joint === "string") o.joint = ref(o.joint, idMap);
    if (typeof o.flag === "string") o.flag = sanitizeId(o.flag, "flag");
    if (o.action === "set_flag" && typeof o.name === "string") o.name = sanitizeId(o.name, "flag");
    return o;
  };
  const out: Json = { ...r, id: sanitizeId(String(r.id ?? ""), "rule") };
  if (typeof r.label === "string") out.label = clip(r.label, c.program.limits.maxLabelLength);
  if (r.when && typeof r.when === "object" && !Array.isArray(r.when))
    out.when = Object.fromEntries(Object.entries(r.when as Json).map(([k, v]) => [k, Array.isArray(v) ? v.map(fix) : v]));
  if (Array.isArray(r.do)) out.do = r.do.map(fix);
  return out;
}

/**
 * 設計の program（JSON 文字列。{ flags, rules, joints: { 部品id: 関節 } }）→ アプリの program と、部品に付ける関節。
 * 読めないときは program なしにして fixes に記録する（評価で数える）。
 */
export function toProgram(text: string, idMap: Map<string, string>, c: Catalog, fixes: string[]) {
  if (!text || !text.trim()) return undefined;
  const o = parseObject(text);
  if (!o || !Array.isArray(o.rules) || o.rules.length === 0) { fixes.push("動作プログラムを読めなかったので外した"); return undefined; }
  const used = new Set<string>();
  const rules = (o.rules as unknown[]).filter((r): r is Json => !!r && typeof r === "object" && !Array.isArray(r)).map((r, i) => {
    const out = toRule(r, idMap, c);
    let id = String(out.id);
    while (used.has(id)) id = `${id.slice(0, 44)}-${i + 1}`;
    used.add(id);
    out.id = id;
    return out;
  });
  const flags = Array.isArray(o.flags) ? [...new Set((o.flags as unknown[]).map((f) => sanitizeId(String(f), "flag")))] : [];
  const joints = o.joints && typeof o.joints === "object" && !Array.isArray(o.joints)
    ? Object.fromEntries(Object.entries(o.joints as Json).filter(([, v]) => v && typeof v === "object" && !Array.isArray(v)).map(([k, v]) => [k, v as Json]))
    : {};
  return { program: { version: 1, flags, rules }, joints };
}

/** 期待（JSON 文字列）→ アプリの形。when がなければ付けない。 */
export function toExpect(text: string) {
  const e = parseObject(text);
  if (!e || !e.when || typeof e.when !== "object") return undefined;
  const out: Json = { ...e };
  if (typeof e.minRatio === "number") out.minRatio = Math.min(1, Math.max(0, e.minRatio));
  if (!("joint" in out)) out.joint = "";
  return out;
}
