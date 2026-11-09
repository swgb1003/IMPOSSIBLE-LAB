// Unity の LabCatalog から書き出した catalog.json を読む（部品・機能・パラメータ範囲・実験カタログの唯一の出典は C# 側）。
import { readFileSync } from "node:fs";

export interface PartTypeInfo { type: string; labelJa: string; minScale: number; maxScale: number }
export interface ParameterInfo { key: string; labelJa: string; unit: string; min: number; max: number; default: number }
export interface BehaviorInfo {
  type: string; labelJa: string; requiresTargetPart: boolean; allowedTargetTypes: string[]; parameters: ParameterInfo[];
  /** 簡易モデルの説明（5週目〜）。 */
  modelJa?: string;
}
export interface CategoryInfo { id: string; labelJa: string; experimentAvailable: boolean }
export interface NamedOption { id: string; labelJa: string }
export interface MaterialInfo { id: string; labelJa: string; densityKgM3: number }
export type AppearanceInfo = NamedOption;
export interface ScenarioInfo { id: string; labelJa: string; description: string; allowedModules: string[] }
export interface ModuleInfo { id: string; labelJa: string; params: ParameterInfo[] }
export interface MetricInfo {
  key: string; labelJa: string; unit: string; lowerIsBetter: boolean; realityOnly: boolean;
  scenarios: string[]; anyBlocks: string[]; modules: string[]; min: number; max: number;
  /** stand で測るときだけ追加で必要なモジュール（古い catalog.json には無い）。 */
  standModules?: string[];
}
export interface Catalog {
  schemaVersion?: number;
  partTypes: PartTypeInfo[];
  behaviors: BehaviorInfo[];
  categories: CategoryInfo[];
  /** 外観プリセット（色の組み合わせ）。古い catalog.json には無い。 */
  appearances?: AppearanceInfo[];
  tags: NamedOption[];
  mounts: NamedOption[];
  shapes: NamedOption[];
  materials: MaterialInfo[];
  functionStatuses: string[];
  experiments: { scenarios: ScenarioInfo[]; modules: ModuleInfo[]; metrics: MetricInfo[] };
  limits: {
    maxParts: number; maxBehaviors: number; positionLimitXZ: number; positionMinY: number; positionMaxY: number;
    maxNameLength: number; maxPurposeLength: number; maxRequestLength: number;
    maxTags: number; maxFunctionNeeds: number; maxNeedLength: number; maxNoteLength: number;
    maxPlans: number; maxModules: number; maxMetrics: number; maxCriteria: number; maxReasonLength: number;
    minDurationSec: number; maxDurationSec: number;
  };
}

export function loadCatalog(path: string): Catalog {
  const c = JSON.parse(readFileSync(path, "utf8")) as Catalog;
  if (!Array.isArray(c.partTypes) || c.partTypes.length === 0) throw new Error("catalog.json: partTypes がありません");
  if (!Array.isArray(c.behaviors) || c.behaviors.length === 0) throw new Error("catalog.json: behaviors がありません");
  if (!c.limits || typeof c.limits.maxParts !== "number") throw new Error("catalog.json: limits がありません");
  for (const k of ["tags", "mounts", "shapes", "materials", "functionStatuses"] as const)
    if (!Array.isArray(c[k]) || c[k].length === 0) throw new Error(`catalog.json: ${k} がありません（Unity で ExportProxyCatalog を実行してください）`);
  if (!c.experiments || !Array.isArray(c.experiments.scenarios) || !Array.isArray(c.experiments.metrics))
    throw new Error("catalog.json: experiments がありません（Unity で ExportProxyCatalog を実行してください）");
  return c;
}

/** 今の画面で実験場を選ぶのに使う、従来のタグ（カテゴリ）。 */
export function legacyCategories(c: Catalog): CategoryInfo[] {
  return c.categories.filter((x) => x.experimentAvailable);
}
