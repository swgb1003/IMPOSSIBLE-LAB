// JIDF学生文化デザイン賞2027 プロジェクトシート（A4横3枚）
const path = require("path");
const pptxgen = require("pptxgenjs");
const sharp = require("sharp");
const { applyTheme } = require("C:/Users/swgb1/.claude/skills/synced/58e40682-bfb1-4810-82e6-80ba910860fc_f8baa330-debc-49ca-b355-65c307d64ca1/pptx/scripts/apply_theme.js");

const SHOTS = "D:/IMPOSSIBLE_LAB/Logs/verify";
const OUT = process.argv[2] || "D:/IMPOSSIBLE_LAB/docs/contest/jidf2027/IMPOSSIBLE_LAB_project_sheet.pptx";

// アプリ（LabTheme）と同じ色
const THEME = {
  name: "IMPOSSIBLE LAB",
  headFontFace: "Noto Sans JP",
  bodyFontFace: "Noto Sans JP",
  colors: {
    dk1: "0F172A", lt1: "F8FAFC", dk2: "1E293B", lt2: "CBD5E1",
    accent1: "0EA5E9", accent2: "38BDF8", accent3: "F59E0B", accent4: "334155",
    accent5: "64748B", accent6: "EF4444", hlink: "38BDF8", folHlink: "94A3B8",
  },
};
const HEX = THEME.colors;

async function crop(file, left, top, width, height) {
  const buf = await sharp(path.join(SHOTS, file))
    .extract({ left, top, width, height })
    .jpeg({ quality: 86 })
    .toBuffer();
  return "image/jpeg;base64," + buf.toString("base64");
}

(async () => {
  const pres = new pptxgen();
  pres.defineLayout({ name: "A4L", width: 11.69, height: 8.27 });
  pres.layout = "A4L";
  pres.theme = { headFontFace: THEME.headFontFace, bodyFontFace: THEME.bodyFontFace };
  pres.title = "IMPOSSIBLE LAB プロジェクトシート";
  const C = pres.SchemeColor;
  const W = 11.69;

  pres.defineSlideMaster({
    title: "SHEET",
    background: { color: HEX.dk1 },
    objects: [
      { text: { text: "IMPOSSIBLE LAB", options: { x: 0.5, y: 0.3, w: 3.2, h: 0.32, fontSize: 14, bold: true, italic: true, color: C.background1, margin: 0 } } },
      { text: { text: "VR Invention Platform", options: { x: 0.5, y: 0.6, w: 3.2, h: 0.22, fontSize: 8, italic: true, color: C.background2, margin: 0 } } },
      { placeholder: { options: { name: "title", type: "title", x: 0.5, y: 0.98, w: 10.7, h: 0.66, fontSize: 28, bold: true, color: C.background1, margin: 0, valign: "middle", align: "left" }, text: "" } },
      { placeholder: { options: { name: "body", type: "body", x: 0.5, y: 1.64, w: 10.7, h: 0.4, fontSize: 13, color: C.background2, margin: 0, valign: "middle" }, text: "" } },
    ],
  });

  // ヘッダーのタブ（アプリの工程タブと同じ見た目）
  function tabs(slide, active) {
    const names = ["① コンセプト", "② 体験", "③ 展望"];
    const w = 1.25, g = 0.08, x0 = 11.19 - (3 * w + 2 * g);
    names.forEach((n, i) => {
      const on = i === active;
      slide.addText(n, {
        objectName: `tab-${i + 1}`, isTextBox: true,
        shape: pres.shapes.RECTANGLE, x: x0 + i * (w + g), y: 0.34, w, h: 0.36,
        fill: { color: on ? C.accent1 : C.text2 }, color: on ? C.background1 : C.background2,
        fontSize: 11, bold: on, align: "center", valign: "middle", margin: 0,
      });
    });
  }
  function card(slide, name, x, y, w, h) {
    slide.addShape(pres.shapes.ROUNDED_RECTANGLE, { objectName: name, x, y, w, h, rectRadius: 0.08, fill: { color: C.text2 }, line: { type: "none" } });
  }
  function heading(slide, text, x, y, w, color) {
    slide.addText(text, { objectName: "h-" + text.slice(0, 8), isTextBox: true, x, y, w, h: 0.36, fontSize: 14, bold: true, color: color || C.background1, margin: 0, valign: "middle" });
  }
  function numCircle(slide, n, x, y, d, color) {
    slide.addText(String(n), { objectName: `num-${n}-${x.toFixed(2)}`, isTextBox: true, shape: pres.shapes.OVAL, x, y, w: d, h: d, fill: { color: color || C.accent1 }, color: C.background1, fontSize: 12, bold: true, align: "center", valign: "middle", margin: 0 });
  }
  function arrow(slide, x1, y1, x2, y2, color) {
    slide.addShape(pres.shapes.LINE, { objectName: `arrow-${x1.toFixed(2)}-${y1.toFixed(2)}`, x: x1, y: y1, w: x2 - x1, h: y2 - y1, line: { color: color || C.accent2, width: 1.5, endArrowType: "triangle" } });
  }
  function dots(slide, x, y, cols, rows, gap, d, color, tag) {
    for (let r = 0; r < rows; r++) for (let c = 0; c < cols; c++)
      slide.addShape(pres.shapes.OVAL, { objectName: `dot-${tag}-${r}-${c}`, x: x + c * gap, y: y + r * gap, w: d, h: d, fill: { color: color || C.accent2 }, line: { type: "none" } });
  }

  const img = {
    hero: await crop("01_overview_lab.png", 96, 40, 1408, 792),
    s1: await crop("02_S01_invention_input.png", 270, 0, 1080, 608),
    s2: await crop("06_S02_building.png", 790, 0, 560, 315),
    s3: await crop("09_S05_running.png", 400, 150, 800, 450),
    s4: await crop("13_S05_result_compare.png", 840, 590, 400, 225),
    s5: await crop("19_S06_gallery.png", 380, 380, 600, 338),
  };

  // ================= 1枚目：コンセプト =================
  pres.addSection({ title: "コンセプト" });
  let s = pres.addSlide({ masterName: "SHEET", sectionTitle: "コンセプト" });
  tabs(s, 0);
  s.addText("誰もが「開発者」になれる世界へ", { placeholder: "title" });
  s.addText("言葉で頼むだけで、AIが設計し、VRの中で試作・実験できる「仮想試作室」", { placeholder: "body" });

  // 左：課題と解決
  card(s, "card-problem", 0.5, 2.25, 5.35, 5.52);
  heading(s, "これまで", 0.75, 2.42, 4.8, C.background2);
  dots(s, 0.8, 3.0, 3, 2, 0.24, 0.16, C.accent2, "a");
  arrow(s, 1.55, 3.2, 1.9, 3.2);
  ["知識", "設備", "費用"].forEach((t, i) => {
    s.addText(t, { objectName: `wall-${i}`, isTextBox: true, shape: pres.shapes.RECTANGLE, x: 1.95 + i * 0.67, y: 2.82, w: 0.6, h: 0.78, fill: { color: C.accent4 }, color: C.background1, fontSize: 12, bold: true, align: "center", valign: "middle", margin: 0 });
  });
  arrow(s, 3.93, 3.2, 4.28, 3.2, C.accent5);
  dots(s, 4.36, 3.12, 1, 1, 0.24, 0.16, C.accent5, "a2");
  s.addText("試作できるのは\n一部の人だけ", { objectName: "few", isTextBox: true, x: 4.6, y: 2.92, w: 1.2, h: 0.56, fontSize: 10.5, color: C.background2, margin: 0, valign: "middle" });
  s.addText("アイデアの多くは、試作の前に消えている", { objectName: "problem-line", isTextBox: true, x: 0.75, y: 3.78, w: 4.9, h: 0.36, fontSize: 13, bold: true, color: C.background1, margin: 0 });

  heading(s, "IMPOSSIBLE LAB なら", 0.75, 4.5, 4.8, C.accent2);
  dots(s, 0.8, 5.08, 3, 2, 0.24, 0.16, C.accent2, "b");
  arrow(s, 1.55, 5.28, 1.9, 5.28);
  s.addText("言葉で頼むだけ\nAIが設計・VRで実験", { objectName: "lab-box", isTextBox: true, shape: pres.shapes.ROUNDED_RECTANGLE, rectRadius: 0.06, x: 1.95, y: 4.9, w: 1.94, h: 0.78, fill: { color: C.accent1 }, color: C.background1, fontSize: 11, bold: true, align: "center", valign: "middle", margin: 0 });
  arrow(s, 3.93, 5.28, 4.28, 5.28);
  dots(s, 4.36, 5.08, 3, 2, 0.24, 0.16, C.accent2, "b2");
  s.addText("誰でも、思いついたその場で試作・実験できる", { objectName: "solution-line", isTextBox: true, x: 0.75, y: 5.86, w: 4.95, h: 0.36, fontSize: 13, bold: true, color: C.background1, margin: 0 });
  s.addText([
    { text: "対象　", options: { bold: true, color: C.accent2 } },
    { text: "技術・設備・費用がなく、試作をあきらめていた初心者" },
  ], { objectName: "target", isTextBox: true, x: 0.75, y: 6.85, w: 4.9, h: 0.6, fontSize: 11.5, color: C.background2, margin: 0, valign: "middle" });

  // 右：ラボの画像と3ステップ
  s.addImage({ data: img.hero, x: 6.1, y: 2.25, w: 5.09, h: 2.863, altText: "VRの中の発明ラボの全景" });
  s.addText("VRの中の発明ラボ（Meta Quest 3S）", { objectName: "hero-cap", isTextBox: true, x: 6.1, y: 5.15, w: 5.09, h: 0.28, fontSize: 10, color: C.background2, margin: 0 });
  const steps1 = [
    ["言葉で頼む", "「手を使わず雨を防ぐ装置を作って」"],
    ["AIが設計", "部品と動きを考え、組み立てる"],
    ["実験で確かめる", "雨の中で効果を数値で見る"],
  ];
  const cw = (5.09 - 2 * 0.15) / 3;
  steps1.forEach(([t, d], i) => {
    const x = 6.1 + i * (cw + 0.15);
    card(s, `step1-${i + 1}`, x, 5.6, cw, 2.17);
    numCircle(s, i + 1, x + 0.14, 5.74, 0.38);
    s.addText(t, { objectName: `step1-t-${i + 1}`, isTextBox: true, x: x + 0.14, y: 6.22, w: cw - 0.28, h: 0.36, fontSize: 13, bold: true, color: C.background1, margin: 0 });
    s.addText(d, { objectName: `step1-d-${i + 1}`, isTextBox: true, x: x + 0.14, y: 6.6, w: cw - 0.28, h: 0.95, fontSize: 10.5, color: C.background2, margin: 0, valign: "top" });
  });

  // ================= 2枚目：体験 =================
  pres.addSection({ title: "体験" });
  s = pres.addSlide({ masterName: "SHEET", sectionTitle: "体験" });
  tabs(s, 1);
  s.addText("言葉で頼み、AIが設計し、実験で確かめる", { placeholder: "title" });
  s.addText("例：「手を使わず雨を防ぐ装置を作って」と頼んだ場合の流れ", { placeholder: "body" });

  const steps2 = [
    ["言葉で頼む", img.s1, "作りたい道具を、言葉で入力するだけ（例から選ぶこともできる）"],
    ["AIが設計", img.s2, "AI（Claude）が部品と動き（頭上追従・雨よけ）を設計し、組み立てる"],
    ["雨の実験", img.s3, "雨量と風を設定し、体に当たった雨粒を数えて防護率を出す"],
    ["改良", img.s4, "「横からの雨も防いで」と頼むと改良案が出て、同じ条件で再実験"],
    ["保存", img.s5, "設計・改良の履歴・実験記録をギャラリーに保存"],
  ];
  steps2.forEach(([t, data, d], i) => {
    const x = 0.5 + i * 2.175;
    card(s, `step2-${i + 1}`, x, 2.2, 2.0, 2.8);
    numCircle(s, i + 1, x + 0.12, 2.32, 0.36);
    s.addText(t, { objectName: `step2-t-${i + 1}`, isTextBox: true, x: x + 0.56, y: 2.32, w: 1.36, h: 0.36, fontSize: 13, bold: true, color: C.background1, margin: 0, valign: "middle" });
    s.addImage({ data, x: x + 0.08, y: 2.8, w: 1.84, h: 1.035, altText: `${t}の画面` });
    s.addText(d, { objectName: `step2-d-${i + 1}`, isTextBox: true, x: x + 0.12, y: 3.95, w: 1.78, h: 0.98, fontSize: 10.5, color: C.background2, margin: 0, valign: "top" });
    if (i < 4) arrow(s, x + 2.01, 3.32, x + 2.165, 3.32);
  });

  // 左下：改良の効果
  card(s, "card-effect", 0.5, 5.25, 5.35, 2.52);
  heading(s, "改良の効果（同じ条件で再実験）", 0.72, 5.38, 4.9);
  s.addChart(pres.charts.BAR, [{ name: "防護率", labels: ["改良前", "改良後"], values: [40.9, 66.4] }], {
    x: 0.62, y: 5.8, w: 2.9, h: 1.65, barDir: "col", barGapWidthPct: 60,
    chartColors: [HEX.accent5, HEX.accent1],
    showValue: true, dataLabelPosition: "outEnd", dataLabelFormatCode: '0.0"%"', dataLabelColor: HEX.lt1, dataLabelFontSize: 11, dataLabelFontBold: true, dataLabelFontFace: "+mn-lt",
    showTitle: true, title: "防護率（高いほどよい）", titleColor: HEX.lt2, titleFontSize: 10, titleFontFace: "+mn-lt",
    catAxisLabelColor: HEX.lt2, catAxisLabelFontSize: 10, catAxisLabelFontFace: "+mn-lt", catAxisLineShow: false,
    valAxisHidden: true, valAxisMinVal: 0, valAxisMaxVal: 80, valGridLine: { style: "none" }, catGridLine: { style: "none" },
    showLegend: false,
  });
  s.addText("被濡れスコア", { objectName: "wet-label", isTextBox: true, x: 3.7, y: 5.95, w: 2.0, h: 0.3, fontSize: 11, color: C.background2, margin: 0 });
  s.addText([
    { text: "30.8", options: { color: C.accent5 } },
    { text: " → ", options: { color: C.background2, fontSize: 18 } },
    { text: "17.5", options: { color: C.accent2 } },
  ], { objectName: "wet-value", isTextBox: true, x: 3.7, y: 6.25, w: 2.05, h: 0.55, fontSize: 26, bold: true, margin: 0, valign: "middle" });
  s.addText("体に当たる雨粒の数／秒\n（低いほどよい）", { objectName: "wet-unit", isTextBox: true, x: 3.7, y: 6.8, w: 2.05, h: 0.5, fontSize: 10, color: C.background2, margin: 0, valign: "top" });
  s.addText("PC上の自動検証の値。雨量30mm/h・風速4m/s・横からの風", { objectName: "effect-note", isTextBox: true, x: 0.72, y: 7.42, w: 4.95, h: 0.28, fontSize: 10, color: C.background2, margin: 0 });

  // 右下：2つの実験モード
  card(s, "card-modes", 6.1, 5.25, 5.09, 2.52);
  heading(s, "2つの実験モード", 6.32, 5.38, 4.6);
  const th = (t, color) => ({ text: t, options: { bold: true, color, fill: { color: C.text1 }, align: "center" } });
  const td = (t, extra) => ({ text: t, options: Object.assign({ color: C.background1 }, extra || {}) });
  s.addTable([
    [th("", C.background2), th("DREAM", C.accent2), th("REALITY", C.accent3)],
    [td("目的", { color: C.background2, bold: true }), td("理想の性能を確かめる"), td("現実に作れるかを推定する")],
    [td("見るもの", { color: C.background2, bold: true }), td("雨の当たり方・防護率"), td("消費電力・稼働時間・風への強さ")],
    [td("結果の例", { color: C.background2, bold: true }), td("防護率 66.4%"), td("稼働 約6分（2〜16分）")],
  ], {
    objectName: "modes-table", x: 6.3, y: 5.82, w: 4.7, colW: [0.9, 1.75, 2.05], rowH: 0.42,
    fontSize: 10.5, fontFace: THEME.bodyFontFace, valign: "middle", margin: [0, 0.06, 0, 0.06],
    border: { type: "solid", pt: 0.75, color: HEX.accent4 }, fill: { color: C.text2 },
  });

  // ================= 3枚目：展望 =================
  pres.addSection({ title: "展望" });
  s = pres.addSlide({ masterName: "SHEET", sectionTitle: "展望" });
  tabs(s, 2);
  s.addText("仮想の試作から、現実の開発へ", { placeholder: "title" });
  s.addText("設計と実験記録を公開し、技術者・研究者・メーカーの実用化につなげる", { placeholder: "body" });

  const flow = [
    ["発想する", "誰でも、言葉で"],
    ["VRで試作・実験", "AIが設計"],
    ["記録を残す", "設計と実験記録"],
    ["公開する", "設計書として"],
    ["実用化", "技術者・研究者・メーカー"],
  ];
  flow.forEach(([t, d], i) => {
    const x = 0.5 + i * 2.19, now = i < 3;
    s.addShape(pres.shapes.ROUNDED_RECTANGLE, {
      objectName: `flow-${i + 1}`, x, y: 2.3, w: 1.95, h: 1.12, rectRadius: 0.08,
      fill: now ? { color: C.accent1, transparency: 72 } : { color: C.text1 },
      line: now ? { color: C.accent1, width: 1.5 } : { color: C.accent3, width: 1.5, dashType: "dash" },
    });
    s.addText(t, { objectName: `flow-t-${i + 1}`, isTextBox: true, x: x + 0.1, y: 2.42, w: 1.75, h: 0.46, fontSize: 14, bold: true, color: C.background1, align: "center", valign: "middle", margin: 0 });
    s.addText(d, { objectName: `flow-d-${i + 1}`, isTextBox: true, x: x + 0.1, y: 2.88, w: 1.75, h: 0.42, fontSize: 10.5, color: C.background2, align: "center", valign: "middle", margin: 0 });
    if (i < 4) arrow(s, x + 1.97, 2.86, x + 2.17, 2.86, now && i < 2 ? C.accent2 : C.accent3);
  });
  // 範囲の表示
  s.addShape(pres.shapes.LINE, { objectName: "range-now", x: 0.5, y: 3.62, w: 6.33, h: 0, line: { color: C.accent1, width: 2 } });
  s.addText("本作で実現", { objectName: "range-now-label", isTextBox: true, x: 0.5, y: 3.7, w: 6.33, h: 0.32, fontSize: 12, bold: true, color: C.accent2, align: "center", margin: 0 });
  s.addShape(pres.shapes.LINE, { objectName: "range-future", x: 7.07, y: 3.62, w: 4.14, h: 0, line: { color: C.accent3, width: 2, dashType: "dash" } });
  s.addText("将来の構想", { objectName: "range-future-label", isTextBox: true, x: 7.07, y: 3.7, w: 4.14, h: 0.32, fontSize: 12, bold: true, color: C.accent3, align: "center", margin: 0 });

  s.addText([
    { text: "開発に参加できる人を増やし、", options: { color: C.background1 } },
    { text: "日本の技術開発の発展へ", options: { color: C.accent2 } },
  ], { objectName: "vision", isTextBox: true, shape: pres.shapes.ROUNDED_RECTANGLE, rectRadius: 0.08, x: 0.5, y: 4.3, w: 10.7, h: 0.78, fill: { color: C.text2 }, fontSize: 20, bold: true, align: "center", valign: "middle", margin: 0 });

  function listCard(name, x, title, titleColor, mark, markColor, items) {
    card(s, name, x, 5.35, 5.22, 2.42);
    heading(s, title, x + 0.22, 5.48, 4.8, titleColor);
    items.forEach((t, i) => {
      const y = 5.98 + i * 0.58;
      s.addText(mark, { objectName: `${name}-mark-${i}`, isTextBox: true, shape: pres.shapes.OVAL, x: x + 0.22, y: y + 0.05, w: 0.32, h: 0.32, fill: { color: markColor }, color: C.background1, fontSize: 11, bold: true, align: "center", valign: "middle", margin: 0 });
      s.addText(t, { objectName: `${name}-item-${i}`, isTextBox: true, x: x + 0.66, y, w: 4.4, h: 0.44, fontSize: 11.5, color: C.background1, margin: 0, valign: "middle" });
    });
  }
  listCard("card-done", 0.5, "制作で確かめたこと", C.accent2, "✓", C.accent1, [
    "Meta Quest 3S の実機で動作する",
    "AIの設計は、アプリが検証してから組み立てる",
    "改良で防護率 40.9% → 66.4%（同じ条件の再実験）",
  ]);
  listCard("card-next", 5.98, "今後の課題", C.accent3, "!", C.accent3, [
    "REALITYの推定値を、実物の測定で検証する",
    "設計書（部品表・組立手順）を出力し、公開できる形にする",
    "体験会で、初心者が使いこなせるかを確かめる",
  ]);

  await pres.writeFile({ fileName: OUT });
  await applyTheme(OUT, THEME);
  console.log("wrote", OUT);
})().catch((e) => { console.error(e); process.exit(1); });
