// サイトの画面で共通に使う小さな道具：要素の作成・日付・日本語の折り返し。
// 文字はすべて textContent で入れる（公開データをそのまま HTML にしない）。

export const $ = (s) => document.querySelector(`[data-slot="${s}"]`);
export const el = (tag, cls, text) => { const e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; };

export function date(iso) {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "" : `${d.getFullYear()}年${d.getMonth() + 1}月${d.getDate()}日`;
}

// 数値と単位のあいだは折り返さない（間を折り返さない空白にする）
const UNIT = /(\d) (?=(%|m\/s|mm\/h|m\b|kg\b|W\b|G\b|lx\b|°|分|秒|点|回|件))/g;
export const tie = (t) => t.replace(UNIT, "$1 ");

// 日本語を文節に近いまとまりに分け、その切れ目にだけ折り返し位置（U+200B）を入れる。
// ひらがなだけの語（助詞・活用の語尾）と句読点は前の語に付け、開きかっこは次の語に付ける。
const seg = "Segmenter" in Intl ? new Intl.Segmenter("ja", { granularity: "word" }) : null;
const ATTACH = /^[ぁ-ゟー、。，．）」』！？：・]+$/;
export function phrases(text) {
  if (!seg) return [text];
  const out = [];
  for (const { segment } of seg.segment(text)) {
    const prev = out[out.length - 1];
    // 日付と数（2026年11月25日・0 件）は切らない：数字のあとの単位の字と、年・月のあとの数字は前に付ける
    const unitAfterNumber = /\d$/.test(prev ?? "") && /^[年月日時分秒件人回点個]/.test(segment);
    const numberAfterDate = (/[年月]$/.test(prev ?? "") && /^\d/.test(segment)) || /^[中化的性品]$/.test(segment);
    if (prev != null && (ATTACH.test(segment) || unitAfterNumber || numberAfterDate || /[（「『]$/.test(prev) || /^ /.test(segment) || / $/.test(prev))) out[out.length - 1] = prev + segment;
    else out.push(segment);
  }
  return out;
}

/** root の中の日本語の文に、文節の切れ目だけで折り返す印を入れる（数値の欄 .num と折り返さない欄 .nw は除く）。 */
export function phraseBreaks(root) {
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
    acceptNode: (n) => (n.parentElement.closest(".num, .nw, script, style, canvas") || !/[぀-ヿ一-鿿]/.test(n.data) || n.data.includes("​"))
      ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT,
  });
  const nodes = [];
  while (walker.nextNode()) nodes.push(walker.currentNode);
  for (const n of nodes) n.data = phrases(tie(n.data)).join("​");
}
