// 部品の品番（数百件）から、依頼に合う候補だけを選んで AI に渡す（全部をシステムプロンプトに入れると長すぎるため）。
// 選び方は API を使わない：依頼の文に品番のタグ・種類の名前が含まれるかで点を付け、種類ごとに数を抑え、足りなければよく使う品番で埋める。
import type { Catalog, PartModelInfo } from "./catalog.ts";

/** どの依頼にも候補として出す、よく使う品番（点が同じなら前にあるものを先に）。 */
export const STAPLES = [
  "bat-18650x2", "bat-bank10000", "bat-aa4", "bat-lipo1000",
  "mot-gear-37mm", "mot-servo-standard", "mot-dc280",
  "led-spot3w", "led-tape30",
  "sen-ultra", "sen-pir", "sen-cam", "sen-imu6",
  "spk-buzzer-loud", "sw-big", "rf-ble",
];

const PER_TYPE = 4;

export function modelCandidates(c: Catalog, text: string, limit = 28): PartModelInfo[] {
  const models = c.partModels ?? [];
  if (!models.length) return [];
  const typeLabel = new Map(c.partTypes.map((t) => [t.type, t.labelJa.replace(/（.*?）/g, "")]));
  const scored = models.map((m, i) => {
    let score = 0;
    for (const t of m.tags) if (t && text.includes(t)) score += 3;
    const tl = typeLabel.get(m.type);
    if (tl && text.includes(tl)) score += 2;
    for (const w of m.labelJa.split(/[（）、 ・×]/)) if (w.length >= 2 && text.includes(w)) score += 1;
    const staple = STAPLES.indexOf(m.id);
    return { m, score, staple: staple < 0 ? 999 : staple, i };
  });
  const picked: PartModelInfo[] = [];
  const perType = new Map<string, number>();
  const take = (x: { m: PartModelInfo }) => {
    if (picked.length >= limit || picked.includes(x.m)) return;
    const n = perType.get(x.m.type) ?? 0;
    if (n >= PER_TYPE) return;
    perType.set(x.m.type, n + 1);
    picked.push(x.m);
  };
  scored.filter((x) => x.score > 0).sort((a, b) => b.score - a.score || a.m.priceJpy - b.m.priceJpy).forEach(take);
  scored.filter((x) => x.staple < 999).sort((a, b) => a.staple - b.staple).forEach(take);
  return picked;
}

const SPEC_TEXT: [string, (v: number) => string][] = [
  ["capacityWh", (v) => `${v} Wh`], ["voltageV", (v) => `${v} V`], ["powerW", (v) => `${v} W`], ["lumens", (v) => `${v} lm`],
  ["rangeM", (v) => `range ${v} m`], ["fovDeg", (v) => `view ${v}°`], ["db", (v) => `${v} dB at 1 m`], ["thrustN", (v) => `thrust ${v} N`],
  ["torqueNm", (v) => `${v} N·m`], ["forceN", (v) => `${v} N`], ["loadKg", (v) => `load ${v} kg`], ["strokeM", (v) => `stroke ${v} m`],
  ["flowLpm", (v) => `${v} L/min`], ["airflowM3h", (v) => `${v} m³/h`], ["volumeL", (v) => `${v} L`], ["inch", (v) => `${v} inch`],
];

/** 依頼の文に添える候補の一覧（1行1品番）。 */
export function candidatesText(models: PartModelInfo[]): string {
  if (!models.length) return "";
  const lines = models.map((m) => {
    const specs = SPEC_TEXT.filter(([k]) => m.specs[k] != null && m.specs[k] > 0).map(([k, f]) => f(m.specs[k]));
    if (m.ip >= 0) specs.push(`waterproof IPX${m.ip}`);
    return `- ${m.id} | ${m.type} | ${m.labelJa} | ${m.shape} [${m.size.join(", ")}] m | ${m.massKg} kg | about ${m.priceJpy} yen${specs.length ? " | " + specs.join(", ") : ""}`;
  });
  return `Part models you may use (optional). Put the id in part.model only when the model fits the part; then use that model's shape and scale:\n${lines.join("\n")}`;
}
