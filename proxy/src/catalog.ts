// Unity の LabCatalog から書き出した catalog.json を読む（部品・機能・パラメータ範囲の唯一の出典は C# 側）。
import { readFileSync } from "node:fs";

export interface PartTypeInfo { type: string; labelJa: string; minScale: number; maxScale: number }
export interface ParameterInfo { key: string; labelJa: string; unit: string; min: number; max: number; default: number }
export interface BehaviorInfo {
  type: string; labelJa: string; requiresTargetPart: boolean; allowedTargetTypes: string[]; parameters: ParameterInfo[];
}
export interface CategoryInfo { id: string; labelJa: string; experimentAvailable: boolean }
export interface AppearanceInfo { id: string; labelJa: string }
export interface Catalog {
  partTypes: PartTypeInfo[];
  behaviors: BehaviorInfo[];
  categories: CategoryInfo[];
  /** 外観プリセット（色の組み合わせ）。古い catalog.json には無い。 */
  appearances?: AppearanceInfo[];
  limits: {
    maxParts: number; maxBehaviors: number; positionLimitXZ: number; positionMinY: number; positionMaxY: number;
    maxNameLength: number; maxPurposeLength: number; maxRequestLength: number;
  };
}

export function loadCatalog(path: string): Catalog {
  const c = JSON.parse(readFileSync(path, "utf8")) as Catalog;
  if (!Array.isArray(c.partTypes) || c.partTypes.length === 0) throw new Error("catalog.json: partTypes がありません");
  if (!Array.isArray(c.behaviors) || c.behaviors.length === 0) throw new Error("catalog.json: behaviors がありません");
  if (!Array.isArray(c.categories) || c.categories.length === 0) throw new Error("catalog.json: categories がありません");
  if (!c.limits || typeof c.limits.maxParts !== "number") throw new Error("catalog.json: limits がありません");
  return c;
}

/** この版で設計を受け付けるカテゴリ（実験場があるもの）。 */
export function supportedCategories(c: Catalog): CategoryInfo[] {
  return c.categories.filter((x) => x.experimentAvailable);
}
