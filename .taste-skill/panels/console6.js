// IMPOSSIBLE LAB コンソール（6画面）。すべての数値は state に1か所だけ持つ。
const NS = "http://www.w3.org/2000/svg";
const svg = document.getElementById("c");
const SCREENS = ["shiji", "sekkei", "henshu", "jikken", "kekka", "hozon"];
const STAGES = ["指示", "設計", "編集", "実験", "結果", "保存"];
const CX = 800, CY = 372, M = 140; // 計器の中心と、1 m の長さ

const state = {
  screen: 2,
  name: "ハンズフリー雨よけ装置", rev: 2,
  request: "", listening: false,
  examples: [
    ["手を使わない傘", "雨と風", "両手がふさがらない雨よけが欲しい"],
    ["空を飛べる靴", "飛行デッキ", "空を飛んで移動できる靴が欲しい"],
    ["ついてくる運搬ロボット", "障害物コース", "荷物を載せて後ろからついてくるロボットが欲しい"],
    ["頭の上の日よけ", "真夏の日差し", "頭の上についてくる日よけが欲しい"],
    ["夜道の足元ライト", "足元の明るさ", "夜道で足元を照らすライトが欲しい"],
    ["背中を温めるヒーター", "冬のバス停", "冬のバス停で背中を温める道具が欲しい"],
    ["後ろを見守るリュック", "近づく自転車", "後ろから来る自転車を知らせるリュックが欲しい"],
    ["近づくと開くゴミ箱", "人が近づく", "近づくと自動で開くゴミ箱が欲しい"],
  ],
  values: [
    { key: "radius", label: "防ぐ半径", unit: " m", min: 0.20, max: 1.50, step: 0.05, v: 0.60, dec: 2 },
    { key: "height", label: "頭上からの高さ", unit: " m", min: 0.15, max: 0.80, step: 0.05, v: 0.35, dec: 2 },
    { key: "skirt", label: "側面カバーの長さ", unit: " m", min: 0.00, max: 0.60, step: 0.05, v: 0.00, dec: 2 },
    { key: "seal", label: "遮水の効き", unit: "", min: 0.10, max: 1.00, step: 0.05, v: 0.90, dec: 2 },
    { key: "steady", label: "揺れにくさ", unit: "", min: 0.00, max: 1.00, step: 0.05, v: 0.30, dec: 2 },
  ],
  selected: "skirt",
  proposal: { key: "skirt", to: 0.25, text: "側面カバーを伸ばし、横から吹き込む雨を防ぐ" },
  parts: [
    { id: "canopy", name: "キャノピー", role: "雨を受け止める屋根", shape: "円柱", material: "樹脂" },
    { id: "core", name: "本体", role: "頭の上に浮かんでついてくる", shape: "円柱", material: "樹脂" },
    { id: "eye", name: "センサー", role: "頭の位置を見つける", shape: "球", material: "樹脂" },
    { id: "cell", name: "電池", role: "本体を動かす", shape: "箱", material: "樹脂" },
  ],
  part: "canopy", look: "研究所ホワイト",
  needs: [
    ["手を使わずに雨を防ぐ", "実験で測る"],
    ["歩いても頭の上についてくる", "実験で測る"],
    ["横からの雨も防ぐ", "実験で測る"],
    ["蒸れない", "確かめられない"],
  ],
  build: ["要望を読む", "機能を選ぶ", "部品を設計", "あなたの確認", "組み立て"], buildAt: 3,
  plans: [
    ["横なぐりの雨", "横からの雨も防ぐ", "防護率、濡れた粒の数"],
    ["歩きながらの雨", "歩いても頭の上についてくる", "防護率、位置のずれ"],
    ["電池のもち", "通学の 30 分もつ", "消費エネルギー"],
  ],
  plan: 0, mode: "DREAM", rain: 30, wind: 4, dir: 0, seconds: 10,
  rate: 62.3, prevRate: 40.9, pass: 80,
  hits: { head: 0, upper: 109, lower: 185 },
  mass: 0.52, massLimit: 0.50,
  saved: [
    { name: "ハンズフリー雨よけ装置", tag: "防護", rev: 3, exp: "横なぐりの雨", metric: "防護率", v: 84.1, max: 100, pass: 80, unit: "%", date: "今日 14:20", kind: "canopy" },
    { name: "自動で開くゴミ箱", tag: "動きつき", rev: 2, exp: "近づいて離れる", metric: "期待の達成率", v: 78, max: 100, pass: 70, unit: "%", date: "11/22", kind: "box" },
    { name: "ついてくるキャリーバッグ", tag: "運搬", rev: 3, exp: null, date: "11/22", kind: "box" },
    { name: "近づくと点く足元灯", tag: "照明", rev: 1, exp: "近づいて離れる", metric: "期待の達成率", v: 100, max: 100, pass: 70, unit: "%", date: "11/18", kind: "pad" },
    { name: "バス停ぬくぬくヒーター", tag: "加温", rev: 2, exp: "冬のバス停", metric: "昇温", v: 6.2, max: 10, pass: 5, unit: "℃", date: "11/11", kind: "pad" },
    { name: "空を飛べる靴", tag: "移動", rev: 4, exp: "飛行デッキ", metric: "安定度", v: 60, max: 100, pass: 70, unit: "点", date: "11/09", kind: "pair" },
  ],
  savedTotal: 15, pick: 0,
  filters: [["すべて", 15], ["防護", 2], ["運搬", 4], ["移動", 2], ["照明", 2], ["加温", 1], ["動きつき", 3]], filter: 0,
};
const DIRS = ["右から", "右後ろから", "後ろから", "左後ろから", "左から", "左前から", "前から", "右前から"];
console.assert(state.hits.head + state.hits.upper + state.hits.lower === 294);

// ---------- 描く道具 ----------
const el = (name, attrs = {}, text) => { const e = document.createElementNS(NS, name); for (const k in attrs) if (attrs[k] !== "" && attrs[k] != null) e.setAttribute(k, attrs[k]); if (text != null) e.textContent = text; return e; };
const add = (name, attrs, text) => svg.appendChild(el(name, attrs, text));
const pol = (r, deg, cx = CX, cy = CY) => [cx + r * Math.cos(deg * Math.PI / 180), cy + r * Math.sin(deg * Math.PI / 180)];
const arc = (r, a0, a1) => { const [x0, y0] = pol(r, a0), [x1, y1] = pol(r, a1); return `M${x0},${y0} A${r},${r} 0 ${a1 - a0 > 180 ? 1 : 0} 1 ${x1},${y1}`; };
const T = (x, y, text, o = {}) => add("text", { x, y, class: (o.nu ? "nu" : "jp") + (o.halo ? " halo" : ""), "font-size": o.size || 18, "font-weight": o.w || 400, fill: o.fill || "var(--ink)", "text-anchor": o.anchor || "start" }, text);
const press = (node, fn) => { node.addEventListener("click", (e) => { e.stopPropagation(); fn(); }); node.addEventListener("keydown", (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); fn(); } }); };
const go = (i) => { state.screen = i; try { history.replaceState(null, "", "#" + SCREENS[i]); } catch (e) {} render(); };

function button({ x, y, w, h, label, kind = "", onClick, size = 20, aria }) {
  const b = el("g", { class: `btn hit ${kind}`, role: "button", tabindex: 0, "aria-label": aria || label });
  const sl = 12, pts = `${x + sl},${y} ${x + w},${y} ${x + w - sl},${y + h} ${x},${y + h}`;
  const fill = kind === "primary" ? "var(--cy)" : kind === "warm" ? "var(--or)" : kind === "on" ? "var(--cy-deep)" : "var(--plate-2)";
  const stroke = kind === "primary" || kind === "on" ? "var(--cy)" : kind === "warm" ? "var(--or)" : kind === "quiet" ? "var(--edge-hi)" : "var(--cy-dim)";
  b.appendChild(el("polygon", { class: "face", points: pts, fill, stroke, "stroke-width": 2, filter: kind === "primary" ? "url(#glow)" : "" }));
  b.appendChild(el("text", { x: x + w / 2, y: y + h / 2 + size * 0.36, "text-anchor": "middle", "font-size": size, "font-weight": 700, class: "jp", fill: kind === "primary" || kind === "warm" ? "#021012" : kind === "quiet" ? "var(--mut)" : "var(--ink)" }, label));
  if (onClick) press(b, onClick);
  svg.appendChild(b);
}
function tri({ x, y, dir, sel, onClick, aria }) {
  const b = el("g", { class: `hit ${sel ? "sel" : ""}`, role: "button", tabindex: 0, "aria-label": aria });
  b.appendChild(el("rect", { x, y, width: 48, height: 48, fill: "transparent" }));
  b.appendChild(el("polygon", { class: "tri", points: dir < 0 ? `${x + 36},${y + 8} ${x + 36},${y + 40} ${x + 10},${y + 24}` : `${x + 12},${y + 8} ${x + 12},${y + 40} ${x + 38},${y + 24}`, "stroke-linejoin": "round" }));
  press(b, onClick || (() => {}));
  svg.appendChild(b);
}
function plate(x, y, w, h, title, right) {
  add("rect", { x, y, width: w, height: h, rx: 4, fill: "url(#metal)", stroke: "var(--edge)", "stroke-width": 1 });
  add("rect", { x, y, width: w, height: h, rx: 4, fill: "url(#brush)" });
  add("line", { x1: x + 4, y1: y + 0.5, x2: x + w - 4, y2: y + 0.5, stroke: "var(--edge-hi)", "stroke-width": 1 });
  if (title) T(x + 20, y + 34, title, { size: 20, w: 700 });
  if (right) T(x + w - 20, y + 34, right, { size: 17, fill: "var(--mut)", anchor: "end", nu: true });
}
const LP = { x: 28, w: 454 }, RP = { x: 1118, w: 454 }; // 左右の板
function row(x, y, w, h, { title, sub, right, rightFill, sel, onClick, aria, size = 19, ty }) {
  const g = el("g", { class: "rowhit" + (onClick ? " hit" : ""), role: onClick ? "button" : "", tabindex: onClick ? 0 : "", "aria-label": aria || title });
  g.appendChild(el("rect", { class: "row-bg", x: x + 8, y, width: w - 16, height: h, rx: 3 }));
  if (sel) g.appendChild(el("rect", { x: x + 8.5, y: y + 0.5, width: w - 17, height: h - 1, rx: 3, fill: "rgba(255,106,38,0.07)", stroke: "var(--or)", "stroke-width": 1.5 }));
  if (onClick) press(g, onClick);
  svg.appendChild(g);
  T(x + 24, y + (ty || (sub ? 27 : h / 2 + 7)), title, { size, w: sel ? 700 : 400, fill: sel ? "var(--or)" : "var(--ink)" });
  if (sub) T(x + 24, y + 50, sub, { size: 15, fill: "var(--mut)" });
  if (right) T(x + w - 24, y + (sub ? 27 : h / 2 + 7), right, { size: 16, fill: rightFill || "var(--mut)", anchor: "end", nu: true });
}

// ---------- 計器 ----------
function dial({ rings = true, dirs = true, orbit = true } = {}) {
  add("circle", { cx: CX, cy: CY, r: 304, fill: "none", stroke: "#0b1013", "stroke-width": 10 });
  add("circle", { cx: CX, cy: CY, r: 309, fill: "none", stroke: "var(--edge-hi)", "stroke-width": 1 });
  add("circle", { cx: CX, cy: CY, r: 296, fill: "url(#dial)", stroke: "var(--edge)", "stroke-width": 1.5 });
  add("circle", { cx: CX, cy: CY, r: 296, fill: "url(#brush)" });
  if (rings) for (const m of [0.5, 1.0, 1.5]) {
    add("circle", { cx: CX, cy: CY, r: m * M, fill: "none", stroke: "var(--cy-deep)", "stroke-width": 1.5 });
    if (m === 0.5) continue;
    const [lx, ly] = pol(m * M, 58); T(lx + 8, ly + 4, `${m.toFixed(1)} m`, { nu: true, size: 15, fill: "var(--mut)", halo: true });
  }
  for (let a = 0; a < 360; a += 5) { const long = a % 30 === 0, [x0, y0] = pol(228, a), [x1, y1] = pol(long ? 214 : 221, a); add("line", { x1: x0, y1: y0, x2: x1, y2: y1, stroke: long ? "var(--cy)" : "var(--cy-dim)", "stroke-width": long ? 2 : 1.5 }); }
  if (orbit) add("circle", { cx: CX, cy: CY, r: 236, fill: "none", stroke: "var(--cy-dim)", "stroke-width": 2, "stroke-dasharray": "2 10", class: "orbit" });
  if (dirs) for (const [a, name] of [[270, "前"], [0, "右"], [90, "後"], [180, "左"]]) { const [x, y] = pol(247, a); T(x, y + 6, name, { size: 15, fill: "var(--mut)", anchor: "middle" }); }
}
const ang = (f) => 150 + 240 * f;
function gauge({ v, max = 100, pass, prev, prevLabel, unit = "%" }) {
  add("path", { d: arc(270, 150, 390), fill: "none", stroke: "var(--cy-deep)", "stroke-width": 16 });
  if (v != null && v > 0) add("path", { d: arc(270, 150, ang(v / max)), fill: "none", stroke: "var(--cy)", "stroke-width": 16, filter: "url(#glow)" });
  for (let k = 0; k <= 10; k++) { const [x0, y0] = pol(282, ang(k / 10)), [x1, y1] = pol(290, ang(k / 10)); add("line", { x1: x0, y1: y0, x2: x1, y2: y1, stroke: "var(--mut)", "stroke-width": 1.5 }); }
  if (pass != null) { const a = ang(pass / max), [x0, y0] = pol(240, a), [x1, y1] = pol(296, a); add("line", { x1: x0, y1: y0, x2: x1, y2: y1, stroke: "var(--ink)", "stroke-width": 4 }); const [lx, ly] = pol(206, a - 9); T(lx, ly, `合格 ${pass}${unit}`, { size: 17, w: 500, anchor: "end", halo: true }); }
  if (prev != null) { const a = ang(prev / max), [x0, y0] = pol(258, a), [x1, y1] = pol(284, a); add("line", { x1: x0, y1: y0, x2: x1, y2: y1, stroke: "var(--mut)", "stroke-width": 3 }); const [lx, ly] = pol(312, a); T(lx, ly, `${prevLabel} ${prev}`, { nu: true, size: 15, fill: "var(--mut)", anchor: "middle" }); }
  { const [x, y] = pol(270, 150); T(x - 16, y + 26, "0", { nu: true, size: 15, fill: "var(--mut)", anchor: "middle" }); const [x2, y2] = pol(270, 390); T(x2 + 16, y2 + 26, String(max), { nu: true, size: 15, fill: "var(--mut)", anchor: "middle" }); }
}
function bigNumber(text, unit, label, fill = "var(--cy)") {
  const w = text.length * 40;
  T(CX - 10, 688, text, { nu: true, size: 76, w: 600, fill, anchor: "middle", halo: true });
  T(CX - 10 + w / 2 + 10, 688, unit, { nu: true, size: 30, w: 500, fill });
  T(CX, 716, label, { size: 16, fill: "var(--mut)", anchor: "middle" });
}
function person() { add("ellipse", { cx: CX, cy: CY, rx: 0.22 * M, ry: 0.12 * M, fill: "var(--body)" }); add("circle", { cx: CX, cy: CY, r: 0.10 * M, fill: "var(--body-2)" }); }
function canopy(R, dashed) { add("circle", { cx: CX, cy: CY, r: R, fill: "rgba(47,227,230,0.08)", stroke: "var(--cy)", "stroke-width": 3, "stroke-dasharray": dashed ? "10 8" : "", filter: dashed ? "" : "url(#glow)" }); add("rect", { x: CX - 13, y: CY - 13, width: 26, height: 26, fill: "none", stroke: "var(--ink)", "stroke-width": 2, transform: `rotate(45 ${CX} ${CY})` }); }

// ---------- 枠（上・下） ----------
function frame(left, primary) {
  T(40, 46, "IMPOSSIBLE LAB", { nu: true, size: 22, w: 700 });
  if (state.screen > 0 && state.screen < 5) { T(248, 46, state.name, { size: 20, w: 500 }); T(248 + state.name.length * 20 + 14, 46, `Rev.${state.rev}`, { nu: true, size: 20, w: 600, fill: "var(--cy)" }); }
  add("circle", { cx: 1392, cy: 39, r: 6, fill: "var(--cy)", filter: "url(#glow)" });
  T(1408, 46, "PC 接続中", { size: 17, fill: "var(--mut)" });
  T(1560, 46, "AI 待機", { size: 17, fill: "var(--mut)", anchor: "end" });
  const sw = 104, sx = 800 - (sw * 6) / 2;
  STAGES.forEach((name, i) => {
    const x = sx + i * sw, now = i === state.screen, done = i < state.screen;
    const g = el("g", { class: "hit stage", role: "button", tabindex: 0, "aria-label": `${name}の画面へ` });
    g.appendChild(el("polygon", { points: `${x + 14},806 ${x + sw - 4},806 ${x + sw - 18},850 ${x},850`, fill: now ? "var(--cy)" : done ? "var(--cy-deep)" : "transparent", stroke: now ? "var(--cy)" : "var(--cy-dim)", "stroke-width": 1.5, filter: now ? "url(#glow)" : "" }));
    press(g, () => go(i));
    svg.appendChild(g);
    T(x + sw / 2 - 2, 835, name, { size: 18, w: now ? 700 : 400, fill: now ? "#021012" : done ? "var(--ink)" : "var(--mut)", anchor: "middle" }).setAttribute("pointer-events", "none");
  });
  let x = 40;
  for (const b of left) { button({ x, y: 800, w: b.w || 132, h: 56, label: b.label, kind: b.kind || "", onClick: b.onClick }); x += (b.w || 132) + 8; }
  if (primary) button({ x: 1560 - (primary.w || 260), y: 794, w: primary.w || 260, h: 68, label: primary.label, kind: primary.kind == null ? "primary" : primary.kind, size: primary.size || 24, onClick: primary.onClick });
}

// ---------- S01 指示 ----------
function shiji() {
  dial({ rings: true, dirs: false });
  // 外周：声の大きさ（話している間だけ動く）
  for (let k = 0; k < 60; k++) { const a = 150 + (240 / 59) * k, [x0, y0] = pol(262, a), [x1, y1] = pol(state.listening ? 284 : 270, a); add("line", { x1: x0, y1: y0, x2: x1, y2: y1, stroke: state.listening ? "var(--cy)" : "var(--cy-deep)", "stroke-width": 5, class: state.listening ? "meter" : "", style: `animation-delay:${(k * 37) % 600}ms` }); }
  add("circle", { cx: CX, cy: CY, r: 96, fill: "none", stroke: "var(--cy-dim)", "stroke-width": 2, "stroke-dasharray": "4 8" });
  if (state.listening) { T(CX, CY - 4, "聞いています", { size: 30, w: 700, anchor: "middle", fill: "var(--cy)", halo: true }); T(CX, CY + 30, "話し終えたら「止める」", { size: 16, fill: "var(--mut)", anchor: "middle", halo: true }); }
  else if (!state.request) { T(CX, CY - 4, "何を発明しますか", { size: 32, w: 700, anchor: "middle", halo: true }); T(CX, CY + 30, "話すか、右の例から選ぶ", { size: 16, fill: "var(--mut)", anchor: "middle", halo: true }); }
  else { const s = state.request, lines = s.length > 13 ? [s.slice(0, Math.ceil(s.length / 2)), s.slice(Math.ceil(s.length / 2))] : [s]; lines.forEach((ln, i) => T(CX, CY + (i - (lines.length - 1) / 2) * 40 + 10, ln, { size: 28, w: 700, anchor: "middle", fill: "var(--cy)", halo: true })); }
  T(CX, 690, state.listening ? "録音中" : state.request ? "送信できます" : "入力待ち", { size: 34, w: 700, anchor: "middle", fill: state.request || state.listening ? "var(--cy)" : "var(--mut)", halo: true });
  T(CX, 716, `${state.request.length} / 200 文字`, { nu: true, size: 16, fill: "var(--mut)", anchor: "middle" });

  plate(LP.x, 78, LP.w, 264, "作りたい道具");
  add("rect", { x: 48, y: 128, width: 414, height: 64, rx: 3, fill: "#04080a", stroke: "var(--cy)", "stroke-width": 1.5 });
  T(64, 168, state.request ? (state.request.length > 20 ? state.request.slice(0, 19) + "…" : state.request) : "ここに入力する", { size: 19, fill: state.request ? "var(--ink)" : "var(--mut)" });
  button({ x: 48, y: 206, w: 200, h: 56, label: state.listening ? "止める" : "話す", kind: state.listening ? "on" : "", onClick: () => { if (state.listening) { state.listening = false; state.request = state.examples[0][2]; } else state.listening = true; render(); } });
  button({ x: 256, y: 206, w: 206, h: 56, label: "消す", kind: "quiet", onClick: () => { state.request = ""; state.listening = false; render(); } });
  T(48, 300, "声は PC の中だけで文字にします", { size: 15, fill: "var(--mut)" });
  T(48, 324, "保存はしません", { size: 15, fill: "var(--mut)" });

  plate(LP.x, 354, LP.w, 394, "続きから");
  state.saved.slice(0, 5).forEach((s, i) => row(LP.x, 400 + i * 66, LP.w, 62, { title: s.name, sub: s.exp ? `${s.metric} ${s.v}${s.unit}` : "まだ実験していません", right: `Rev.${s.rev}`, onClick: () => { state.pick = i; go(5); } }));

  plate(RP.x, 78, RP.w, 670, "例から選ぶ", "発明 ／ 確かめる実験");
  state.examples.forEach((e, i) => row(RP.x, 124 + i * 76, RP.w, 72, { title: e[0], sub: e[1], sel: state.request === e[2], onClick: () => { state.request = e[2]; state.listening = false; render(); } }));

  frame([{ label: "保存した発明", w: 190, onClick: () => go(5) }], { label: "AI に設計させる", w: 300, kind: state.request ? "primary" : "", onClick: () => { if (state.request) go(1); } });
}

// ---------- S02 設計の確認 ----------
function sekkei() {
  dial();
  // 外周：設計の進み（5つの区画）
  state.build.forEach((name, i) => { const a0 = 150 + i * 48 + 1.5, a1 = 150 + (i + 1) * 48 - 1.5, done = i < state.buildAt, now = i === state.buildAt; add("path", { d: arc(270, a0, a1), fill: "none", stroke: done ? "var(--cy)" : now ? "var(--or)" : "var(--cy-deep)", "stroke-width": 16, class: now ? "ghost" : "", filter: done ? "url(#glow)" : "" }); });
  person();
  const R = state.values[0].v * M;
  canopy(R, true);
  add("circle", { cx: CX, cy: CY - 34, r: 6, fill: "none", stroke: "var(--cy)", "stroke-width": 2, "stroke-dasharray": "3 3" });
  add("rect", { x: CX - 20, y: CY + 22, width: 40, height: 16, fill: "none", stroke: "var(--cy)", "stroke-width": 2, "stroke-dasharray": "5 4" });
  const tags = [["キャノピー", pol(R, 215), pol(190, 215), "end"], ["センサー", [CX, CY - 40], pol(186, 300), "start"], ["本体", [CX + 14, CY], pol(180, 340), "start"], ["電池", [CX + 20, CY + 30], pol(190, 40), "start"]];
  for (const [name, [px, py], [lx, ly], anchor] of tags) { add("line", { x1: px, y1: py, x2: lx, y2: ly, stroke: "var(--mut)", "stroke-width": 1 }); add("circle", { cx: px, cy: py, r: 3, fill: "var(--ink)" }); T(lx + (anchor === "end" ? -8 : 8), ly + 6, name, { size: 17, anchor, halo: true }); }
  T(CX, 690, "確認を待っています", { size: 34, w: 700, anchor: "middle", fill: "var(--or)", halo: true });
  T(CX, 716, "点線は、まだ組み立てていない設計です", { size: 16, fill: "var(--mut)", anchor: "middle" });

  plate(LP.x, 78, LP.w, 468, "部品", `${state.parts.length} 点・推定 ${state.mass.toFixed(2)} kg`);
  state.parts.forEach((p, i) => row(LP.x, 124 + i * 78, LP.w, 72, { title: p.name, sub: p.role, right: `${p.shape}・${p.material}` }));
  T(48, 480, "依頼", { size: 15, fill: "var(--mut)" });
  T(48, 510, state.request || state.examples[0][2], { size: 18 });

  plate(LP.x, 558, LP.w, 190, "設計の進み");
  state.build.forEach((name, i) => { const y = 624 + i * 27, done = i < state.buildAt, now = i === state.buildAt; add("rect", { x: 50, y: y - 13, width: 14, height: 14, fill: done ? "var(--cy)" : "none", stroke: now ? "var(--or)" : done ? "var(--cy)" : "var(--cy-dim)", "stroke-width": 2 }); T(76, y, name, { size: 17, w: now ? 700 : 400, fill: now ? "var(--or)" : done ? "var(--ink)" : "var(--mut)" }); if (done) T(462, y, "済み", { size: 15, fill: "var(--mut)", anchor: "end" }); });

  plate(RP.x, 78, RP.w, 468, "要望をどう確かめるか");
  state.needs.forEach((n, i) => row(RP.x, 124 + i * 78, RP.w, 72, { title: n[0], sub: n[1] === "確かめられない" ? "蒸れは空気の流れで決まり、実験に入っていません" : "実験の計測値で合否を出します", right: n[1], rightFill: n[1] === "確かめられない" ? "var(--mut)" : "var(--cy)" }));

  plate(RP.x, 558, RP.w, 190, "検証");
  T(1138, 630, "AI が設計しました", { size: 18 });
  T(1138, 662, "アプリの検証を通過しています", { size: 18 });
  T(1138, 706, "部品・値はすべて許可された範囲内です", { size: 15, fill: "var(--mut)" });

  frame([{ label: "指示に戻る", kind: "quiet", onClick: () => go(0) }, { label: "作り直す" }], { label: "承認して組み立てる", w: 330, onClick: () => go(2) });
}

// ---------- S03 編集 ----------
function henshu() {
  dial();
  add("line", { x1: CX + 205, y1: CY, x2: CX + 150, y2: CY, stroke: "var(--ink)", "stroke-width": 2.5 });
  add("polygon", { points: `${CX + 138},${CY} ${CX + 152},${CY - 8} ${CX + 152},${CY + 8}`, fill: "var(--ink)" });
  T(CX + 205, CY - 14, `前回の風 ${state.wind} m/s`, { size: 15, anchor: "end", halo: true });
  person();
  const R = state.values[0].v * M;
  canopy(R);
  const [ex, ey] = pol(R, -138), [tx, ty] = pol(R + 16, -138);
  add("line", { x1: CX, y1: CY, x2: ex, y2: ey, stroke: "var(--cy)", "stroke-width": 1.5 });
  add("circle", { cx: ex, cy: ey, r: 4, fill: "var(--cy)" });
  T(tx - 4, ty, state.values[0].v.toFixed(2) + " m", { nu: true, size: 20, w: 600, fill: "var(--cy)", anchor: "end", halo: true });
  const pr = state.part === "canopy" ? R + 10 : 26;
  for (const a of [45, 135, 225, 315]) add("path", { d: arc(pr, a - 9, a + 9), fill: "none", stroke: "var(--or)", "stroke-width": 3 });
  gauge({ v: state.rate, pass: state.pass, prev: state.prevRate, prevLabel: "Rev.1" });
  bigNumber(state.rate.toFixed(1), "%", "前回の実験の防護率");

  plate(LP.x, 78, LP.w, 468);
  T(48, 112, "性能の値", { size: 20, w: 700 }); T(158, 112, "動きの手順", { size: 18, fill: "var(--mut)" });
  add("rect", { x: 48, y: 122, width: 80, height: 3, fill: "var(--cy)" });
  const sel = state.values.find((d) => d.key === state.selected);
  state.values.forEach((d, i) => {
    const y = 136 + i * 80, isSel = d.key === state.selected, col = isSel ? "var(--or)" : "var(--cy)";
    const g = el("g", { class: "rowhit hit", role: "button", tabindex: 0, "aria-label": `${d.label}を選ぶ` });
    g.appendChild(el("rect", { class: "row-bg", x: 36, y, width: 438, height: 76, rx: 3 }));
    if (isSel) g.appendChild(el("rect", { x: 36.5, y: y + 0.5, width: 437, height: 75, rx: 3, fill: "rgba(255,106,38,0.07)", stroke: "var(--or)", "stroke-width": 1.5 }));
    press(g, () => { state.selected = d.key; render(); });
    svg.appendChild(g);
    T(52, y + 27, d.label, { size: 18, w: isSel ? 700 : 400, fill: isSel ? "var(--or)" : "var(--ink)" });
    const prop = state.proposal && state.proposal.key === d.key ? state.proposal : null;
    T(460, y + 28, prop ? `${d.v.toFixed(d.dec)} → ${prop.to.toFixed(d.dec)}${d.unit}` : d.v.toFixed(d.dec) + d.unit, { nu: true, size: 24, w: 600, fill: col, anchor: "end" });
    const n = Math.round((d.max - d.min) / d.step), on = Math.round((d.v - d.min) / d.step), bx = 100, bw = 310, sw = bw / n, pOn = prop ? Math.round((prop.to - d.min) / d.step) : on;
    for (let k = 0; k < n; k++) { const x = bx + k * sw, filled = k < on, ghost = !filled && k < pOn; add("polygon", { points: `${x + 5},${y + 40} ${x + sw - 2},${y + 40} ${x + sw - 7},${y + 62} ${x},${y + 62}`, fill: filled ? col : "none", stroke: filled ? col : ghost ? "var(--or)" : "var(--cy-deep)", "stroke-width": ghost ? 2 : 1.5, class: ghost ? "ghost" : "", filter: filled && isSel ? "url(#glow)" : "" }); }
    const stepBy = (s) => { d.v = Math.min(d.max, Math.max(d.min, Math.round((d.v + s * d.step) * 100) / 100)); state.selected = d.key; render(); };
    tri({ x: 44, y: y + 27, dir: -1, sel: isSel, onClick: () => stepBy(-1), aria: `${d.label}を減らす` });
    tri({ x: 418, y: y + 27, dir: +1, sel: isSel, onClick: () => stepBy(+1), aria: `${d.label}を増やす` });
  });
  if (sel) { const y = 136 + state.values.indexOf(sel) * 80 + 38, t = sel.key === "radius" || sel.key === "skirt" ? [CX - R, CY] : sel.key === "seal" ? [CX - R / 2, CY + 10] : [CX - 20, CY]; add("polyline", { points: `474,${y} 520,${y} ${t[0] - 40},${t[1]} ${t[0]},${t[1]}`, fill: "none", stroke: "var(--or)", "stroke-width": 1.5 }); add("circle", { cx: t[0], cy: t[1], r: 5, fill: "var(--or)", filter: "url(#glow)" }); }

  plate(LP.x, 558, LP.w, 190, "AI に改良を頼む");
  add("rect", { x: 48, y: 606, width: 250, height: 52, rx: 3, fill: "#04080a", stroke: "var(--cy-dim)", "stroke-width": 1.5 });
  T(62, 639, "横からの雨も防いで", { size: 18 });
  button({ x: 306, y: 606, w: 78, h: 52, label: "話す", size: 18 });
  button({ x: 388, y: 606, w: 82, h: 52, label: "依頼", size: 18 });
  if (state.proposal) {
    T(48, 690, `案：${state.proposal.text}`, { size: 15, fill: "var(--or)" });
    button({ x: 48, y: 700, w: 120, h: 40, label: "やめる", kind: "quiet", size: 17, onClick: () => { state.proposal = null; render(); } });
    button({ x: 176, y: 700, w: 294, h: 40, label: "この案を使う", kind: "warm", size: 18, onClick: () => { const d = state.values.find((v) => v.key === state.proposal.key); d.v = state.proposal.to; state.selected = d.key; state.proposal = null; render(); } });
  } else T(48, 700, "値を変えたら、実験で確かめましょう", { size: 16, fill: "var(--mut)" });

  plate(RP.x, 78, RP.w, 468, "部品", `${state.parts.length} 点`);
  state.parts.forEach((p, i) => row(RP.x, 128 + i * 58, RP.w, 54, { title: p.name, right: `${p.shape}・${p.material}`, sel: p.id === state.part, onClick: () => { state.part = p.id; render(); } }));
  const cur = state.parts.find((p) => p.id === state.part);
  [["形", cur.shape], ["素材", cur.material], ["外観", state.look]].forEach(([k, v], i) => { const y = 372 + i * 56; T(1142, y + 32, k, { size: 17, fill: "var(--mut)" }); tri({ x: 1262, y: y + 4, dir: -1, aria: `${k}を前へ` }); T(1410, y + 32, v, { size: 19, w: 500, anchor: "middle" }); tri({ x: 1512, y: y + 4, dir: +1, aria: `${k}を次へ` }); });

  plate(RP.x, 558, RP.w, 190, "総質量（推定）");
  const over = state.mass > state.massLimit, mw = 414, mx = (v) => 1138 + (v / 0.75) * mw;
  T(1518, 602, state.mass.toFixed(2), { nu: true, size: 44, w: 600, fill: over ? "var(--or)" : "var(--cy)", anchor: "end" });
  T(1526, 602, "kg", { nu: true, size: 20, w: 500, fill: over ? "var(--or)" : "var(--cy)" });
  add("rect", { x: 1138, y: 624, width: mw, height: 14, fill: "var(--cy-deep)" });
  add("rect", { x: 1138, y: 624, width: mx(Math.min(state.mass, state.massLimit)) - 1138, height: 14, fill: "var(--cy)" });
  if (over) add("rect", { x: mx(state.massLimit), y: 624, width: mx(state.mass) - mx(state.massLimit), height: 14, fill: "var(--or)", filter: "url(#glow)" });
  add("line", { x1: mx(state.massLimit), y1: 616, x2: mx(state.massLimit), y2: 648, stroke: "var(--ink)", "stroke-width": 3 });
  T(mx(state.massLimit), 670, `頭に付ける目安 ${state.massLimit.toFixed(2)} kg`, { size: 15, fill: "var(--mut)", anchor: "middle" });
  T(1138, 716, over ? `目安を ${(state.mass - state.massLimit).toFixed(2)} kg 超えています` : "目安の範囲内です", { size: 17, fill: over ? "var(--or)" : "var(--ink)" });

  frame([{ label: "元に戻す", kind: "quiet" }, { label: "試用する" }, { label: "保存", w: 100, onClick: () => go(5) }], { label: "実験へ進む", kind: state.proposal ? "" : "primary", onClick: () => go(3) });
}

// ---------- S04 実験の条件 ----------
function jikken() {
  dial();
  // 雨（上から見た粒）：強さで数が変わる
  let seed = 7; const rnd = () => (seed = (seed * 1664525 + 1013904223) % 4294967296) / 4294967296;
  const R = state.values[0].v * M, n = Math.round(state.rain * 5);
  for (let i = 0; i < n; i++) { const r = Math.sqrt(rnd()) * 205, a = rnd() * 360, [x, y] = pol(r, a); if (r < R + 4) continue; add("circle", { cx: x, cy: y, r: 2.6, fill: "var(--cy)", opacity: 0.75, class: "drop", style: `animation-delay:${Math.round(rnd() * 1800)}ms` }); }
  person(); canopy(R);
  // 風：向きの印と、強さの数だけの流れの矢印
  const a = state.dir * 45, u = [-Math.cos(a * Math.PI / 180), -Math.sin(a * Math.PI / 180)], v = [-u[1], u[0]];
  const [mx0, my0] = pol(170, a);
  add("polygon", { points: [[0, 0], [34, -20], [34, 20]].map(([p, q]) => `${mx0 - u[0] * p + v[0] * q},${my0 - u[1] * p + v[1] * q}`).join(" "), fill: "var(--or)", filter: "url(#glow)" });
  if (state.wind > 0) for (const off of [-64, 64]) { const [sx, sy] = pol(204, a), x0 = sx + v[0] * off, y0 = sy + v[1] * off, len = 24 + state.wind * 9, x1 = x0 + u[0] * len, y1 = y0 + u[1] * len;
    add("line", { x1: x0, y1: y0, x2: x1, y2: y1, stroke: "var(--or)", "stroke-width": 2.5, "stroke-dasharray": "14 8", class: "flow" });
    add("polygon", { points: [[0, 0], [-14, -8], [-14, 8]].map(([p, q]) => `${x1 + u[0] * p + v[0] * q},${y1 + u[1] * p + v[1] * q}`).join(" "), fill: "var(--or)" }); }
  gauge({ v: state.rain, max: 100, unit: "" });
  bigNumber(String(state.rain), "mm/h", `雨の強さ ／ 風 ${state.wind} m/s ${DIRS[state.dir]}`);

  plate(LP.x, 78, LP.w, 468, "実験を選ぶ", "AI が用意した 3 案");
  state.plans.forEach((p, i) => { const y = 124 + i * 138, selp = i === state.plan;
    row(LP.x, y, LP.w, 130, { title: p[0], sel: selp, size: 21, ty: 36, onClick: () => { state.plan = i; render(); } });
    T(52, y + 74, "確かめる", { size: 14, fill: "var(--mut)" }); T(128, y + 74, p[1], { size: 16 });
    T(52, y + 104, "測る", { size: 14, fill: "var(--mut)" }); T(128, y + 104, p[2], { size: 16 }); });

  plate(LP.x, 558, LP.w, 190, "物理の決まり");
  button({ x: 48, y: 606, w: 204, h: 52, label: "DREAM", kind: state.mode === "DREAM" ? "on" : "", onClick: () => { state.mode = "DREAM"; render(); } });
  button({ x: 258, y: 606, w: 204, h: 52, label: "REALITY", kind: state.mode === "REALITY" ? "on" : "", onClick: () => { state.mode = "REALITY"; render(); } });
  T(48, 694, state.mode === "DREAM" ? "重さと電力の制限なしで試します" : "電池の容量と装置の重さが結果に入ります", { size: 17 });
  T(48, 722, state.mode === "DREAM" ? "まず形と大きさを決めるときに" : "現実で使えるかを見るときに", { size: 15, fill: "var(--mut)" });

  plate(RP.x, 78, RP.w, 468, "環境");
  const env = [["雨の強さ", `${state.rain} mm/h`, (s) => { state.rain = Math.min(100, Math.max(5, state.rain + s * 5)); }], ["風の強さ", `${state.wind} m/s`, (s) => { state.wind = Math.min(12, Math.max(0, state.wind + s)); }], ["風の向き", DIRS[state.dir], (s) => { state.dir = (state.dir + s + 8) % 8; }]];
  env.forEach(([k, val, fn], i) => { const y = 132 + i * 92; T(1142, y + 22, k, { size: 17, fill: "var(--mut)" }); tri({ x: 1134, y: y + 30, dir: -1, onClick: () => { fn(-1); render(); }, aria: `${k}を下げる` }); T(1345, y + 66, val, { nu: true, size: 30, w: 600, fill: "var(--cy)", anchor: "middle" }); tri({ x: 1512, y: y + 30, dir: +1, onClick: () => { fn(+1); render(); }, aria: `${k}を上げる` }); });
  T(1142, 430, "時間", { size: 17, fill: "var(--mut)" }); T(1345, 474, `${state.seconds} 秒`, { nu: true, size: 30, w: 600, anchor: "middle" }); T(1552, 474, "固定", { size: 15, fill: "var(--mut)", anchor: "end" });

  plate(RP.x, 558, RP.w, 190, "合格の基準");
  T(1138, 668, "防護率", { size: 22 }); T(1330, 676, String(state.pass), { nu: true, size: 64, w: 600, anchor: "middle" }); T(1378, 676, "% 以上", { size: 22 });
  T(1138, 722, "前回は 62.3% でした", { size: 15, fill: "var(--mut)" });

  frame([{ label: "編集に戻る", kind: "quiet", w: 150, onClick: () => go(2) }], { label: "実験を始める", w: 280, onClick: () => go(4) });
}

// ---------- S05 結果 ----------
function kekka() {
  dial({ rings: false, dirs: false, orbit: false });
  // 計器の中：横から見た図
  const S = 124, gx = CX + 30, gy = CY + 158, X = (x) => gx + x * S, Y = (y) => gy - y * S, slant = 0.577, cyTop = 1.95, rad = state.values[0].v;
  const dryR = (y, top = cyTop) => rad - slant * (top - y), dryL = (y) => -rad - slant * (cyTop - y);
  const g = el("g", { "clip-path": "url(#dialclip)" }); svg.appendChild(g);
  const ga = (name, attrs) => g.appendChild(el(name, attrs));
  ga("polygon", { points: [[-rad, cyTop], [rad, cyTop], [dryR(0), 0], [dryL(0), 0]].map(([x, y]) => `${X(x)},${Y(y)}`).join(" "), fill: "rgba(47,227,230,0.10)" });
  let seed = 2026; const rnd = () => (seed = (seed * 1664525 + 1013904223) % 4294967296) / 4294967296;
  for (let i = 0; i < 170; i++) { const x = -2.4 + rnd() * 4.6, y = 0.05 + rnd() * 3.3; if (y < cyTop && x > dryL(y) - 0.04 && x < dryR(y) + 0.04) continue; if (x > rad - 0.02 && y > 1.3 && y < 1.9) continue; ga("line", { x1: X(x), y1: Y(y), x2: X(x - slant * 0.16), y2: Y(y - 0.16), stroke: "var(--cy-dim)", "stroke-width": 1.5, "stroke-linecap": "round" }); }
  ga("line", { x1: CX - 230, y1: Y(0), x2: CX + 230, y2: Y(0), stroke: "var(--ink)", "stroke-width": 2 });
  ga("rect", { x: X(-0.14), y: Y(0.85), width: 0.28 * S, height: 0.85 * S, rx: 0.12 * S, fill: "var(--body)" });
  ga("rect", { x: X(-0.17), y: Y(1.40), width: 0.34 * S, height: 0.57 * S, rx: 0.14 * S, fill: "var(--body)" });
  ga("circle", { cx: X(0), cy: Y(1.50), r: 0.10 * S, fill: "var(--body-2)" });
  ga("rect", { x: X(-rad), y: Y(cyTop + 0.035), width: rad * 2 * S, height: 0.035 * S, fill: "var(--ink)" });
  ga("rect", { x: X(-0.11), y: Y(cyTop), width: 0.22 * S, height: 0.075 * S, fill: "var(--ink)" });
  let k = 0;
  for (const part of [{ key: "lower", y0: 0, y1: 0.85, half: 0.14 }, { key: "upper", y0: 0.85, y1: 1.40, half: 0.17 }]) { let placed = 0, guard = 0; while (placed < state.hits[part.key] && guard++ < 20000) { const y = part.y0 + rnd() * (part.y1 - part.y0), edge = Math.max(dryR(y), -part.half); if (edge >= part.half - 0.004) continue; ga("circle", { cx: X(edge + rnd() * (part.half - edge)), cy: Y(y), r: 2, fill: "var(--cy)", class: "dot", style: `animation-delay:${k++ * 3}ms` }); placed++; } }
  const top = cyTop - 0.25;
  ga("line", { x1: X(rad), y1: Y(cyTop), x2: X(rad), y2: Y(top), stroke: "var(--or)", "stroke-width": 4, filter: "url(#glow)" });
  ga("line", { x1: X(rad), y1: Y(top), x2: X(dryR(0, top)), y2: Y(0), stroke: "var(--or)", "stroke-width": 1.5, "stroke-dasharray": "6 6" });
  T(X(rad) + 12, Y(top) + 4, "案：側面カバー", { size: 15, fill: "var(--or)", w: 500, halo: true });
  T(X(rad) + 12, Y(top) + 26, "0.25 m 下げる", { size: 15, fill: "var(--or)", halo: true });
  add("line", { x1: CX + 150, y1: CY - 178, x2: CX + 96, y2: CY - 178, stroke: "var(--ink)", "stroke-width": 2.5 });
  add("polygon", { points: `${CX + 84},${CY - 178} ${CX + 98},${CY - 186} ${CX + 98},${CY - 170}`, fill: "var(--ink)" });
  T(CX + 150, CY - 196, `風 ${state.wind} m/s`, { size: 15, anchor: "end", halo: true });
  T(X(-1.02), Y(1.02), "雨が当たらない範囲", { size: 14, fill: "var(--mut)", anchor: "middle", halo: true });
  add("circle", { cx: CX, cy: CY, r: 228, fill: "none", stroke: "var(--cy-dim)", "stroke-width": 2 });
  gauge({ v: state.rate, pass: state.pass, prev: state.prevRate, prevLabel: "Rev.1" });
  bigNumber(state.rate.toFixed(1), "%", `防護率 ／ 合格まであと ${(state.pass - state.rate).toFixed(1)}`);

  const total = state.hits.head + state.hits.upper + state.hits.lower;
  plate(LP.x, 78, LP.w, 468, "雨が当たった場所", `${state.seconds} 秒で ${total} 粒`);
  [["頭", state.hits.head], ["上半身", state.hits.upper], ["下半身", state.hits.lower]].forEach(([name, n], i) => { const y = 136 + i * 132, pct = Math.round((n / total) * 100), col = n === 0 ? "var(--mut)" : "var(--cy)";
    T(52, y + 26, name, { size: 19 }); T(52, y + 84, String(n), { nu: true, size: 52, w: 600, fill: col }); T(52 + String(n).length * 29 + 10, y + 84, "粒", { size: 17, fill: "var(--mut)" }); T(460, y + 84, `${pct}%`, { nu: true, size: 28, w: 600, fill: col, anchor: "end" });
    for (let s = 0; s < 40; s++) { const x = 52 + s * 10.2, on = s < Math.round((n / total) * 40); add("polygon", { points: `${x + 4},${y + 98} ${x + 9},${y + 98} ${x + 5},${y + 114} ${x},${y + 114}`, fill: on ? "var(--cy)" : "none", stroke: on ? "var(--cy)" : "var(--cy-deep)", "stroke-width": 1.2 }); } });

  plate(LP.x, 558, LP.w, 190, "この実験の条件");
  [["雨", `${state.rain} mm/h`], ["風", `${state.wind} m/s ${DIRS[state.dir]}`], ["時間", `${state.seconds} 秒`], ["物理", state.mode]].forEach(([k2, v2], i) => { const x = 48 + (i % 2) * 214, y = 622 + Math.floor(i / 2) * 62; T(x, y, k2, { size: 15, fill: "var(--mut)" }); T(x, y + 30, v2, { nu: true, size: 22, w: 600 }); });

  plate(RP.x, 78, RP.w, 468, "次に変えるところ");
  ["濡れの 63% は下半身です。", "風で雨が斜めに入り、キャノピーの影が", "足元から外れています。"].forEach((ln, i) => T(1138, 150 + i * 30, ln, { size: 18 }));
  ["側面カバーを下げると、オレンジの点線まで", "雨を防げます。"].forEach((ln, i) => T(1138, 256 + i * 28, ln, { size: 16, fill: "var(--or)" }));
  button({ x: 1138, y: 330, w: 414, h: 56, label: "同じ条件でもう一度" });
  button({ x: 1138, y: 398, w: 414, h: 56, label: "REALITY で電力と重さも見る" });
  button({ x: 1138, y: 466, w: 414, h: 56, label: "保存して終わる", kind: "quiet", onClick: () => go(5) });

  plate(RP.x, 558, RP.w, 190, "前の版と比べる");
  T(1138, 640, "Rev.1", { nu: true, size: 17, fill: "var(--mut)" }); T(1138, 684, state.prevRate.toFixed(1), { nu: true, size: 40, w: 600, fill: "var(--mut)" });
  add("line", { x1: 1262, y1: 668, x2: 1318, y2: 668, stroke: "var(--ink)", "stroke-width": 2.5 }); add("polygon", { points: "1330,668 1316,660 1316,676", fill: "var(--ink)" });
  T(1352, 640, "Rev.2", { nu: true, size: 17, fill: "var(--cy)" }); T(1352, 684, state.rate.toFixed(1), { nu: true, size: 40, w: 600, fill: "var(--cy)" });
  T(1552, 684, `+${(state.rate - state.prevRate).toFixed(1)}`, { nu: true, size: 30, w: 600, anchor: "end" });
  T(1138, 724, "防ぐ半径を 0.45 m から 0.60 m に広げた分です", { size: 15, fill: "var(--mut)" });

  frame([{ label: "条件に戻る", kind: "quiet", w: 150, onClick: () => go(3) }], { label: "側面カバーを伸ばして再実験", w: 400, size: 22, onClick: () => { const d = state.values.find((q) => q.key === "skirt"); d.v = 0.25; state.proposal = null; state.selected = "skirt"; go(2); } });
}

// ---------- S06 保存 ----------
function hozon() {
  dial({ rings: true, dirs: false });
  const s = state.saved[state.pick];
  // 目盛りの上に、保存した発明の数だけ印を置く
  for (let i = 0; i < state.savedTotal; i++) { const [x, y] = pol(247, 270 + (360 / state.savedTotal) * i), now = i === state.pick; add("circle", { cx: x, cy: y, r: now ? 8 : 5, fill: now ? "var(--or)" : "var(--cy-dim)", filter: now ? "url(#glow)" : "" }); }
  if (s.kind === "canopy") { person(); canopy(0.6 * M); }
  else if (s.kind === "box") { add("rect", { x: CX - 36, y: CY - 36, width: 72, height: 72, rx: 6, fill: "rgba(47,227,230,0.08)", stroke: "var(--cy)", "stroke-width": 3, filter: "url(#glow)" }); add("line", { x1: CX - 36, y1: CY - 20, x2: CX + 36, y2: CY - 20, stroke: "var(--cy)", "stroke-width": 2 }); }
  else if (s.kind === "pair") { for (const dx of [-26, 26]) add("rect", { x: CX + dx - 16, y: CY - 40, width: 32, height: 80, rx: 14, fill: "rgba(47,227,230,0.08)", stroke: "var(--cy)", "stroke-width": 3, filter: "url(#glow)" }); }
  else { add("rect", { x: CX - 50, y: CY - 30, width: 100, height: 60, rx: 4, fill: "rgba(47,227,230,0.08)", stroke: "var(--cy)", "stroke-width": 3, filter: "url(#glow)" }); }
  T(CX, CY - 130, s.name, { size: 22, w: 700, anchor: "middle", halo: true });
  if (s.exp) { const ok = s.v >= s.pass; gauge({ v: s.v, max: s.max, pass: s.pass, unit: s.unit === "%" ? "%" : "" }); bigNumber(String(s.v), s.unit, `${s.metric} ／ ${ok ? "合格" : "要改良"}`, ok ? "var(--cy)" : "var(--or)"); }
  else { gauge({ v: null }); T(CX, 688, "未実験", { size: 44, w: 700, anchor: "middle", fill: "var(--mut)" }); T(CX, 716, "開いて、実験で確かめましょう", { size: 16, fill: "var(--mut)", anchor: "middle" }); }

  plate(LP.x, 78, LP.w, 468, "分類で絞り込む");
  state.filters.forEach((f, i) => row(LP.x, 124 + i * 58, LP.w, 54, { title: f[0], right: `${f[1]} 件`, sel: i === state.filter, onClick: () => { state.filter = i; render(); } }));

  plate(LP.x, 558, LP.w, 190, "保存しました");
  T(48, 632, `${state.name} Rev.3`, { size: 18 });
  T(48, 660, "実験の記録 3 回分も一緒に保存しています", { size: 15, fill: "var(--mut)" });
  button({ x: 48, y: 684, w: 414, h: 48, label: "新しい発明を始める", size: 18, onClick: () => { state.request = ""; go(0); } });

  plate(RP.x, 78, RP.w, 468, "保存した発明", `${state.savedTotal} 件のうち 1 〜 ${state.saved.length}`);
  state.saved.forEach((v, i) => row(RP.x, 124 + i * 68, RP.w, 64, { title: v.name, sub: v.exp ? `${v.metric} ${v.v}${v.unit}・${v.v >= v.pass ? "合格" : "要改良"}` : "まだ実験していません", right: `Rev.${v.rev}`, sel: i === state.pick, onClick: () => { state.pick = i; render(); } }));

  plate(RP.x, 558, RP.w, 190, "選んだ発明");
  [["分類", s.tag], ["最後の実験", s.exp || "なし"], ["更新", s.date]].forEach(([k2, v2], i) => { T(1138, 622 + i * 26, k2, { size: 15, fill: "var(--mut)" }); T(1260, 622 + i * 26, v2, { size: 17 }); });
  button({ x: 1138, y: 696, w: 200, h: 44, label: "複製", size: 18 });
  button({ x: 1346, y: 696, w: 206, h: 44, label: "削除", kind: "quiet", size: 18 });

  frame([{ label: "編集に戻る", kind: "quiet", w: 150, onClick: () => go(2) }], { label: "開く", w: 220, onClick: () => go(2) });
}

function render() {
  svg.replaceChildren();
  const defs = el("defs");
  defs.innerHTML = `
    <filter id="glow" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="4" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
    <linearGradient id="metal" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#0f161a"/><stop offset="1" stop-color="#070b0d"/></linearGradient>
    <pattern id="brush" width="6" height="3" patternUnits="userSpaceOnUse"><rect width="6" height="1" fill="#ffffff" opacity="0.022"/></pattern>
    <radialGradient id="dial" cx="50%" cy="45%" r="60%"><stop offset="0" stop-color="#0d1418"/><stop offset="1" stop-color="#030506"/></radialGradient>
    <clipPath id="dialclip"><circle cx="${CX}" cy="${CY}" r="227"/></clipPath>`;
  svg.appendChild(defs);
  add("rect", { width: 1600, height: 900, fill: "var(--bg)" });
  [shiji, sekkei, henshu, jikken, kekka, hozon][state.screen]();
}
const fromHash = () => { const i = SCREENS.indexOf(location.hash.replace("#", "")); if (i >= 0) state.screen = i; render(); };
window.addEventListener("hashchange", fromHash);
fromHash();
