// 発明の1件のページ。?id=<公開 ID> ならライブラリの入口から、無ければ見本（sample/rain）を読む。
// 文字はすべて textContent で入れる（公開データをそのまま HTML にしない）。
import { THREE, createRenderer, createScene, loadModel, measure, stageRing, aim } from "./stage3d.js";
import { $, el, date, phraseBreaks } from "./text.js";

const STATE = { simulated: "計算した", simplified: "単純化して計算した", unverifiable: "確かめられない" };

const id = new URLSearchParams(location.search).get("id");
const src = id
  ? { page: `/library/${encodeURIComponent(id)}`, model: `/library/${encodeURIComponent(id)}/model.glb` }
  : { page: "sample/rain/page.json", model: "sample/models/sample-rain.glb", key: "sample-rain" };


function render(page) {
  const inv = page.invention;
  document.title = `${inv.name} | IMPOSSIBLE LIBRARY`;
  $("name").textContent = inv.name;
  $("summary").textContent = inv.summary;
  // 名前の下の1行：見本の印と、分類・付ける所・版・部品と実験の数・公開日・発案者
  const meta = $("meta");
  if (page.sample) meta.append(el("span", "sample", "見本"));
  const who = page.creator.publicId ? `発案者 ${page.creator.name}` : "発案者は匿名";
  meta.append(el("span", null, [inv.tags.map((t) => t.label).join("・"), `${inv.mount.label}に付ける`, `Rev.${inv.revision}`,
    `部品 ${inv.parts.length} 点`, `実験 ${inv.experiments.length} 回`, `${date(page.publishedAt)} 公開`, who].join("　")));
  $("request").textContent = inv.request || "依頼の文の記録なし";
  $("rev").textContent = `Rev.${inv.revision}`;

  // 数値：いまの版の DREAM と REALITY の主な計測（同じ条件の最初の版と比べる）
  const primary = (e) => e?.metrics.find((m) => m.primary);
  const dream = [...inv.experiments].reverse().find((e) => e.mode === "dream" && e.revision === inv.revision);
  const reality = [...inv.experiments].reverse().find((e) => e.mode === "reality" && e.revision === inv.revision);
  const first = inv.experiments.find((e) => e.mode === "dream" && dream && e.title === dream.title);
  const setFig = (slot, m) => { const v = $(slot); v.textContent = m.display; v.append(el("small", null, m.unit)); };
  if (primary(dream)) {
    const m = primary(dream);
    setFig("dream", m);
    const before = first && first.revision !== dream.revision ? `Rev.${first.revision} は ${primary(first).display} ${m.unit}` : "";
    const goal = dream.criteria ? `合格の目安は${dream.criteria.replace(m.label, "").trim()}` : "";
    $("dream-note").textContent = `${m.label}。Rev.${dream.revision} を DREAM で測った値。` + [before, goal].filter(Boolean).join("、") + (before || goal ? "。" : "");
  }
  if (primary(reality)) {
    const m = primary(reality);
    setFig("reality", m);
    const drift = reality.metrics.find((x) => x.key === "driftM");
    $("reality-note").textContent = `同じ Rev.${reality.revision} を REALITY で測った${m.label}。` + (drift ? `横風で ${drift.display} m 流され、頭から外れた。` : "");
    const power = reality.metrics.find((x) => x.key === "powerW");
    const runtime = reality.metrics.find((x) => x.key === "runtimeMin");
    const mass = reality.metrics.find((x) => x.key === "massKg");
    if (power) {
      setFig("power", power);
      $("power-note").textContent = `${power.label}。` + (mass ? `電池を含めた重さは ${mass.display} kg。` : "");
    }
    if (runtime) {
      setFig("runtime", runtime);
      $("runtime-note").textContent = `${runtime.label}。浮かび続ける電力を電池でまかなった場合。`;
    }
  }

  // 部品
  $("part-count").textContent = String(inv.parts.length);
  const list = $("parts");
  for (const p of inv.parts) {
    const b = el("button");
    b.type = "button"; b.dataset.part = p.id; b.setAttribute("aria-pressed", "false");
    b.append(el("b", null, p.label), el("span", null, p.role || `${p.shape}・${p.material}`));
    b.addEventListener("click", () => select(b.getAttribute("aria-pressed") === "true" ? null : p.id));
    const li = el("li"); li.append(b); list.append(li);
  }

  // 改良の履歴（新しい2つ）
  const revs = $("revisions");
  for (const r of inv.revisions.slice(-2)) {
    const li = el("li");
    li.append(el("span", "r", `Rev.${r.revision}`));
    li.append(el("q", null, r.request || r.summary || "記録なし"));
    const c = r.changes[0];
    if (c) { const p = el("p", "chg", `${c.label} `); p.append(el("span", "nw", `${c.before.replace(/ \S+$/, "")} → ${c.after}`)); li.append(p); }
    revs.append(li);
  }

  // しくみ
  const needs = $("needs");
  for (const n of inv.needs) {
    const li = el("li");
    li.append(el("p", "need", n.need), el("p", "state", STATE[n.status] ?? ""));
    const what = el("p", "what", n.how || "しくみの記録なし");
    if (n.note) what.append(el("span", null, n.note));
    li.append(what);
    needs.append(li);
  }
  const assumptions = $("assumptions");
  for (const a of [...inv.research.assumptions, ...inv.research.simplified].slice(0, 4)) assumptions.append(el("li", null, a));
  const challenges = $("challenges");
  for (const c of [...inv.developer.challenges, ...inv.research.unverifiable.map((u) => u.reason)].slice(0, 4)) challenges.append(el("li", null, c));
  $("adopt").addEventListener("click", (e) => {
    e.currentTarget.previousElementSibling.textContent = "採用の画面はこれから作ります。登録した研究者・開発者を選び、理由を書いて採用します。";
  });
  $("sample-note").textContent = page.sample
    ? "このページは見本です。研究所の通し確認で作った記録で、実在の発案者はいません。"
    : `${date(page.updatedAt)} 更新。公開を取り下げると、このページは一覧から消えます。`;
  phraseBreaks(document.querySelector("main"));
  return inv;
}

// ---------------- 回せる試作品 ----------------
let select = () => {};

function stage(canvas, url, key, inv) {
  let renderer;
  try { renderer = createRenderer({ canvas }); }
  catch { $("fallback").hidden = false; return; }
  const scene = createScene(renderer);
  const cam = new THREE.PerspectiveCamera(28, 1, 0.01, 100);

  const meshes = [];
  let center = new THREE.Vector3(), radius = 0.5, yaw = 0.5, pitch = 0.32, manual = false, picked = null, visible = true;
  const partNames = new Set(inv.parts.map((p) => p.id));
  const ownerOf = (o) => { for (let x = o; x; x = x.parent) if (partNames.has(x.name)) return x.name; return null; };

  loadModel(url, key).then((root) => {
    scene.add(root);
    root.traverse((o) => { if (o.isMesh) meshes.push({ mesh: o, part: ownerOf(o), color: o.material.color.clone(), emissive: o.material.emissive.clone() }); });
    const m = measure(root);
    center = m.center; radius = m.fit;
    $("size").textContent = `幅 ${(m.halfWidth * 2).toFixed(2)} m　高さ ${m.size.y.toFixed(2)} m`;
    scene.add(stageRing(m.center, m.floor, m.halfWidth));
    frame();
  }, () => { $("fallback").hidden = false; });

  function frame() {
    const w = canvas.clientWidth, h = canvas.clientHeight;
    if (canvas.width !== Math.round(w * renderer.getPixelRatio()) || canvas.height !== Math.round(h * renderer.getPixelRatio())) {
      renderer.setSize(w, h, false); cam.aspect = w / h; cam.updateProjectionMatrix();
    }
    aim(cam, center, radius, yaw, pitch);
    renderer.render(scene, cam);
  }

  let last = performance.now();
  function loop(t) {
    const dt = Math.min(0.05, (t - last) / 1000); last = t;
    if (visible) {
      if (!manual && !matchMedia("(prefers-reduced-motion: reduce)").matches) yaw += dt * 0.22;
      frame();
    }
    requestAnimationFrame(loop);
  }
  requestAnimationFrame(loop);
  new IntersectionObserver(([e]) => { visible = e.isIntersecting; }).observe(canvas);

  // 押したまま動かすと回る。動かさずに離したら、その部品を選ぶ
  let down = null;
  canvas.addEventListener("pointerdown", (e) => { down = { x: e.clientX, y: e.clientY, moved: 0 }; canvas.setPointerCapture(e.pointerId); });
  canvas.addEventListener("pointermove", (e) => {
    if (!down) return;
    const dx = e.clientX - down.x, dy = e.clientY - down.y;
    down.moved += Math.abs(dx) + Math.abs(dy); down.x = e.clientX; down.y = e.clientY;
    if (down.moved > 4) { manual = true; yaw -= dx * 0.008; pitch = THREE.MathUtils.clamp(pitch + dy * 0.006, -0.35, 1.3); }
  });
  canvas.addEventListener("pointerup", (e) => {
    if (down && down.moved <= 4) {
      const r = canvas.getBoundingClientRect();
      const ray = new THREE.Raycaster();
      ray.setFromCamera(new THREE.Vector2(((e.clientX - r.left) / r.width) * 2 - 1, -((e.clientY - r.top) / r.height) * 2 + 1), cam);
      const hit = ray.intersectObjects(meshes.map((m) => m.mesh), false)[0];
      const part = hit ? ownerOf(hit.object) : null;
      select(part && part !== picked ? part : null);
    }
    down = null;
  });

  select = (part) => {
    picked = part;
    for (const m of meshes) {
      const mat = m.mesh.material;
      if (!part) { mat.color.copy(m.color); mat.emissive.copy(m.emissive); }
      else if (m.part === part) { mat.color.copy(m.color); mat.emissive.setHex(0xff6a26).multiplyScalar(0.45); }
      else { mat.color.copy(m.color).multiplyScalar(0.22); mat.emissive.setHex(0x000000); }
    }
    for (const b of document.querySelectorAll("[data-part]")) b.setAttribute("aria-pressed", String(b.dataset.part === part));
    const p = inv.parts.find((x) => x.id === part);
    $("pick").textContent = p ? `${p.label}　${p.role}` : "";
  };
}

// ---------------- 板が順に入る（読み込んだときに一度だけ。スクロールは待たない） ----------------
function entrance() {
  let i = 0;
  for (const board of document.querySelectorAll(".board")) {
    for (const t of board.children) { t.style.setProperty("--d", `${Math.min(i++, 14) * 60}ms`); }
  }
  requestAnimationFrame(() => requestAnimationFrame(() => { for (const t of document.querySelectorAll(".board > .t")) t.classList.add("in"); }));
}

try {
  const res = await fetch(src.page);
  if (!res.ok) throw new Error(String(res.status));
  const inv = render(await res.json());
  stage($("canvas"), src.model, src.key, inv);
} catch {
  $("name").textContent = "この発明を読み込めません";
  $("summary").textContent = "公開を取り下げられたか、ライブラリにつながっていません。";
}
entrance();
