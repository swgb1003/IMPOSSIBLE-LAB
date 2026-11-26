// 発明の一覧。ライブラリの入口（/library）から読み、公開が無いか、つながらないときは見本（sample/list.json）を出す。
// 並べ替えと絞り込みはこの画面の中で行い、板は新しい位置へ滑って動く。
import { $, el, date, phraseBreaks } from "./text.js";
import { mountAccount } from "./session.js";

mountAccount(); // 上の帯の右（ライブラリにつながらないときは出さない）

const SORTS = [
  { id: "wants", label: "ほしい人の数" },
  { id: "new", label: "新しい順" },
  { id: "adoptions", label: "採用の実績" },
  { id: "name", label: "名前" },
];
const STATUS = [
  { id: "", label: "すべて" },
  { id: "open", label: "公開中" },
  { id: "considering", label: "検討中" },
  { id: "adopted", label: "採用" },
];
const STATUS_TEXT = { open: "公開中", considering: "検討中", adopted: "採用" };
const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;

const view = { sort: "new", tag: "", status: "" };
let items = [];
let sample = false;

async function load() {
  const forceSample = new URLSearchParams(location.search).has("sample");
  if (!forceSample) {
    try {
      const res = await fetch("/library?sort=new");
      if (res.ok) {
        const data = await res.json();
        if (Array.isArray(data.items) && data.items.length > 0) return data.items.map((x) => ({ ...x, page: `/inventions/${encodeURIComponent(x.id)}`, model: `/library/${encodeURIComponent(x.id)}/model.glb`, key: null }));
      }
    } catch { /* ライブラリにつながらない（共有ページなど）→ 見本 */ }
  }
  sample = true;
  const res = await fetch("/site/sample/list.json");
  return (await res.json()).items.map((x) => ({ ...x, model: `/site/sample/models/${x.id}.glb`, key: x.id }));
}

const wants = (x) => x.wants;
const newer = (a, b) => b.publishedAt.localeCompare(a.publishedAt);
function ordered() {
  const list = items.filter((x) => (!view.tag || x.tags.some((t) => t.id === view.tag)) && (!view.status || x.status === view.status));
  const by = {
    new: newer,
    wants: (a, b) => wants(b) - wants(a) || newer(a, b),
    adoptions: (a, b) => b.creatorAdoptions - a.creatorAdoptions || newer(a, b),
    name: (a, b) => a.name.localeCompare(b.name, "ja") || newer(a, b),
  }[view.sort];
  return list.sort(by);
}

// ---------------- 1件の板 ----------------
let entered = false;
function row(x) {
  const li = el("li", entered ? "t row in" : "t row");
  li.dataset.id = x.id;

  // 試作品の 3D がゆっくり回る。3D を出せないときは、研究所の発明台で撮った写真を出す
  const fig = el("figure", "thumb");
  const canvas = el("canvas", "spin");
  canvas.setAttribute("role", "img");
  canvas.setAttribute("aria-label", `「${x.name}」の試作品の立体。ゆっくり回っています`);
  fig.append(canvas);
  li.append(fig);
  spinner.add(fig, canvas, x, () => {
    canvas.remove();
    if (!x.thumb) return;
    const img = el("img");
    img.src = x.thumb; img.width = 640; img.height = 480; img.loading = "lazy";
    img.alt = `IMPOSSIBLE LAB の発明台に置いた「${x.name}」の試作品`;
    fig.append(img);
  });

  const body = el("div", "body");
  const h = el("h2", "row-name");
  if (x.page) { const a = el("a", null, x.name); a.href = x.page; h.append(a); } else h.textContent = x.name;
  const meta = el("p", "meta");
  const status = el("span", `st ${x.status}`, STATUS_TEXT[x.status] ?? "");
  meta.append(status, document.createTextNode(`　${x.tags.map((t) => t.label).join("・")}　Rev.${x.revision}　${date(x.publishedAt)} 公開　ほしい ${wants(x)} 人　採用 ${x.adoptions} 件`));
  body.append(h, el("p", "row-sum", x.summary), meta);
  li.append(body);

  const r = x.result;
  const res = el("div", "result");
  if (r) {
    const big = el("p", "res-v num");
    if (r.before) {
      big.append(el("span", "was", r.before.display), el("span", "arrow", "→"));
    }
    big.append(el("span", "now", r.after.display), el("small", null, r.unit));
    res.append(big, el("p", "res-l", `${r.label}（${r.title}）`));
    const pass = (p) => (p === true ? "合格" : p === false ? "不合格" : "判定なし");
    res.append(el("p", "res-n", r.before
      ? `Rev.${r.before.revision} ${pass(r.before.passed)} → Rev.${r.after.revision} ${pass(r.after.passed)}`
      : `Rev.${r.after.revision} ${pass(r.after.passed)}`));
  } else res.append(el("p", "res-n", "実験の記録なし"));
  li.append(res);
  return li;
}

// ---------------- ゆっくり回る試作品 ----------------
const spinner = (() => {
  let renderer = null, failed = false, running = false, last = 0, acc = 0;
  const all = [];
  const seen = new IntersectionObserver((entries) => {
    for (const e of entries) {
      const it = all.find((x) => x.fig === e.target);
      if (!it) continue;
      it.visible = e.isIntersecting;
      if (it.visible && !it.loading) start(it);
    }
    if (!running) { running = true; requestAnimationFrame(loop); }
  }, { rootMargin: "200px 0px" });

  // 3D の部品は後から読む（読めなくても一覧は出す）
  let L = null;
  const lib = () => (L ??= import("./stage3d.js"));
  let THREE, createRenderer, createScene, loadModel, measure, stageRing, dress, aim;

  async function start(it) {
    it.loading = true;
    try { ({ THREE, createRenderer, createScene, loadModel, measure, stageRing, dress, aim } = await lib()); } catch { failed = true; }
    if (!renderer && !failed) { try { renderer = createRenderer(); renderer.setPixelRatio(1); } catch { failed = true; } }
    if (failed) { it.fail(); return; }
    loadModel(it.model, it.key).then(async (root) => {
      it.scene = createScene(renderer);
      it.scene.add(root);
      const m = measure(root);
      it.root = root; it.center = m.center; it.radius = m.fit; it.baseY = root.position.y;
      // 撮影台（床・影・光）。頭の上に付けるものは床に置かず、少し下から見上げる
      if (!it.floating) it.scene.add(stageRing(m.center, m.floor, m.halfWidth));
      it.lights = await dress(it.scene, root, m, { floating: it.floating });
      it.cam = new THREE.PerspectiveCamera(28, 4 / 3, 0.01, 100);
      it.ready = true;
      draw(it, 0);
    }, () => it.fail());
  }

  function draw(it, t) {
    const dpr = Math.min(devicePixelRatio, 2);
    const w = Math.max(1, Math.round(it.canvas.clientWidth * dpr)), h = Math.max(1, Math.round(it.canvas.clientHeight * dpr));
    if (it.canvas.width !== w || it.canvas.height !== h) { it.canvas.width = w; it.canvas.height = h; }
    const size = renderer.getSize(new THREE.Vector2());
    if (size.x !== w || size.y !== h) renderer.setSize(w, h, false);
    it.cam.aspect = w / h; it.cam.updateProjectionMatrix();
    // 頭の上に付けるものは、ゆっくり回りながらわずかに上下する（床に置いたものは床から離れない）
    it.root.position.y = it.baseY + (reduced || !it.floating ? 0 : Math.sin(t * 1.3 + it.phase) * it.radius * 0.025);
    aim(it.cam, it.center, it.radius * 1.06, it.yaw, it.floating ? -0.2 : 0.34);
    it.lights.light(it.yaw);
    renderer.render(it.scene, it.cam);
    const ctx = it.canvas.getContext("2d");
    ctx.clearRect(0, 0, w, h);
    ctx.drawImage(renderer.domElement, 0, 0, w, h);
  }

  function loop(now) {
    const dt = Math.min(0.1, (now - last) / 1000 || 0); last = now; acc += dt;
    const live = all.filter((it) => it.ready && it.visible && it.canvas.isConnected);
    if (!reduced && acc >= 1 / 30) {
      for (const it of live) { it.yaw += acc * (it.fast ? 1.1 : 0.35); draw(it, now / 1000); }
      acc = 0;
    }
    if (live.length || all.some((it) => it.loading && !it.ready)) requestAnimationFrame(loop);
    else running = false;
  }

  return {
    add(fig, canvas, x, fail) {
      const it = { fig, canvas, fail: () => { if (!it.failed) { it.failed = true; fail(); } }, model: x.model, key: x.key, floating: x.mount?.id === "head",
        yaw: all.length * 0.9 + 0.5, phase: all.length * 1.7, visible: false, loading: false, ready: false, fast: false };
      all.push(it);
      // 板にのせると少し速く回る
      fig.parentElement?.addEventListener("pointerenter", () => { it.fast = true; });
      fig.parentElement?.addEventListener("pointerleave", () => { it.fast = false; });
      seen.observe(fig);
    },
  };
})();

// ---------------- 並べ替え（板は新しい位置へ滑る） ----------------
function draw() {
  const list = $("rows");
  const before = new Map([...list.children].map((c) => [c.dataset.id, c.getBoundingClientRect().top]));
  const next = ordered();
  const keep = new Map([...list.children].map((c) => [c.dataset.id, c]));
  list.replaceChildren(...next.map((x) => keep.get(x.id) ?? row(x)));
  phraseBreaks(list);
  $("empty").hidden = next.length > 0;
  const total = items.length;
  $("count").textContent = view.tag || view.status ? `${total} 件のうち ${next.length} 件` : `${total} 件`;
  if (sample) $("count").textContent += "（すべて見本）";
  if (reduced) return;
  for (const c of list.children) {
    const was = before.get(c.dataset.id);
    if (was == null) continue;
    const dy = was - c.getBoundingClientRect().top;
    if (Math.abs(dy) < 1) continue;
    c.animate([{ transform: `translateY(${dy}px)` }, { transform: "none" }], { duration: 420, easing: "cubic-bezier(.2,.7,.2,1)" });
  }
}

function seg(slot, options, key) {
  const box = $(slot);
  box.replaceChildren(...options.map((o) => {
    const b = el("button", null, o.count != null ? `${o.label} ${o.count}` : o.label);
    b.type = "button";
    b.setAttribute("aria-pressed", String(view[key] === o.id));
    b.addEventListener("click", () => {
      view[key] = key !== "sort" && view[key] === o.id ? "" : o.id;
      for (const x of box.children) x.setAttribute("aria-pressed", "false");
      if (view[key] === o.id) b.setAttribute("aria-pressed", "true");
      else box.firstElementChild?.setAttribute("aria-pressed", String(key !== "sort" && view[key] === ""));
      draw();
    });
    return b;
  }));
}

try {
  items = await load();
  // 機能のタグは、一覧に出てくるものだけを件数つきで出す（多い順）
  const counts = new Map();
  for (const x of items) for (const t of x.tags) counts.set(t.id, { label: t.label, n: (counts.get(t.id)?.n ?? 0) + 1 });
  const tags = [{ id: "", label: "すべて" }, ...[...counts].sort((a, b) => b[1].n - a[1].n).map(([id, v]) => ({ id, label: v.label, count: v.n }))];
  seg("sort", SORTS, "sort");
  seg("tags", tags, "tag");
  seg("status", STATUS.map((s) => ({ ...s, count: s.id ? items.filter((x) => x.status === s.id).length : undefined })), "status");
  draw();
  $("sample-note").textContent = sample
    ? "この一覧は見本です。IMPOSSIBLE LAB の通し確認で作った記録で、実在の発案者はいません。"
    : "公開を取り下げた発明は、この一覧から消えます。";
  phraseBreaks(document.querySelector("main"));
} catch {
  $("count").textContent = "一覧を読み込めません";
}
// 板は読み込んだときに順に入る（スクロールは待たない）
requestAnimationFrame(() => requestAnimationFrame(() => {
  let i = 0;
  for (const t of document.querySelectorAll(".board > .t, .rows > .t")) { t.style.setProperty("--d", `${Math.min(i++, 12) * 60}ms`); t.classList.add("in"); }
  entered = true;
}));
