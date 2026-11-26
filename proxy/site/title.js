// IMPOSSIBLE LIBRARY の入口。
// 最初の画面で試作品が部品キットの部品から組み上がり、スクロールすると画面に残ったまま
// 分かれて部品の名前を見せ（分解）、戻って回りながら実験の記録の横に立つ（実験）。
// その下は公開中の発明品の行（選んだ行の試作品が左に立つ）と、採用するまでの案内。
// 3D が使えないときは研究所で撮った写真を出す（文とリンクは 3D がなくても読める）。
import { $, el, phrases, phraseBreaks } from "./text.js";
import { mountAccount } from "./session.js";

mountAccount();

const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
// スマホとタブレットの縦向きは、試作品を上・文を下に並べる（title.css と同じ条件）
const NARROW = matchMedia("(max-width: 760px), (max-width: 1100px) and (orientation: portrait)");
const enc = encodeURIComponent;
const story = $("story");
const hero = $("hero");
const FEATURED = /アゲルくん/; // 入口に立てる試作品（部品が多く、形が分かりやすい）

// 上の帯の高さ（スマホでは折り返して高くなる）を最初の画面の高さから引く
const bar = $("bar");
new ResizeObserver(() => document.documentElement.style.setProperty("--barh", `${bar.offsetHeight}px`)).observe(bar);
requestAnimationFrame(() => hero.classList.add("in"));

// 各場面の文と写真は、画面に入った時に一度だけ下から上がる
const enter = new IntersectionObserver((entries) => {
  for (const e of entries) if (e.isIntersecting) { e.target.classList.add("in"); enter.unobserve(e.target); }
}, { rootMargin: "0px 0px -12% 0px" });
for (const n of document.querySelectorAll("[data-enter], .adopt-pic, .board > .t")) enter.observe(n);

const clamp = (v, a = 0, b = 1) => Math.min(b, Math.max(a, v));
const ease = (t) => 1 - Math.pow(1 - clamp(t), 3);
const smooth = (v, a, b) => { const t = clamp((v - a) / (b - a)); return t * t * (3 - 2 * t); };
const lerp = (a, b, t) => a + (b - a) * t;

// ---------------- データ（ライブラリー → 見本） ----------------
// 入口の紹介（プロンプト・部品・実験・改良の依頼）は、運営が作った見本1件の詳しい内容（showcase.json）で見せる。
// 公開された発明の依頼の原文と設計は、ログインした開発者だけが読めるため、入口には出さない。
async function load() {
  let items = null, live = false;
  if (!new URLSearchParams(location.search).has("sample")) {
    try {
      const r = await fetch("/library?sort=new");
      if (r.ok) {
        const d = await r.json();
        if (d.items?.length) {
          live = true;
          items = d.items.map((x) => ({ ...x, model: `/library/${enc(x.id)}/model.glb`, key: null, page: `/inventions/${enc(x.id)}` }));
        }
      }
    } catch { /* ライブラリーにつながらない → 見本 */ }
  }
  if (!items) items = (await (await fetch("/site/sample/list.json")).json()).items.map((x) => ({ ...x, model: `/site/sample/models/${enc(x.id)}.glb`, key: x.id }));
  let page = null;
  try {
    const r = await fetch("/site/sample/showcase.json");
    if (r.ok) page = await r.json();
  } catch { /* 紹介の見本が読めなくても、最初の画面と行は出す */ }
  const feat = items.find((x) => x.id === page?.id) ?? items.find((x) => FEATURED.test(x.name)) ?? items[0];
  if (page && page.id !== feat.id) page = null; // 立てる試作品と紹介の記録は同じ発明に限る
  return { items, feat, page, sample: !live };
}

// 区切りは全角の空白でなく余白で取る（行末の全角空白が枠の外へはみ出さないように）
const sub = (x) => { const s = el("span", "shelf-sub"); s.append(el("span", "sep", x.name), el("span", "sep", `Rev.${x.revision}`), resultText(x)); return s; };
const resultText = (x) => {
  const r = x.result;
  if (!r) return "実験の記録なし";
  const name = r.title.replace(/（.*?）/g, ""), pass = r.after.passed ? "合格" : "不合格";
  return /実験(場)?$/.test(name) ? `${name}で${pass}` : `${name}実験に${pass}`;
};
const metricNodes = (e) => e.metrics.filter((m) => m.primary).flatMap((m, i) => [...(i ? ["・"] : []), el("span", "nw", `${m.label} ${m.display} ${m.unit}`)]);

function fill({ items, feat, page, sample }) {
  const cap = $("cap");
  cap.href = feat.page;
  $("cap-name").textContent = feat.name;
  $("cap-meta").replaceChildren(el("span", "sep", `Rev.${feat.revision}`), resultText(feat));

  const inv = page?.invention;
  if (inv) {
    if (inv.designSource !== "ai") $("parts-h").textContent = "プロンプトを元に部品を組み立てる";
    if (inv.request) quoteLines($("request"), `「${inv.request}」`);
    if (inv.parts) $("part-list").textContent = `使った部品は${[...new Set(inv.parts.map((p) => p.label))].join("・")}です。`;
    facts(inv);
    const img = page.images?.[0];
    if (img) {
      $("adopt-img").src = img.url;
      $("adopt-img").alt = `IMPOSSIBLE LAB の発明台に置いた、${feat.name}`;
      $("adopt-cap").textContent = img.caption;
    }
  }

  // 公開中の発明品の行
  $("others-count").textContent = `${items.length} 件${sample ? "（すべて見本）" : ""}`;
  $("rows").replaceChildren(...items.map((x, i) => {
    const li = el("li", x.thumb ? "shelf-row" : "shelf-row nopic");
    li.dataset.i = i;
    const a = el("a");
    a.href = x.page;
    if (x.thumb) {
      const img = el("img");
      Object.assign(img, { src: x.thumb, alt: `IMPOSSIBLE LAB の発明台に置いた、${x.name}`, width: 132, height: 99, loading: "lazy" });
      a.append(img);
    }
    a.append(el("span", "shelf-req", x.summary), sub(x));
    li.append(a);
    return li;
  }));
  $("sample-note").textContent = sample ? "載っている発明品は見本です。IMPOSSIBLE LAB の通し確認で作った記録です。" : "";
  phraseBreaks(document.querySelector("main"));
  phraseBreaks(document.querySelector("footer"));
}

// 依頼の原文：2行に分かれるとき、助詞や読点の後のうち真ん中に近い所で分ける（スマホでは自然に折り返す）
function quoteLines(node, text) {
  const ps = phrases(text);
  let at = -1, best = Infinity, len = 0;
  const total = [...text].length;
  ps.forEach((p, i) => {
    len += [...p].length;
    if (i < ps.length - 1 && /[をにでがは、]$/.test(p) && Math.abs(len - total / 2) < best) { best = Math.abs(len - total / 2); at = i; }
  });
  if (at < 0) { node.textContent = text; return; }
  node.replaceChildren(ps.slice(0, at + 1).join(""), el("br", "br-wide"), ps.slice(at + 1).join(""));
}

// 実験の場面：最初の実験、改良の依頼と変えた値、その後の実験を順に（多いときは最後の5つ）
function facts(inv) {
  const ex = [...(inv.experiments ?? [])].sort((a, b) => a.revision - b.revision);
  const first = ex[0];
  if (!first) return;
  $("test-title").replaceChildren(el("span", "sep", `${first.title}の実験`), `${first.conditions.map((c) => `${c.label} ${c.value}`).join("、")}`);
  const events = [];
  for (const e of ex) {
    const rev = (inv.revisions ?? []).find((r) => r.revision === e.revision && r.parent != null);
    if (rev) events.push({ rev });
    events.push({ e });
  }
  $("facts").replaceChildren(...events.slice(-5).map(({ e, rev }) => {
    const li = el("li");
    if (e) {
      li.append(el("span", "who-said", `Rev.${e.revision} の実験`), el("span", e.passed ? "pass sep" : "sep", e.passed ? "合格" : "不合格"), ...metricNodes(e));
    } else {
      const change = rev.changes.map((c) => `${c.label.split("：").pop()} ${c.before} → ${c.after}`).join("、");
      li.append(el("span", "who-said", `Rev.${rev.revision} への改良の依頼`), document.createTextNode(`「${rev.request}」`), el("span", "change", change));
    }
    return li;
  }));
}

// ---------------- 最初の画面から実験まで残る試作品 ----------------
const SETTLE = 2.6; // 組み上がって落ち着くまでの秒
const CYAN = 0x2fe3e6;

async function storyStage(item, inv) {
  const { THREE, createRenderer, createScene, loadModel, measure, stageRing } = await import("./stage3d.js");
  const canvas = $("stage");
  const renderer = createRenderer({ canvas });
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  const scene = createScene(renderer);
  const root = await loadModel(item.model, item.key);

  const m = measure(root);
  const c = m.center, H = m.size.y;
  // 試作品は中心の縦の軸で回す
  const pivot = new THREE.Group();
  pivot.position.set(c.x, 0, c.z);
  root.position.x -= c.x; root.position.z -= c.z;
  pivot.add(root);
  scene.add(pivot);
  scene.updateMatrixWorld(true);
  root.traverse((o) => { if (o.isMesh) { o.castShadow = true; o.receiveShadow = true; } });

  // 部品（名前の付いた節）ごと：組み上がる前の位置と、分解したときの位置（親の座標で持つ）
  const labels = new Map((inv?.parts ?? []).map((p) => [p.id, p.label]));
  const types = new Map((inv?.parts ?? []).map((p) => [p.id, p.type]));
  const lit = changedPartTypes(inv);
  const top = root.getObjectByName("invention") ?? root;
  const box = new THREE.Box3();
  const parts = top.children.filter((o) => o.name).map((o) => {
    box.setFromObject(o);
    const pc = box.getCenter(new THREE.Vector3());
    const d = pc.clone().sub(c);
    const flat = new THREE.Vector3(d.x, 0, d.z);
    if (flat.lengthSq() > 1e-6) flat.normalize();
    const away = new THREE.Vector3(d.x * 0.9, d.y * 0.55, d.z * 0.9).addScaledVector(flat, H * 0.1);
    const local = o.parent.worldToLocal(pc.clone().add(away)).sub(o.parent.worldToLocal(pc.clone()));
    const mats = [];
    o.traverse((n) => {
      if (n.isMesh) mats.push({ m: n.material, opacity: n.material.opacity, transparent: n.material.transparent, depthWrite: n.material.depthWrite, color: n.material.color?.clone() });
    });
    // 組み上がる前は、試作品の外側から寄ってくる
    const from = o.parent.worldToLocal(pc.clone().addScaledVector(flat, H * 0.18)).sub(o.parent.worldToLocal(pc.clone()));
    return { o, rest: o.position.clone(), restRot: o.rotation.z, low: box.min.y, from, away: local, mats, label: labels.get(o.name), lit: lit.has(types.get(o.name)) };
  }).sort((a, b) => a.low - b.low);
  parts.forEach((p, i) => {
    p.start = 0.25 + i * 0.09;
    p.lift = H * (0.5 + i * 0.035);
    p.spin = (i % 2 ? 1 : -1) * 0.35;
  });

  // 分解の場面の部品の名前（同じ名前の部品は1つだけに付ける）
  const tagsBox = $("tags");
  const tags = [];
  for (const p of parts) {
    if (!p.label || tags.some((t) => t.label === p.label)) continue;
    const t = el("span", "tag", p.label);
    tagsBox.append(t);
    tags.push({ label: p.label, part: p, node: t });
  }

  // 床：影だけを受ける面と、研究所の発明台の輪
  const floor = new THREE.Mesh(new THREE.PlaneGeometry(m.radius * 10, m.radius * 10), new THREE.ShadowMaterial({ opacity: 0.6 }));
  floor.rotation.x = -Math.PI / 2; floor.position.set(c.x, m.floor - 0.001, c.z); floor.receiveShadow = true;
  scene.add(floor);
  const ring = stageRing(c, m.floor, m.halfWidth);
  ring.traverse((n) => { if (n.material) { n.material.transparent = true; n.material.opacity = 0; } });
  scene.add(ring);

  // 光：右上からの主な光で影を落とし、後ろからの光で形の縁を浮かせる
  const key = scene.children.find((o) => o.isDirectionalLight);
  key.position.copy(c).add(new THREE.Vector3(1.1, 2.6, 1.5).multiplyScalar(m.radius * 2.2));
  key.target.position.copy(c); scene.add(key.target);
  key.castShadow = true;
  key.shadow.mapSize.set(2048, 2048);
  key.shadow.bias = -0.0004; key.shadow.normalBias = 0.01;
  Object.assign(key.shadow.camera, { left: -m.radius * 2, right: m.radius * 2, top: m.radius * 2, bottom: -m.radius * 2, near: 0.01, far: m.radius * 14 });
  const rim = new THREE.DirectionalLight(0xd8f6ff, 1.5);
  rim.position.copy(c).add(new THREE.Vector3(-1.4, 1.8, -2.4).multiplyScalar(m.radius * 2));
  rim.target.position.copy(c); scene.add(rim, rim.target);

  const cam = new THREE.PerspectiveCamera(22, 1, 0.01, 200);
  const partsText = $("parts-text");
  const cyan = new THREE.Color(CYAN);
  const v = new THREE.Vector3();

  // スクロールの位置から、分解の量・左右の位置・実験の場面への移り具合を出す
  function scrollState() {
    const vh = innerHeight;
    const u = (vh - $("parts").getBoundingClientRect().top) / vh;  // 分解の場面の上端が画面の下 → 0、上 → 1
    const w = (vh - $("test").getBoundingClientRect().top) / vh;   // 実験の場面について同じ
    if (reduced) {
      // 動きを減らす設定：途中の動きは見せず、場面ごとの姿に切り替える
      const inTest = w > 0.5, inParts = !inTest && u > 0.5;
      return { toScene: u > 0.5 ? 1 : 0, explode: inParts ? 1 : 0, test: inTest ? 1 : 0, leave: 0 };
    }
    return {
      toScene: smooth(u, 0, 1),
      explode: smooth(u, 0.35, 1) * (1 - smooth(w, -0.05, 0.4)),
      test: smooth(w, 0.1, 0.6),
      leave: smooth(w, 0, 0.3),
    };
  }

  function pose(t, s) {
    const lift = s.explode * H * 0.12;
    for (const p of parts) {
      const k = ease((t - p.start) / 0.8);
      p.o.position.copy(p.rest).addScaledVector(p.away, s.explode).addScaledVector(p.from, 1 - k);
      p.o.position.y += (1 - k) * p.lift;
      p.o.rotation.z = p.restRot + (1 - k) * p.spin;
      const a = clamp((t - p.start) / 0.3);
      for (const s2 of p.mats) {
        s2.m.transparent = a < 1 || s2.transparent;
        s2.m.depthWrite = a < 1 ? false : s2.depthWrite;
        s2.m.opacity = s2.opacity * a;
        // 実験の場面では、改良で値を変えた部品を水色（データの色）に寄せる
        if (s2.color && p.lit) s2.m.color.copy(s2.color).lerp(cyan, s.test * 0.55);
      }
      p.o.visible = a > 0;
    }
    pivot.position.y = lift;
    const r = ease(t / 0.7), sc = 0.75 + 0.25 * r;
    ring.scale.setScalar(sc);
    ring.position.set(c.x * (1 - sc), m.floor * (1 - sc), c.z * (1 - sc));
    ring.traverse((n) => { if (n.material) n.material.opacity = r; });
  }

  // 画面の中で試作品の中心を置く位置（画面の左上を 0,0 とした割合）と、画面の高さに占める割合
  function layout(s, w, vh) {
    const phone = NARROW.matches;
    const barh = bar.offsetHeight;
    const heroY = barh + (phone ? 0.235 : 0.48) * (vh - barh);
    const heroShare = (phone ? 0.4 : 0.82) * (vh - barh) / vh;
    const sceneY = phone ? 0.25 * vh : 0.52 * vh;
    const sceneShare = phone ? 0.36 : lerp(0.6, 0.72, s.test);
    return {
      x: phone ? 0.5 : lerp(0.66, 0.33, s.test),
      y: lerp(heroY, sceneY, s.toScene) / vh,
      share: lerp(heroShare, sceneShare, s.toScene) / (1 + 0.45 * s.explode),
      pitch: 0.2 + 0.12 * s.explode,
      side: phone ? 0.8 : 0.42,
    };
  }

  let yaw = reduced ? 0.3 : -0.9, last = null;
  const held = Number.parseFloat(new URLSearchParams(location.search).get("t")); // ?t=秒：その時刻の姿に止める（画面の確認用）
  const still = Number.isFinite(held) ? held : reduced ? SETTLE + 4.6 : null;

  function frame(now) {
    const t = still ?? (now - t0) / 1000;
    const dt = last == null ? 0 : Math.min(0.1, (now - last) / 1000);
    last = now;
    const w = canvas.clientWidth, h = canvas.clientHeight;
    if (!w || !h) return;
    const s = scrollState();
    if (partsText) partsText.style.opacity = !NARROW.matches && !reduced ? String(1 - s.leave) : "";
    // 組み上がる間に正面へ回り込み、その後はゆっくり回る。分解の間は止め、実験の場面では少し速く回る
    if (still == null) yaw += dt * (t < SETTLE ? 0.9 / SETTLE : 0.08 + 0.2 * s.test) * (1 - s.explode);
    pivot.rotation.y = still != null && !reduced ? -0.9 + 0.9 * ease(t / SETTLE) + 0.08 * Math.max(0, t - SETTLE) : yaw;

    renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
    renderer.setSize(w, h, false);
    cam.aspect = w / h;
    const vh = innerHeight;
    const L = layout(s, w, vh);
    const view = Math.max(H / L.share, Math.hypot(m.size.x, m.size.z) / (L.side * cam.aspect));
    const d = view / (2 * Math.tan(THREE.MathUtils.degToRad(cam.fov / 2)));
    const dolly = 1 + 0.12 * (1 - ease(t / SETTLE));
    const aimAt = v.set(c.x, c.y + s.explode * H * 0.12, c.z);
    cam.position.set(aimAt.x, aimAt.y + Math.sin(L.pitch) * d * dolly, aimAt.z + Math.cos(L.pitch) * d * dolly);
    cam.lookAt(aimAt);
    // 舞台（キャンバス）は画面の上端に止まるまでは下にずれているので、その分を引いて置く
    const top = canvas.getBoundingClientRect().top;
    cam.setViewOffset(w, h, (0.5 - L.x) * w, (0.5 * h) - (L.y * vh - top), w, h);
    cam.updateProjectionMatrix();
    pose(t, s);
    renderer.render(scene, cam);
    placeTags(s, w, h);
  }

  // 部品の名前：分解しきった頃に出す。試作品の右にある部品は右へ、左は左へ。重なれば下へずらす
  function placeTags(s, w, h) {
    const show = clamp((s.explode - 0.55) / 0.35);
    if (!tags.length) return;
    const cx = (() => { v.set(c.x, c.y, c.z).project(cam); return (v.x + 1) / 2 * w; })();
    const sides = { l: [], r: [] };
    for (const t of tags) {
      box.setFromObject(t.part.o).getCenter(v).project(cam);
      const x = (v.x + 1) / 2 * w, y = (1 - v.y) / 2 * h;
      const right = x >= cx;
      (right ? sides.r : sides.l).push({ t, x, y, right });
    }
    for (const list of [sides.l, sides.r]) {
      list.sort((a, b) => a.y - b.y);
      let floorY = -Infinity;
      for (const q of list) { q.y = Math.max(q.y, floorY + 38); floorY = q.y; }
      for (const q of list) {
        const n = q.t.node, tw = n.offsetWidth;
        const gap = 34;
        const x = q.right ? Math.min(w - tw - 16, q.x + gap) : Math.max(16, q.x - gap - tw);
        n.style.transform = `translate(${Math.round(x)}px, ${Math.round(q.y - 16)}px)`;
        n.style.opacity = show;
      }
    }
  }

  // 見えている間だけ描く
  let visible = true, t0 = null, raf = 0;
  const loop = (now) => {
    raf = 0;
    if (t0 == null) t0 = now;
    frame(now);
    if (visible && !document.hidden && (still == null || moving)) raf = requestAnimationFrame(loop);
    moving = false;
  };
  let moving = false;
  const wake = () => { moving = true; if (!raf) raf = requestAnimationFrame(loop); };
  new IntersectionObserver(([e]) => { visible = e.isIntersecting; if (visible) wake(); }).observe(story);
  document.addEventListener("visibilitychange", () => { if (!document.hidden) wake(); });
  addEventListener("scroll", wake, { passive: true });
  new ResizeObserver(wake).observe(canvas);
  wake();
}

// 改良で値を変えた部品の種類（変えた値の名前 → それを受け持つ機能 → 部品）
function changedPartTypes(inv) {
  const out = new Set();
  if (!inv) return out;
  for (const r of inv.revisions ?? []) {
    for (const ch of r.changes ?? []) {
      const block = ch.label.split("：")[0];
      for (const n of inv.needs ?? []) {
        if (!n.blocks?.some((b) => b.label === block)) continue;
        const id = /（([a-z0-9-]+)）/.exec(n.how ?? "")?.[1];
        const part = inv.parts.find((p) => p.id === id);
        if (part) out.add(part.type);
      }
    }
  }
  return out;
}

// ---------------- 公開中の発明品：選んだ行の試作品を左に立てる ----------------
async function shelf(items) {
  const { THREE, createRenderer, createScene, loadModel, measure, stageRing, dress, aim } = await import("./stage3d.js");
  const canvas = $("shelf");
  const renderer = createRenderer({ canvas });
  const cam = new THREE.PerspectiveCamera(26, 1, 0.01, 100);
  const cache = new Map();
  let cur = null, want = -1, yaw = 0.5, last = null, since = 0;

  async function show(i) {
    want = i;
    let s = cache.get(i);
    if (!s) {
      s = loadModel(items[i].model, items[i].key).then(async (root) => {
        const sc = createScene(renderer);
        sc.add(root);
        const m = measure(root);
        // 撮影台（床・影・光）。頭の上に付けるものは床に置かず、少し下から見上げる
        const floating = items[i].mount?.id === "head";
        if (!floating) sc.add(stageRing(m.center, m.floor, m.halfWidth));
        const lights = await dress(sc, root, m, { floating });
        return { sc, m, root, baseY: root.position.y, lights, floating };
      });
      cache.set(i, s);
    }
    const ready = await s;
    if (want === i) { cur = ready; since = performance.now(); wake(); }
  }

  function frame(now) {
    const dt = last == null ? 0 : Math.min(0.1, (now - last) / 1000);
    last = now;
    if (!cur) return;
    const w = canvas.clientWidth, h = canvas.clientHeight;
    if (!w || !h) return;
    renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
    renderer.setSize(w, h, false);
    cam.aspect = w / h;
    if (!reduced) yaw += dt * 0.35;
    // 入れ替わった頭の上の試作品は、少し下から上がって止まる（床に置いたものは床から動かさない）
    const k = reduced || !cur.floating ? 1 : ease((now - since) / 450);
    cur.root.position.y = cur.baseY - (1 - k) * cur.m.radius * 0.15;
    aim(cam, cur.m.center, cur.m.fit * 1.02, yaw, cur.floating ? -0.2 : 0.32);
    cur.lights.light(yaw);
    renderer.render(cur.sc, cam);
  }

  let visible = false, raf = 0;
  const loop = (now) => {
    raf = 0;
    frame(now);
    if (visible && !document.hidden && !reduced) raf = requestAnimationFrame(loop);
  };
  const wake = () => { if (!raf) raf = requestAnimationFrame(loop); };
  new IntersectionObserver(([e]) => { visible = e.isIntersecting; if (visible) wake(); }).observe(canvas);
  document.addEventListener("visibilitychange", () => { if (!document.hidden) wake(); });
  new ResizeObserver(wake).observe(canvas);
  return { show };
}

// 行を選ぶ：のせる・フォーカスする・画面の真ん中を通る
function rowsControl(items, onPick) {
  const rows = [...document.querySelectorAll(".shelf-row")];
  let on = -1;
  const pick = (i) => {
    if (i === on) return;
    on = i;
    rows.forEach((r, j) => r.classList.toggle("on", j === i));
    onPick(i);
  };
  rows.forEach((r, i) => {
    r.addEventListener("pointerenter", () => pick(i));
    r.addEventListener("focusin", () => pick(i));
  });
  const phone = NARROW;
  let seen = null;
  const watch = () => {
    seen?.disconnect();
    // スマホでは上に止まる試作品の帯のすぐ下、広い画面では画面の真ん中を通った行
    seen = new IntersectionObserver((entries) => {
      for (const e of entries) if (e.isIntersecting) pick(rows.indexOf(e.target));
    }, { rootMargin: phone.matches ? "-40% 0px -50% 0px" : "-45% 0px -45% 0px" });
    rows.forEach((r) => seen.observe(r));
  };
  watch();
  phone.addEventListener("change", watch);
  pick(0);
}

// ---------------- はじめる ----------------
const data = await load().catch(() => null);
if (data) {
  fill(data);
  try {
    await storyStage(data.feat, data.page?.invention);
  } catch {
    story.classList.add("no3d");
  }
  // 3D が使えないときは、行の写真をそのまま左に出す
  let picker = null;
  try { picker = await shelf(data.items); } catch { picker = null; }
  const stage = document.querySelector(".others-stage");
  rowsControl(data.items, (i) => {
    if (picker) { picker.show(i); return; }
    const x = data.items[i];
    if (!x.thumb) return;
    let img = stage.querySelector("img");
    if (!img) { img = el("img"); img.alt = ""; stage.append(img); stage.classList.add("photo"); }
    img.src = x.thumb;
  });
} else {
  story.classList.add("no3d");
}
