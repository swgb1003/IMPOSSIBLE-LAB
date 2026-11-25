// S05 の図をデータから描く。同じ数値は1か所（DATA）にだけ持つ。
const DATA = {
  rain: 30, wind: 4, seconds: 10, mode: "DREAM",
  radius: 0.60, above: 0.35, personHeight: 1.60,
  hits: { head: 0, upper: 109, lower: 185 },
  rate: 62.3, prevRate: 40.9, pass: 80,
  proposal: { skirt: 0.25 },
  slant: 0.577, // 雨が1m落ちる間に風下へ流される距離（m）
};
const total = DATA.hits.head + DATA.hits.upper + DATA.hits.lower;
console.assert(total === 294, "当たった粒の合計");
console.assert(Math.round((DATA.pass - DATA.rate) * 10) / 10 === 17.7, "合格までの差");
console.assert(Math.round((DATA.rate - DATA.prevRate) * 10) / 10 === 21.4, "前の版との差");

const S = 232, X0 = 1.95, YTOP = 2.32, W = 772 - 40, H = 590;
const X = (x) => (x + X0) * S * (W / ((X0 + 1.2) * S));
const K = W / ((X0 + 1.2) * S);            // 横幅に合わせた縮尺
const Y = (y) => (YTOP - y) * S * K + 8;
const L = (m) => m * S * K;
const canopyY = DATA.personHeight + DATA.above;          // 1.95 m
const dryRight = (y, top = canopyY) => DATA.radius - DATA.slant * (top - y);
const dryLeft = (y) => -DATA.radius - DATA.slant * (canopyY - y);
const body = [
  { key: "lower", y0: 0, y1: 0.85, half: 0.14 },
  { key: "upper", y0: 0.85, y1: 1.40, half: 0.17 },
];
let seed = 2026;
const rnd = () => (seed = (seed * 1664525 + 1013904223) % 4294967296) / 4294967296;
const el = (name, attrs, text) => {
  const e = document.createElementNS("http://www.w3.org/2000/svg", name);
  for (const k in attrs) e.setAttribute(k, attrs[k]);
  if (text != null) e.textContent = text;
  return e;
};

function drawSide(svg) {
  svg.setAttribute("viewBox", `0 0 ${W} ${H}`);
  const add = (name, attrs, text) => svg.appendChild(el(name, attrs, text));

  // 雨が当たらない範囲（キャノピーの影。風で風下へ流れる）
  add("polygon", { points: [[-DATA.radius, canopyY], [DATA.radius, canopyY], [dryRight(0), 0], [dryLeft(0), 0]].map(([x, y]) => `${X(x)},${Y(y)}`).join(" "), fill: "var(--zone)" });

  // 雨のすじ（影の外だけ）
  for (let i = 0; i < 150; i++) {
    const x = -X0 + rnd() * (X0 + 1.2), y = 0.1 + rnd() * 2.15;
    if (y < canopyY && x > dryLeft(y) - 0.03 && x < dryRight(y) + 0.03) continue;
    if (y > canopyY && y < canopyY + 0.08 && Math.abs(x) < DATA.radius) continue;
    if (x > DATA.radius - 0.02 && y > 1.33 && y < 1.86) continue; // 改良案の文字の場所
    if (x < -1.2 && y < 1.75) continue; // 場所ごとの数の欄
    add("line", { x1: X(x), y1: Y(y), x2: X(x - DATA.slant * 0.16), y2: Y(y - 0.16), stroke: "var(--line)", "stroke-width": 1.5, "stroke-linecap": "round" });
  }

  // 床と、1m の目盛り
  add("line", { x1: 0, y1: Y(0), x2: W, y2: Y(0), stroke: "var(--ink)", "stroke-width": 2 });
  add("line", { x1: X(0.55), y1: Y(-0.09), x2: X(1.05), y2: Y(-0.09), stroke: "var(--muted)", "stroke-width": 2 });
  add("text", { x: X(0.8), y: Y(-0.09) + 20, "text-anchor": "middle", "font-size": 13, class: "tm" }, "0.5 m");

  // 人型（アプリの人型と同じ、頭・胴・脚の3つの形）
  add("rect", { x: X(-0.14), y: Y(0.85), width: L(0.28), height: L(0.85), rx: L(0.12), fill: "var(--body-fill)" });
  add("rect", { x: X(-0.17), y: Y(1.40), width: L(0.34), height: L(0.57), rx: L(0.14), fill: "var(--body-fill)" });
  add("circle", { cx: X(0), cy: Y(1.50), r: L(0.10), fill: "var(--body-fill)" });

  // 装置：キャノピーと本体
  add("rect", { x: X(-DATA.radius), y: Y(canopyY + 0.035), width: L(DATA.radius * 2), height: L(0.035), fill: "var(--ink)" });
  add("rect", { x: X(-0.11), y: Y(canopyY), width: L(0.22), height: L(0.075), fill: "var(--ink)" });

  // 半径の寸法
  const dy = Y(canopyY + 0.17);
  add("line", { x1: X(0), y1: dy, x2: X(DATA.radius), y2: dy, stroke: "var(--ink)", "stroke-width": 1.5 });
  for (const x of [0, DATA.radius]) add("line", { x1: X(x), y1: dy - 6, x2: X(x), y2: dy + 6, stroke: "var(--ink)", "stroke-width": 1.5 });
  add("text", { x: X(DATA.radius / 2), y: dy - 10, "text-anchor": "middle", "font-size": 14, class: "t" }, `半径 ${DATA.radius.toFixed(2)} m`);

  // 風
  const wy = Y(2.2);
  add("line", { x1: X(1.1), y1: wy, x2: X(0.72), y2: wy, stroke: "var(--ink)", "stroke-width": 2.5 });
  add("path", { d: `M${X(0.72) - 2},${wy} l12,-7 v14 z`, fill: "var(--ink)" });
  add("text", { x: X(1.1), y: wy - 12, "text-anchor": "end", "font-size": 14, class: "t" }, `風 ${DATA.wind} m/s`);

  // 当たった粒（1粒 = 1点）。影の外に出ている面にだけ打つ
  let delay = 0;
  for (const part of body) {
    let placed = 0, guard = 0;
    while (placed < DATA.hits[part.key] && guard++ < 20000) {
      const y = part.y0 + rnd() * (part.y1 - part.y0);
      const edge = Math.max(dryRight(y), -part.half);
      if (edge >= part.half - 0.004) continue;
      const x = edge + rnd() * (part.half - edge);
      add("circle", { cx: X(x), cy: Y(y), r: 2.1, fill: "var(--data)", class: "dot", style: `animation-delay:${(delay++ * 3)}ms` });
      placed++;
    }
  }

  // 場所ごとの数（風下側に引き出す）
  const pct = (n) => Math.round((n / total) * 100);
  const rows = [
    ["頭", DATA.hits.head, 1.50, -0.10],
    ["上半身", DATA.hits.upper, 1.12, -0.17],
    ["下半身", DATA.hits.lower, 0.42, -0.14],
  ];
  for (const [name, n, y, edge] of rows) {
    add("line", { x1: X(-1.28), y1: Y(y), x2: X(edge) - 6, y2: Y(y), stroke: "var(--muted)", "stroke-width": 1 });
    add("text", { x: X(-1.9), y: Y(y) - 12, "font-size": 14, class: "tm" }, name);
    add("text", { x: X(-1.9), y: Y(y) + 22, "font-size": 38, class: "n", fill: n === 0 ? "var(--muted)" : "var(--ink)" }, String(n));
    add("text", { x: X(-1.9) + (String(n).length * 19 + 8), y: Y(y) + 22, "font-size": 14, class: "tm" }, `粒　${pct(n)}%`);
  }
  add("text", { x: X(-0.72), y: Y(1.72), "font-size": 13, class: "tm", "text-anchor": "middle" }, "雨が当たらない範囲");

  // 改良案：側面カバー（風上の縁から下げる）と、そのときの影の端
  const sk = DATA.proposal.skirt, top = canopyY - sk;
  add("line", { x1: X(DATA.radius), y1: Y(canopyY), x2: X(DATA.radius), y2: Y(top), stroke: "var(--accent)", "stroke-width": 4 });
  add("line", { x1: X(DATA.radius), y1: Y(top), x2: X(dryRight(0, top)), y2: Y(0), stroke: "var(--accent)", "stroke-width": 1.5, "stroke-dasharray": "6 6" });
  add("text", { x: X(DATA.radius) + 14, y: Y(top) + 4, "font-size": 14, class: "t", fill: "var(--accent)", "font-weight": 500 }, "案：側面カバー");
  add("text", { x: X(DATA.radius) + 14, y: Y(top) + 26, "font-size": 14, class: "t", fill: "var(--accent)" }, `${sk.toFixed(2)} m 下げる`);
  add("text", { x: X(DATA.radius) + 14, y: Y(top) + 50, "font-size": 13, class: "tm" }, "点線まで雨を防ぐ");
}

function drawGauge(svg) {
  const w = 424, h = 104, x = (v) => 4 + (v / 100) * (w - 8);
  svg.setAttribute("viewBox", `0 0 ${w} ${h}`);
  const add = (name, attrs, text) => svg.appendChild(el(name, attrs, text));
  add("rect", { x: x(0), y: 30, width: x(100) - x(0), height: 26, fill: "var(--track)" });
  add("rect", { x: x(0), y: 30, width: x(DATA.rate) - x(0), height: 26, fill: "var(--data)" });
  for (let v = 0; v <= 100; v += 10) add("line", { x1: x(v), y1: 62, x2: x(v), y2: v % 50 === 0 ? 74 : 69, stroke: "var(--muted)", "stroke-width": 1 });
  for (const v of [0, 50, 100]) add("text", { x: x(v), y: 92, "font-size": 14, class: "tm", "text-anchor": v === 0 ? "start" : v === 100 ? "end" : "middle" }, `${v}%`);
  // 前の版
  add("line", { x1: x(DATA.prevRate), y1: 24, x2: x(DATA.prevRate), y2: 62, stroke: "var(--prev)", "stroke-width": 3 });
  add("text", { x: x(DATA.prevRate), y: 16, "font-size": 14, class: "tm", "text-anchor": "middle" }, `Rev.1 ${DATA.prevRate}%`);
  // 合格の線と、あと何ポイントか
  add("line", { x1: x(DATA.pass), y1: 22, x2: x(DATA.pass), y2: 64, stroke: "var(--ink)", "stroke-width": 3 });
  add("text", { x: x(DATA.pass), y: 16, "font-size": 15, class: "t", "text-anchor": "middle", "font-weight": 500 }, `合格 ${DATA.pass}%`);
  add("text", { x: (x(DATA.rate) + x(DATA.pass)) / 2, y: 48, "font-size": 14, class: "t", "text-anchor": "middle" }, `あと ${(DATA.pass - DATA.rate).toFixed(1)}`);
}

drawSide(document.getElementById("side"));
drawGauge(document.getElementById("gauge"));
