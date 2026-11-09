// 料金の目安（Claude API の公開価格、claude-opus-5-5：入力 $4・出力 $20 / 100万トークン、
// キャッシュ書き込み（5分）は入力の 1.25 倍、キャッシュ読み込み $0.20）。2026-11 時点。請求額そのものではない。
export interface Usage { input: number; output: number; cacheRead: number; cacheWrite: number }

const PRICE_PER_MTOK = { input: 4, output: 20, cacheWrite: 5, cacheRead: 0.2 };

export function estimateCostUsd(u: Usage): number {
  return (u.input * PRICE_PER_MTOK.input + u.output * PRICE_PER_MTOK.output +
    u.cacheWrite * PRICE_PER_MTOK.cacheWrite + u.cacheRead * PRICE_PER_MTOK.cacheRead) / 1_000_000;
}
