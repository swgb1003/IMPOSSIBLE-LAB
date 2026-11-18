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
  const P = c.program;
  const sensor = enumOf(P.sensors.map((s) => s.id));
  const action = enumOf(P.actions);
  const op = enumOf(P.ops);

  // 動作プログラム（仕様 28章）。構造化出力では全項目が必須なので、使わない項目は "" / 0 / false にして、変換で落とす
  const joint = z.object({
    type: z.enum(["hinge", "slider"]),
    parent: z.string().describe("part id this joint is attached to, or empty string for the body"),
    axis: z.enum(["x", "y", "z"]),
    pivot: z.array(z.number()).describe("[x, y, z] meters, offset of the hinge point from the center of this part"),
    min: z.number(), max: z.number(),
    speed: z.number().describe("max speed: degrees/s for hinge, m/s for slider"),
    home: z.number().describe("value at start"),
  });
  const condition = z.object({
    kind: z.enum(["sensor", "flag"]),
    sensor: sensor.describe("for kind sensor"),
    op: op.describe("for numeric sensors"),
    value: z.number().describe("for numeric sensors"),
    is: z.boolean().describe("for boolean sensors and flags"),
    joint: z.string().describe("joint part id for joint_position, else empty string"),
    flag: z.string().describe("flag name for kind flag, else empty string"),
  });
  const step = z.object({
    action,
    joint: z.string().describe("move_joint: joint part id"),
    target: z.number().describe("move_joint: target value"),
    speed: z.number().describe("move_joint: speed, 0 = the joint's max speed"),
    behavior: z.string().describe("set_behavior / set_parameter: behavior type"),
    on: z.boolean().describe("set_behavior"),
    parameter: z.string().describe("set_parameter: parameter name"),
    value: z.number().describe("set_parameter: value"),
    name: z.string().describe("set_flag: flag name"),
    flagValue: z.boolean().describe("set_flag: new value"),
    seconds: z.number().describe("wait: seconds"),
  });
  const rule = z.object({
    id: z.string(),
    label: z.string().describe("Japanese, short"),
    trigger: z.enum(["rise", "while"]),
    cooldown: z.number(),
    match: z.enum(["all", "any"]),
    conditions: z.array(condition),
    do: z.array(step),
  });
  const program = z.object({ flags: z.array(z.string()), rules: z.array(rule) });

  const part = z.object({
    id: z.string(),
    type: partType,
    shape,
    material,
    position: z.array(z.number()).describe("[x, y, z] meters, local to the body center"),
    scale: z.array(z.number()).describe("[x, y, z] meters"),
    joint: joint.nullable().describe("null unless a program moves this part"),
  });
  const behavior = z.object({
    type: behaviorType,
    targetPart: z.string().describe("part id; empty string when the behavior needs no target"),
    parameters: z.array(param),
  });
  const need = z.object({
    need: z.string().describe("Japanese, one user need in a few words"),
    blocks: z.array(behaviorType).describe("behaviors that realize and test this need; empty when unverifiable"),
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
    criteria: z.array(z.object({ metric, op: z.enum([">=", "<="]), value: z.number(), reason: z.string() })),
    expect: z.object({
      sensor, op, value: z.number(),
      joint: z.string().describe("joint part id, or empty string when checking a behavior"),
      atLeast: z.number(),
      behavior: z.string().describe("behavior type that should be on, used when joint is empty"),
      minRatio: z.number().describe("0..1"),
    }).nullable().describe("approach_device only: what should happen while the condition holds; null otherwise"),
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
        program: program.nullable().describe("null unless the request needs conditions or an order of movements"),
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
    rule: rule.nullable().describe("new rule for add_rule, else null"),
    joint: joint.nullable().describe("new joint for add_joint (partId names the part), else null"),
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
  d.parts.slice(0, c.limits.maxParts).forEach((p, i) => { if (p.joint) parts[i].joint = toJoint(p.joint, idMap); });
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
    if ((status === "simulated" || status === "simplified") && blocks.length === 0) {
      status = "unverifiable";
      note = note || "対応する機能がないため検証しない";
      fixes.push(`「${clip(n.need, 20)}」を「検証できない」に変更（対応する機能なし）`);
    }
    if (status === "unverifiable" && !note) { note = "理由の記載なし"; fixes.push(`「${clip(n.need, 20)}」に理由を補った`); }
    return { need: clip(n.need, c.limits.maxNeedLength) || "（要望）", blocks, status, ...(note ? { note } : {}) };
  });
  const tags = [...new Set(d.tags)].slice(0, c.limits.maxTags);
  const program = d.program && d.program.rules.length > 0 ? toProgram(d.program, idMap, c) : undefined;
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
      ...(p.expect && p.scenario === "approach_device" ? { expect: toExpect(p.expect) } : {}),
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
    : o.op === "add_rule" ? o.rule != null
    : o.op === "add_joint" ? o.joint != null && o.partId.trim() !== ""
    : o.op === "update_joint" ? o.partId.trim() !== ""
    : o.part != null);
  const operations = meaningful.slice(0, 6).map((o) => {
    if (o.op === "update_parameter") return { op: o.op, behavior: o.behavior, parameter: o.parameter, value: o.value };
    if (o.op === "remove_part") return { op: o.op, partId: o.partId };
    if (o.op === "remove_behavior") return { op: o.op, behavior: o.behavior };
    if (o.op === "remove_rule") return { op: o.op, ruleId: o.ruleId };
    if (o.op === "update_rule")
      return { op: o.op, ruleId: o.ruleId, field: o.field, index: Math.max(0, Math.round(o.index)), value: o.field === "enabled" ? (o.value >= 0.5 ? 1 : 0) : o.value };
    if (o.op === "add_rule") return { op: o.op, rule: toRule(o.rule!, new Map(), c) };
    if (o.op === "add_joint") return { op: o.op, partId: o.partId, joint: toJoint(o.joint!, new Map()) };
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

type JointOut = NonNullable<NonNullable<DesignOutput["design"]>["parts"][number]["joint"]>;
type ProgramOut = NonNullable<NonNullable<DesignOutput["design"]>["program"]>;
type RuleOut = ProgramOut["rules"][number];
type ExpectOut = NonNullable<PlanSpec["expect"]>;

const ref = (id: string, idMap: Map<string, string>) => idMap.get(id) ?? sanitizeId(id, id);
const num = (x: number, fill = 0) => (Number.isFinite(x) ? x : fill);

/** 関節 → アプリの形（parent が空なら付けない）。範囲の確認はアプリの検証器が行う。 */
export function toJoint(j: JointOut, idMap: Map<string, string>) {
  return {
    type: j.type, ...(j.parent.trim() ? { parent: ref(j.parent, idMap) } : {}), axis: j.axis, pivot: vec3(j.pivot),
    min: num(j.min), max: num(j.max), speed: num(j.speed), home: num(j.home),
  };
}

/** ルール → アプリの形。条件・動作は種類ごとに使う項目だけを残す（アプリは未知の項目を拒否する）。 */
export function toRule(r: RuleOut, idMap: Map<string, string>, c: Catalog) {
  const boolSensors = new Set(c.program.sensors.filter((s) => s.isBool).map((s) => s.id));
  const when = r.conditions.slice(0, c.program.limits.maxConditions).map((k) => {
    if (k.kind === "flag") return { flag: sanitizeId(k.flag, "flag"), is: k.is };
    if (boolSensors.has(k.sensor)) return { sensor: k.sensor, is: k.is };
    return { sensor: k.sensor, op: k.op, value: num(k.value), ...(k.sensor === "joint_position" ? { joint: ref(k.joint, idMap) } : {}) };
  });
  const steps = r.do.slice(0, c.program.limits.maxActionsPerRule).map((a) => {
    switch (a.action) {
      case "move_joint": return { action: a.action, joint: ref(a.joint, idMap), target: num(a.target), ...(a.speed > 0 ? { speed: a.speed } : {}) };
      case "set_behavior": return { action: a.action, behavior: a.behavior, on: a.on };
      case "set_parameter": return { action: a.action, behavior: a.behavior, parameter: a.parameter, value: num(a.value) };
      case "set_flag": return { action: a.action, name: sanitizeId(a.name, "flag"), value: a.flagValue };
      default: return { action: a.action, seconds: num(a.seconds, 1) };
    }
  });
  return {
    id: sanitizeId(r.id, "rule"), label: clip(r.label, c.program.limits.maxLabelLength), trigger: r.trigger, cooldown: Math.max(0, num(r.cooldown)),
    when: { [r.match]: when }, do: steps,
  };
}

export function toProgram(p: ProgramOut, idMap: Map<string, string>, c: Catalog) {
  const used = new Set<string>();
  const rules = p.rules.slice(0, c.program.limits.maxRules).map((r, i) => {
    const out = toRule(r, idMap, c);
    while (used.has(out.id)) out.id = `${out.id.slice(0, 44)}-${i + 1}`;
    used.add(out.id);
    return out;
  });
  return { version: 1, flags: [...new Set(p.flags.map((f) => sanitizeId(f, "flag")))].slice(0, c.program.limits.maxFlags), rules };
}

/** 期待 → アプリの形（関節か、オンになっているべき機能のどちらか）。 */
export function toExpect(e: ExpectOut) {
  const base = { when: { sensor: e.sensor, op: e.op, value: num(e.value) }, minRatio: Math.min(1, Math.max(0, num(e.minRatio, 0.7))) };
  return e.joint.trim() ? { ...base, joint: e.joint, atLeast: num(e.atLeast) } : { ...base, joint: "", behavior: e.behavior };
}
