// IMPOSSIBLE LAB 卒業発表（研究発表）— 雰囲気「上品」（夜の研究所）
// 実行：node build2.js <出力パス>
const fs = require("fs");
const path = require("path");
const pptxgen = require("pptxgenjs");
const JSZip = require(require.resolve("jszip", { paths: [require.resolve("pptxgenjs")] }));
const { applyTheme } = require(process.env.PPTX_SKILL + "/scripts/apply_theme.js");

const OUT = process.argv[2] || "deck.pptx";
const IMG = (f) => path.join(__dirname, "img2", f);
const HEAD = "Zen Old Mincho";
const BODY = "LINE Seed JP";

const THEME = {
  name: "IMPOSSIBLE LAB Night",
  headFontFace: HEAD,
  bodyFontFace: BODY,
  colors: {
    dk1: "0B0F17", // 夜の地
    lt1: "F5F6F7", // 明るい地・暗い地の文字
    dk2: "1C2433", // 暗い地の面
    lt2: "C8CED6", // 暗い地の補助の文字
    accent1: "E0A64B", // 金（発明台の床の光）
    accent2: "7DD3FC", // 明るい水色（暗い地の数値）
    accent3: "0369A1", // 濃い水色（明るい地の数値）
    accent4: "5B6574", // 明るい地の控えめな文字
    accent5: "8A94A3", // 暗い地の控えめな文字
    accent6: "9A6B1F", // 濃い金（明るい地の強調）
    hlink: "0369A1",
    folHlink: "0369A1",
  },
};
const HEX = THEME.colors;

const pres = new pptxgen();
pres.layout = "LAYOUT_WIDE"; // 13.333 × 7.5 in
pres.title = "IMPOSSIBLE LAB — 生成AIとVRによる機能付き仮想試作";
pres.theme = { headFontFace: HEAD, bodyFontFace: BODY };
const C = pres.SchemeColor;

const M = 0.75; // 左右の余白
const CW = 13.333 - 2 * M; // 本文の幅
const TITLE = { name: "title", type: "title", x: M, y: 0.55, w: CW, h: 0.95, fontSize: 32, bold: true, align: "left", valign: "top", margin: 0 };

// ---- レイアウト ----
pres.defineSlideMaster({
  title: "COVER",
  background: { color: C.text1 },
  objects: [
    { placeholder: { options: { name: "title", type: "title", x: M, y: 2.9, w: 7.6, h: 2.1, fontSize: 54, bold: true, color: C.background1, align: "left", valign: "bottom", margin: 0, lineSpacingMultiple: 1.1 }, text: "" } },
    { placeholder: { options: { name: "subtitle", type: "body", x: M, y: 5.25, w: 9.2, h: 0.8, fontSize: 15, color: C.background2, align: "left", valign: "top", margin: 0, lineSpacingMultiple: 1.4 }, text: "" } },
    { placeholder: { options: { name: "meta", type: "body", x: M, y: 6.3, w: 7.6, h: 0.4, fontSize: 12, color: C.accent5, align: "left", valign: "top", margin: 0 }, text: "" } },
  ],
});
pres.defineSlideMaster({
  title: "DARK",
  background: { color: C.text1 },
  objects: [{ placeholder: { options: Object.assign({}, TITLE, { color: C.background1 }), text: "" } }],
  slideNumber: { x: 12.1, y: 6.95, w: 0.5, h: 0.3, fontSize: 10, color: C.accent5, fontFace: BODY, align: "right" },
});
pres.defineSlideMaster({
  title: "LIGHT",
  background: { color: C.background1 },
  objects: [{ placeholder: { options: Object.assign({}, TITLE, { color: C.text1 }), text: "" } }],
  slideNumber: { x: 12.1, y: 6.95, w: 0.5, h: 0.3, fontSize: 10, color: C.accent4, fontFace: BODY, align: "right" },
});
pres.defineSlideMaster({
  title: "STATEMENT",
  background: { color: C.text1 },
  objects: [
    { placeholder: { options: { name: "label", type: "body", x: M, y: 1.25, w: CW, h: 0.4, fontSize: 14, color: C.accent1, align: "left", valign: "top", margin: 0 }, text: "" } },
    { placeholder: { options: { name: "title", type: "title", x: M, y: 1.8, w: 10.6, h: 2.6, fontSize: 34, bold: true, color: C.background1, align: "left", valign: "top", margin: 0, lineSpacingMultiple: 1.35 }, text: "" } },
  ],
  slideNumber: { x: 12.1, y: 6.95, w: 0.5, h: 0.3, fontSize: 10, color: C.accent5, fontFace: BODY, align: "right" },
});

const tb = (extra) => Object.assign({ isTextBox: true, margin: 0, valign: "top", align: "left" }, extra);
const rect = (s, x, y, w, h, color, name) => s.addShape(pres.shapes.RECTANGLE, { x, y, w, h, fill: { color }, line: { type: "none" }, objectName: name });
// 16:9 の画像を、指定の枠に切り抜いて置く
const img169 = (s, file, x, y, w, h, alt, name) => {
  const k = Math.max(w / 16, h / 9);
  s.addImage({ path: IMG(file), x, y, w: 16 * k, h: 9 * k, sizing: { type: "cover", w, h }, altText: alt, objectName: name });
};
// 大きな数字と説明
const stat = (s, x, y, w, num, label, dark, numSize) => {
  s.addText(num, tb({ x, y, w, h: 0.9, fontFace: HEAD, bold: true, fontSize: numSize || 40, color: dark ? C.accent1 : C.accent3, objectName: "数値 " + label }));
  s.addText(label, tb({ x, y: y + 0.92, w, h: 0.6, fontSize: 12, color: dark ? C.background2 : C.accent4, lineSpacingMultiple: 1.3, objectName: "説明 " + label }));
};

// ================= 1. 表紙 =================
pres.addSection({ title: "研究の背景と提案" });
{
  const s = pres.addSlide({ masterName: "COVER", sectionTitle: "研究の背景と提案" });
  s.addImage({ path: IMG("hero.jpg"), x: 0, y: 0, w: 13.333, h: 7.5, altText: "夜の中央研究室。光る発明台とロボットアーム（Blender でレンダーしたアプリの研究室）", objectName: "表紙の研究室" });
  s.addImage({ path: IMG("fade.png"), x: 0, y: 0, w: 9.03, h: 7.5, altText: "装飾：表紙の文字を読みやすくする暗いグラデーション", objectName: "文字の下の暗がり" });
  s.addText([{ text: "想像を、", options: { breakLine: true } }, { text: "体験できる発明に。" }], { placeholder: "title" });
  s.addText("IMPOSSIBLE LAB ― 生成AIとVRによる、機能付き仮想試作品の生成・試用・改良", { placeholder: "subtitle" });
  s.addText("[氏名]　[所属]　[発表日]", { placeholder: "meta" });
  s.addNotes("IMPOSSIBLE LAB の発表を始めます。言葉で頼むだけで、VR の研究室に動く試作品が組み上がり、実験して改良できるアプリです。背景はアプリの中の研究室です。");
}

// ================= 2. 背景 =================
{
  const s = pres.addSlide({ masterName: "DARK", sectionTitle: "研究の背景と提案" });
  s.addText("アイデアはあるのに、試せない", { placeholder: "title" });
  img169(s, "wide.jpg", 7.35, 0, 5.983, 7.5, "夜の研究室の正面。中央に発明台、奥に展示ケース", "研究室の正面");
  s.addText("試作品をつくるには、次の3つが要る。", tb({ x: M, y: 1.85, w: 6.0, h: 0.4, fontSize: 15, color: C.background2 }));
  const needs = [["設計", "形と仕組みを決める"], ["プログラム", "動きをつくる"], ["3D モデル", "見た目をつくる"]];
  needs.forEach((n, i) => {
    const y = 2.5 + i * 0.95;
    s.addText(n[0], tb({ x: M, y, w: 2.6, h: 0.7, fontFace: HEAD, bold: true, fontSize: 30, color: C.background1, objectName: n[0] }));
    s.addText(n[1], tb({ x: M + 2.7, y: y + 0.18, w: 3.4, h: 0.5, fontSize: 14, color: C.accent5, objectName: n[0] + "の説明" }));
  });
  s.addText("技能と費用の壁があり、生活の困りごとを持つ人が、自分のアイデアを試すのは難しい。", tb({ x: M, y: 5.55, w: 6.0, h: 0.9, fontSize: 15, color: C.accent1, lineSpacingMultiple: 1.45, objectName: "課題" }));
  s.addNotes("試作品をつくるには、設計、プログラム、3D モデルの技能と費用が要ります。そのため、困りごとを持っていても、自分のアイデアを形にして試すのは難しい。この壁を下げることが出発点です。");
}

// ================= 3. 研究課題 =================
{
  const s = pres.addSlide({ masterName: "STATEMENT", sectionTitle: "研究の背景と提案" });
  s.addText("研究課題", { placeholder: "label" });
  s.addText("生成AIとVRを組み合わせ、言葉から機能付きの仮想試作品を生成・試用・改良できれば、初心者のアイデアの具体化を支援できるか", { placeholder: "title" });
  s.addText("確かめること", tb({ x: M, y: 4.75, w: 4, h: 0.35, fontSize: 12, color: C.accent5 }));
  const pts = ["指示どおりの機能が組み上がるか", "実験で問題が見つかり、改良で良くなるか", "Quest 単体で快適に動くか", "初心者が使えるか（試用テストで確認）"];
  pts.forEach((p, i) => s.addText(p, tb({ x: M + (i % 2) * 5.6, y: 5.2 + Math.floor(i / 2) * 0.5, w: 5.4, h: 0.4, fontSize: 15, color: C.background1, objectName: "確かめること " + (i + 1) })));
  s.addNotes("研究課題はこれです。生成AIと VR を組み合わせて、言葉から機能付きの仮想試作品を作り、試し、改良できるようにしたとき、初心者のアイデアの具体化を支援できるか。確かめることは4つです。最後の、初心者が使えるかどうかは、これから試用テストで確かめます。");
}

// ================= 4. 体験の流れ =================
{
  const s = pres.addSlide({ masterName: "DARK", sectionTitle: "研究の背景と提案" });
  s.addText("言葉で頼み、VR で試し、数値で直す", { placeholder: "title" });
  const steps = [
    ["s01.jpg", "言葉で頼む", "クイック発明か、日本語の文で依頼", "発明指示の画面"],
    ["s02.jpg", "AI が設計する", "部品と機能を選び、検証できる要望を示す", "AI 設計の画面（機能対応表）"],
    ["s03.jpg", "組み上がり、試す", "発明台に組み上がった試作品を使う", "試作品を試す画面"],
    ["s05.jpg", "実験して直す", "同じ条件で比べ、言葉で改良", "改良前後を比べる実験結果の画面"],
  ];
  const gap = 0.25, w = (CW - 3 * gap) / 4, ih = 2.75, y0 = 1.75;
  steps.forEach((st, i) => {
    const x = M + i * (w + gap);
    img169(s, st[0], x, y0, w, ih, st[3], st[1] + "の画面");
    s.addText(String(i + 1), tb({ x, y: y0 + ih + 0.2, w: 0.5, h: 0.6, fontFace: HEAD, bold: true, fontSize: 28, color: C.accent1 }));
    s.addText(st[1], tb({ x: x + 0.5, y: y0 + ih + 0.3, w: w - 0.5, h: 0.45, fontSize: 16, bold: true, color: C.background1, objectName: st[1] }));
    s.addText(st[2], tb({ x, y: y0 + ih + 0.9, w, h: 0.75, fontSize: 12, color: C.background2, lineSpacingMultiple: 1.4, objectName: st[1] + "の説明" }));
  });
  s.addNotes("体験は4段階です。まず言葉で頼みます。AI が部品と機能を選んで設計し、どの要望を検証できるかも示します。承認すると研究室の発明台に試作品が組み上がり、実際に使ってみられます。最後に実験して、同じ条件で改良前と後を比べ、言葉で直します。画面はすべてアプリの実画面です。");
}

// ================= 5. 事例：雨具 =================
pres.addSection({ title: "しくみ" });
{
  const s = pres.addSlide({ masterName: "LIGHT", sectionTitle: "しくみ" });
  s.addText("「横からの雨も防いで」で、防護率が上がった", { placeholder: "title" });
  s.addText("手を使わない雨具：雨量 30 mm/h・風速 4 m/s・同じ乱数で比較", tb({ x: M, y: 1.6, w: 6, h: 0.4, fontSize: 13, color: C.accent4 }));
  s.addChart(pres.charts.BAR, [{ name: "防護率", labels: ["改良前（Rev.1）", "改良後（Rev.3）"], values: [40.9, 66.4] }], {
    x: M, y: 2.15, w: 6.2, h: 4.2, barDir: "col", chartColors: [HEX.accent3], barGapWidthPct: 70,
    showValue: true, dataLabelPosition: "outEnd", dataLabelFormatCode: '0.0"%"', dataLabelColor: HEX.dk1, dataLabelFontSize: 16, dataLabelFontFace: BODY, dataLabelFontBold: true,
    catAxisLabelColor: HEX.accent4, catAxisLabelFontFace: BODY, catAxisLabelFontSize: 13, valAxisHidden: true, valAxisMaxVal: 100, valAxisMinVal: 0,
    valGridLine: { style: "none" }, catGridLine: { style: "none" }, showLegend: false,
    showTitle: true, title: "防護率（DREAM、Editor での実験）", titleColor: HEX.accent4, titleFontFace: BODY, titleFontSize: 13, objectName: "防護率のグラフ",
  });
  const rx = 7.45, rw = 13.333 - M - rx;
  s.addText("改良でしたこと", tb({ x: rx, y: 2.15, w: rw, h: 0.35, fontSize: 12, color: C.accent4 }));
  s.addText("側面カバー 0.25 m を足し、半径を 0.8 m に広げた", tb({ x: rx, y: 2.5, w: rw, h: 0.75, fontSize: 16, bold: true, color: C.text1, lineSpacingMultiple: 1.35 }));
  s.addText("被濡れスコア", tb({ x: rx, y: 3.55, w: rw, h: 0.35, fontSize: 12, color: C.accent4 }));
  s.addText("30.8 → 17.5 粒/秒", tb({ x: rx, y: 3.9, w: rw, h: 0.7, fontFace: HEAD, bold: true, fontSize: 30, color: C.accent3 }));
  s.addText("Quest 3S 実機でも、同じ条件で 33% → 48% に上がった。目標の 80% にはまだ届かず、さらに改良が要る。", tb({ x: rx, y: 4.95, w: rw, h: 1.2, fontSize: 13, color: C.accent4, lineSpacingMultiple: 1.5 }));
  s.addNotes("代表的な例として、手を使わない雨具です。雨量 30 ミリ、風速 4 メートル、同じ乱数で比べました。「横からの雨も防いで」と頼むと、AI が側面カバーを足して半径を広げ、防護率が 40.9% から 66.4% に上がりました。Quest の実機でも 33% から 48% に上がっています。ただし目標の 80% にはまだ届かないので、もう一段の改良が要ります。");
}

// ================= 6. しくみ =================
{
  const s = pres.addSlide({ masterName: "DARK", sectionTitle: "しくみ" });
  s.addText("AI はデータだけを返し、アプリが確かめてから組み立てる", { placeholder: "title" });
  const boxes = [
    ["言葉", "利用者の依頼"],
    ["AI（Claude）", "PC のプロキシ経由"],
    ["発明 DNA", "部品・機能・値の JSON"],
    ["アプリで検証", "許可リスト・範囲・上限"],
    ["組み立て・実験", "Unity で動かし、計測"],
  ];
  const gap = 0.32, w = (CW - 4 * gap) / 5, y = 2.0, h = 1.55;
  boxes.forEach((b, i) => {
    const x = M + i * (w + gap);
    rect(s, x, y, w, h, i === 3 ? HEX.accent1 : HEX.dk2, b[0] + "の面");
    const dark = i !== 3;
    s.addText(b[0], tb({ x: x + 0.18, y: y + 0.25, w: w - 0.36, h: 0.5, fontSize: 15, bold: true, color: dark ? C.background1 : C.text1 }));
    s.addText(b[1], tb({ x: x + 0.18, y: y + 0.8, w: w - 0.36, h: 0.6, fontSize: 11, color: dark ? C.background2 : C.text1, lineSpacingMultiple: 1.3 }));
    if (i < boxes.length - 1) s.addShape(pres.shapes.ISOSCELES_TRIANGLE, { x: x + w + gap / 2 - 0.07, y: y + h / 2 - 0.09, w: 0.14, h: 0.18, rotate: 90, fill: { color: HEX.accent5 }, line: { type: "none" }, objectName: "矢印" });
  });
  const pts = [
    ["AI が書いたコードは実行しない", "AI の出力は JSON のデータとして受け取り、許可した部品・機能・値の範囲だけを通す。"],
    ["API キーは Quest に入れない", "AI への接続は PC のプロキシが受け持ち、キーはアプリに埋め込まない。"],
    ["通信できなくても止まらない", "AI に届かないときは、理由を示してオフラインのサンプル設計図で続けられる。"],
  ];
  pts.forEach((p, i) => {
    const x = M + i * (CW / 3);
    s.addText(p[0], tb({ x, y: 4.35, w: CW / 3 - 0.4, h: 0.45, fontSize: 15, bold: true, color: C.accent1 }));
    s.addText(p[1], tb({ x, y: 4.85, w: CW / 3 - 0.4, h: 1.3, fontSize: 12, color: C.background2, lineSpacingMultiple: 1.5 }));
  });
  s.addNotes("しくみの要点です。AI はコードではなく、発明 DNA という JSON のデータだけを返します。アプリはそれを許可リストと値の範囲で確かめてから組み立てます。AI が書いたコードは実行しません。API キーは Quest に入れず、PC のプロキシが接続を受け持ちます。通信できないときも、理由を示してオフラインのサンプルで続けられます。");
}

// ================= 7. 汎用化 =================
{
  const s = pres.addSlide({ masterName: "DARK", sectionTitle: "しくみ" });
  s.addText("部品と機能の組み合わせで、雨具以外も実験できる", { placeholder: "title" });
  img169(s, "detail.jpg", 0, 4.25, 13.333, 3.25, "光る発明台の寄り", "発明台");
  s.addImage({ path: IMG("fade_up.png"), x: 0, y: 4.25, w: 13.333, h: 3.25, rotate: 180, altText: "装飾：画像の上端をなじませる暗がり", objectName: "画像の上の暗がり" });
  const st = [["18", "機能ブロック（照明・加温・検知・走行など）"], ["21", "部品タイプ"], ["7", "実験の場面（雨・運ぶ・飛ぶ・落とす・坂など）"], ["12", "環境の条件"], ["34", "計測項目"]];
  const w = CW / 5;
  st.forEach((v, i) => stat(s, M + i * w, 1.85, w - 0.3, v[0], v[1], true, 54));
  s.addText("AI は、この一覧から部品と機能を選び、値を決めるだけ。動きの計算は、出典つきの簡易な式で決まったコードが行う。", tb({ x: M, y: 3.55, w: CW, h: 0.5, fontSize: 13, color: C.background2 }));
  s.addNotes("雨具だけでなく、部品と機能の組み合わせで、いろいろな発明を実験できるようにしました。機能ブロックは18種、部品タイプは21種、実験の場面は7種、環境の条件は12種、計測項目は34あります。AI はこの一覧から選んで値を決めるだけで、動きの計算は決まったコードが出典つきの簡易な式で行います。");
}

// ================= 8. DREAM / REALITY =================
{
  const s = pres.addSlide({ masterName: "DARK", sectionTitle: "しくみ" });
  s.addText("空想（DREAM）と現実（REALITY）を同じ条件で比べる", { placeholder: "title" });
  img169(s, "reality.jpg", 6.6, 1.7, 5.983, 4.55, "REALITY モードの実験結果の画面", "REALITY の結果");
  const rows = [
    ["防護率", "66.4%", "30.7%"],
    ["風による装置のずれ", "0 m", "0.47 m"],
    ["稼働時間（推定）", "―", "約 8 分"],
  ];
  s.addText("DREAM", tb({ x: 3.1, y: 1.8, w: 1.4, h: 0.35, fontSize: 12, color: C.accent5 }));
  s.addText("REALITY", tb({ x: 4.6, y: 1.8, w: 1.8, h: 0.35, fontSize: 12, color: C.accent1 }));
  rows.forEach((r, i) => {
    const y = 2.3 + i * 0.95;
    s.addText(r[0], tb({ x: M, y: y + 0.12, w: 2.3, h: 0.5, fontSize: 13, color: C.background2 }));
    s.addText(r[1], tb({ x: 3.1, y, w: 1.4, h: 0.7, fontFace: HEAD, bold: true, fontSize: 24, color: C.background1 }));
    s.addText(r[2], tb({ x: 4.6, y, w: 1.9, h: 0.8, fontFace: HEAD, bold: true, fontSize: 24, color: C.accent1 }));
    if (i === 2) s.addText("幅 3〜19 分", tb({ x: 4.6, y: y + 0.6, w: 1.9, h: 0.35, fontSize: 11, color: C.accent5 }));
  });
  s.addText("同じ雨具（Rev.3）。REALITY は、風・重さ・電池などの係数17件（値・幅・出典つき）で計算する限定モデル。現実の性能や安全性は保証しない。", tb({ x: M, y: 5.3, w: 5.6, h: 1.1, fontSize: 12, color: C.accent5, lineSpacingMultiple: 1.5 }));
  s.addNotes("同じ雨具を、空想の DREAM と、現実寄りの REALITY で比べます。REALITY では、風で装置が 0.47 メートル流され、防護率は 66.4% から 30.7% に下がります。電池での稼働は推定で約8分、幅は3分から19分です。REALITY は出典つきの係数による限定的なモデルで、現実の性能や安全性を保証するものではありません。その差が、現実にするための課題として見えるのが狙いです。");
}

// ================= 9. 評価①：AI 設計 =================
pres.addSection({ title: "評価" });
{
  const s = pres.addSlide({ masterName: "LIGHT", sectionTitle: "評価" });
  s.addText("日常の困りごと40件で、AI の設計を評価した", { placeholder: "title" });
  s.addChart(pres.charts.BAR, [{ name: "割合", labels: ["設計が検証を通過（38/38）", "断るべき依頼を断った（2/2）", "実験計画が検証を通過（83/91）", "「検証できない」要望（64/162）"], values: [100, 100, 91, 40] }], {
    x: M, y: 1.75, w: 7.4, h: 4.4, barDir: "bar", chartColors: [HEX.accent3], barGapWidthPct: 55,
    showValue: true, dataLabelPosition: "outEnd", dataLabelFormatCode: '0"%"', dataLabelColor: HEX.dk1, dataLabelFontSize: 14, dataLabelFontFace: BODY, dataLabelFontBold: true,
    catAxisLabelColor: HEX.dk1, catAxisLabelFontFace: BODY, catAxisLabelFontSize: 12, catAxisOrientation: "maxMin",
    valAxisHidden: true, valAxisMaxVal: 115, valAxisMinVal: 0, valGridLine: { style: "none" }, catGridLine: { style: "none" }, showLegend: false,
    objectName: "AI 設計の評価のグラフ",
  });
  const rx = 8.75, rw = 13.333 - M - rx;
  stat(s, rx, 1.8, rw, "23 秒", "1件の設計にかかった平均時間（最大 32 秒）", false, 36);
  stat(s, rx, 3.35, rw, "$0.052", "1件あたりの AI の料金（40件で $2.07）", false, 36);
  s.addText("通らなかった計画 8 案は、検証の穴が原因だった。直して再実行し、19/19 案が通過した。", tb({ x: rx, y: 4.95, w: rw, h: 1.2, fontSize: 12, color: C.accent4, lineSpacingMultiple: 1.5 }));
  s.addNotes("日常の困りごと40件で AI の設計を評価しました。設計してほしい38件はすべて検証を通り、盗撮など断るべき2件は2件とも断りました。実験計画は91案中83案が通り、通らなかった8案は検証の穴が原因だったので、直して再実行し19案すべてが通りました。一方、要望のうち40%は、防水や体感など、今のモデルでは検証できないものでした。1件あたり平均23秒、料金は約5セントです。人による合致度の評価はまだ行っていません。");
}

// ================= 10. 評価②：代表6件 =================
{
  const s = pres.addSlide({ masterName: "LIGHT", sectionTitle: "評価" });
  s.addText("代表6件で、実験と改良の流れを通した", { placeholder: "title" });
  const head = ["発明（分類）", "実験", "改良の依頼", "判定に使った計測", "改良前 → 後", "判定"];
  const rows = [
    ["日よけ（防護）", "真夏の昼の日陰", "日陰を広げて", "日陰率", "0.58 → 0.77", "不合格 → 合格"],
    ["足元ライト（照明）", "夜道の足元照度", "もっと明るくして", "足元の照度", "44 → 65 lx", "合格 → 合格"],
    ["背中ヒーター（加温）", "バス停で待つ", "もっと温かくして", "温度変化", "13.4 → 16.1 °C", "合格 → 合格"],
    ["見守りリュック（検知）", "後ろの自転車を検知", "もっと遠く広く気づいて", "検知率", "0.33 → 0.67", "不合格 → 不合格"],
    ["段ボール持ち上げ機", "棚へ上げる", "落とさないようにしっかりつかんで", "落とした回数", "0 → 0", "合格 → 合格"],
    ["スマホケース（衝撃吸収）", "ポケットから落とす", "つぶれ切らないように硬くして", "つぶれ", "15 → 12 mm", "不合格 → 合格"],
  ];
  const line = () => ({ type: "solid", pt: 0.75, color: "D5D9DF" });
  const none = { type: "none" };
  const cell = (t, o) => ({ text: t, options: Object.assign({ border: [none, none, line(), none] }, o || {}) });
  const data = [head.map((h) => cell(h, { bold: true, color: C.accent4, fontSize: 11 }))].concat(rows.map((r) => r.map((t, j) => {
    if (j === 5) return cell(t, { bold: true, color: t.endsWith("合格") && !t.endsWith("不合格") ? C.accent3 : C.accent6 });
    if (j === 4) return cell(t, { bold: true });
    return cell(t);
  })));
  s.addTable(data, { x: M, y: 1.75, w: CW, colW: [2.35, 2.0, 2.8, 1.5, 1.55, 1.633], fontSize: 12, color: C.text1, fontFace: BODY, margin: [0.1, 0.08, 0.1, 0], valign: "middle", objectName: "代表6件" });
  s.addText("6件すべてで、設計 → 実験 → 改良 → 再実験 → 保存・読み直しまで通った（Editor の自動検証）。検知は1回の改良では目安の 0.8 に届かず、2回目の改良が要る。", tb({ x: M, y: 5.95, w: CW, h: 0.8, fontSize: 12, color: C.accent4, lineSpacingMultiple: 1.5 }));
  s.addNotes("AI が設計した代表6件で、実験と改良の流れを通しました。6件すべてで、設計から実験、改良、再実験、保存と読み直しまで通っています。日よけとスマホケースは1回の改良で不合格から合格になりました。見守りリュックの検知率は 0.33 から 0.67 に上がりましたが、目安の 0.8 には届かず、2回目の改良が要ります。この6件の改良は、オフラインの改良ルールで行いました。");
}

// ================= 11. 評価③：実機 =================
{
  const s = pres.addSlide({ masterName: "DARK", sectionTitle: "評価" });
  s.addText("Quest 3S 単体で、72 fps を保てている", { placeholder: "title" });
  img169(s, "run.jpg", 7.6, 1.75, 4.983, 4.5, "雨の実験中の画面", "雨の実験");
  const w = 3.2;
  stat(s, M, 1.85, w, "72.0 fps", "同じ画面にいる間（研究室のモデル約12万三角形を入れた後も同じ）", true, 40);
  stat(s, M + 3.45, 1.85, w, "144 ms", "画面切り替えの最悪フレーム（対策前は 421 ms。文字の事前登録で短縮）", true, 40);
  stat(s, M, 3.85, w, "191 / 191", "単体テスト（EditMode）", true, 40);
  stat(s, M + 3.45, 3.85, w, "377 PASS", "Editor の自動通し検証（失敗 0）", true, 40);
  s.addText("実機の数値は 2026-11-11・11-15 の計測ログより。座位・立位の操作、日本語入力も実機で確認した。", tb({ x: M, y: 5.75, w: 6.6, h: 0.6, fontSize: 12, color: C.accent5, lineSpacingMultiple: 1.5 }));
  s.addNotes("Quest 3S 単体で、同じ画面にいる間は 72 fps を保てています。研究室のモデルを入れた後も同じでした。画面を切り替えた瞬間の引っかかりは、日本語の文字の事前登録で、最悪 421 ミリ秒から 144 ミリ秒に縮めました。単体テストは191件すべて合格、自動の通し検証は377項目すべて合格です。");
}

// ================= 12. 限界と今後 =================
pres.addSection({ title: "まとめ" });
{
  const s = pres.addSlide({ masterName: "DARK", sectionTitle: "まとめ" });
  s.addText("わかっている限界と、これから確かめること", { placeholder: "title" });
  const colW = (CW - 0.6) / 2;
  s.addText("限界", tb({ x: M, y: 1.75, w: colW, h: 0.4, fontSize: 14, bold: true, color: C.accent1 }));
  s.addText([
    { text: "要望の40%は検証できない（防水・耐久・体感など、動きでない性質）", options: { bullet: { indent: 14 }, breakLine: true, paraSpaceAfter: 10 } },
    { text: "REALITY は出典つき係数の簡易モデル。現実の性能・安全性は保証しない", options: { bullet: { indent: 14 }, breakLine: true, paraSpaceAfter: 10 } },
    { text: "作れる動きは、登録した機能ブロックの範囲まで", options: { bullet: { indent: 14 } } },
  ], tb({ x: M, y: 2.25, w: colW, h: 3.2, fontSize: 14, color: C.background1, lineSpacingMultiple: 1.4 }));
  const x2 = M + colW + 0.6;
  s.addText("これから", tb({ x: x2, y: 1.75, w: colW, h: 0.4, fontSize: 14, bold: true, color: C.accent1 }));
  s.addText([
    { text: "初心者3〜5名の試用テスト（操作時間・詰まった箇所・楽しさ）— 未実施", options: { bullet: { indent: 14 }, breakLine: true, paraSpaceAfter: 10 } },
    { text: "AI の設計が依頼に合っているかの、人による評価 — 未実施", options: { bullet: { indent: 14 }, breakLine: true, paraSpaceAfter: 10 } },
    { text: "「〇〇したら△△する」動きを、許可した命令の組み合わせで作れるようにする", options: { bullet: { indent: 14 } } },
  ], tb({ x: x2, y: 2.25, w: colW, h: 3.2, fontSize: 14, color: C.background1, lineSpacingMultiple: 1.4 }));
  s.addNotes("わかっている限界は3つです。要望の4割は、防水や体感など動きでない性質で、検証できません。REALITY は簡易モデルで、安全性は保証しません。作れる動きは登録した機能の範囲までです。これから、初心者3〜5名の試用テストと、AI の設計が依頼に合っているかの人による評価を行います。どちらもまだ実施していません。また、「〇〇したら△△する」という動きを、許可した命令の組み合わせで作れるようにする拡張を予定しています。");
}

// ================= 13. 社会実装 =================
{
  const s = pres.addSlide({ masterName: "DARK", sectionTitle: "まとめ" });
  s.addText("試した証拠つきの発明を、作る人へ届ける", { placeholder: "title" });
  const steps = [
    ["教育ワークショップ", "学校・科学館で発明を生み出す（最初の一歩）"],
    ["企業の課題募集", "企業のテーマで発明を募る"],
    ["発明ライブラリ", "困りごと・実験結果・試した人数つきで公開"],
    ["製品化の成功報酬", "売上の一部を発案者と運営で分ける"],
  ];
  const w = (CW - 3 * 0.3) / 4;
  steps.forEach((st, i) => {
    const x = M + i * (w + 0.3), top = 3.3 - i * 0.45, bottom = 4.75;
    const gold = i >= 2;
    rect(s, x, top, w, bottom - top, gold ? HEX.accent1 : HEX.dk2, st[0] + "の段");
    s.addText(st[0], tb({ x: x + 0.2, y: top + 0.18, w: w - 0.4, h: 0.45, fontSize: 15, bold: true, color: gold ? C.text1 : C.background1, objectName: st[0] }));
    s.addText(st[1], tb({ x: x + 0.2, y: top + 0.65, w: w - 0.4, h: 0.75, fontSize: 12, color: gold ? C.text1 : C.background2, lineSpacingMultiple: 1.4, objectName: st[0] + "の説明" }));
  });
  s.addText("収益の式", tb({ x: M, y: 5.0, w: 3, h: 0.35, fontSize: 12, color: C.accent5 }));
  s.addText([{ text: "月額料金 × 契約社数", options: {} }, { text: "　　", options: {} }, { text: "製品の売上 × 料率", options: {} }], tb({ x: M, y: 5.35, w: CW, h: 0.65, fontFace: HEAD, bold: true, fontSize: 24, color: C.accent1 }));
  s.addText("運営は製造しない。在庫と製造のリスクを持たない（自社製造で在庫を抱えた Quirky は 2015 年に破綻）。", tb({ x: M, y: 6.1, w: CW, h: 0.5, fontSize: 12, color: C.background2 }));
  s.addNotes("社会実装の構想です。最終目標は、VR で試した証拠つきの発明を、作る人へ届けることです。まず教育ワークショップで発明を生み出す場をつくり、企業の課題募集を経て、発明ライブラリと製品化の成功報酬につなげます。収益は、ライブラリの月額料金と、製品化の売上の一部です。運営は製造せず、在庫を持ちません。");
}

// ================= 14. まとめ =================
{
  const s = pres.addSlide({ masterName: "DARK", sectionTitle: "まとめ" });
  s.addImage({ path: IMG("wide.jpg"), x: 0, y: 0, w: 13.333, h: 7.5, altText: "夜の研究室の正面", objectName: "研究室" });
  s.addImage({ path: IMG("fade_up.png"), x: 0, y: 3.2, w: 13.333, h: 4.3, altText: "装飾：文字を読みやすくする暗がり", objectName: "下の暗がり" });
  s.addShape(pres.shapes.RECTANGLE, { x: 0, y: 4.3, w: 13.333, h: 3.2, fill: { color: HEX.dk1, transparency: 18 }, line: { type: "none" }, objectName: "文字の下の面" });
  s.addImage({ path: IMG("fade_up.png"), x: 0, y: 0, w: 13.333, h: 2.0, rotate: 180, altText: "装飾：見出しを読みやすくする暗がり", objectName: "上の暗がり" });
  s.addText("まとめ", { placeholder: "title" });
  const pts = [
    "言葉で頼むと、検証済みの設計図から、動く試作品が VR に組み上がる",
    "同じ条件で実験して比べ、言葉で改良できる（雨具の防護率 40.9% → 66.4%）",
    "空想と現実の差を数値で示し、現実にするための課題を見せる",
  ];
  s.addText(pts.map((p, i) => ({ text: p, options: { bullet: { indent: 14 }, breakLine: i < pts.length - 1, paraSpaceAfter: 10 } })),
    tb({ x: M, y: 4.75, w: 11.0, h: 2.1, fontSize: 17, color: C.background1, lineSpacingMultiple: 1.35, objectName: "まとめの3点" }));
  s.addNotes("まとめです。言葉で頼むと、検証済みの設計図から動く試作品が VR に組み上がり、同じ条件で実験して言葉で改良できます。空想と現実の差を数値で示すことで、現実にするための課題も見えます。今後は試用テストで、初心者のアイデアの具体化を支援できるかを確かめます。ありがとうございました。");
}

(async () => {
  await pres.writeFile({ fileName: OUT });
  await applyTheme(OUT, THEME);
  const zip = await JSZip.loadAsync(fs.readFileSync(OUT));
  for (const f of Object.keys(zip.files).filter((n) => /^ppt\/theme\/theme\d+\.xml$/.test(n))) {
    let x = await zip.file(f).async("string");
    const fix = (block, face) => block
      .replace(/<a:ea typeface="[^"]*"\s*\/>/, `<a:ea typeface="${face}"/>`)
      .replace(/<a:font script="Jpan" typeface="[^"]*"\s*\/>/, `<a:font script="Jpan" typeface="${face}"/>`);
    x = x.replace(/<a:majorFont>[\s\S]*?<\/a:majorFont>/, (m) => fix(m, HEAD)).replace(/<a:minorFont>[\s\S]*?<\/a:minorFont>/, (m) => fix(m, BODY));
    zip.file(f, x);
  }
  const clean = new JSZip();
  for (const [n, e] of Object.entries(zip.files)) if (!e.dir) clean.file(n, await e.async("nodebuffer"), { createFolders: false });
  fs.writeFileSync(OUT, await clean.generateAsync({ type: "nodebuffer", compression: "DEFLATE" }));
  console.log("wrote", OUT);
})();
