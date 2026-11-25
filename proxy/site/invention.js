// 発明の1件のページ。?id=<公開 ID> ならライブラリの入口から、無ければ見本（sample/rain）を読む。
// 文字はすべて textContent で入れる（公開データをそのまま HTML にしない）。
import { THREE, createRenderer, createScene, loadModel, measure, stageRing, aim } from "./stage3d.js";
import { $, el, date, phraseBreaks } from "./text.js";
import { api, mountWho, roleLabel, voterId } from "./who.js";

// 要望の確かめ方（アプリの設計の確認の画面と同じ言い方）
const STATE = { simulated: "実験で測る", simplified: "単純化して測る", demonstrated: "動きで見せる", unverifiable: "確かめられない" };

const id = new URLSearchParams(location.search).get("id");
const enc = encodeURIComponent;
/** ライブラリの入口から読めたか（見本のファイルから読んだときは、採用・ほしいを押せない）。 */
let live = false;

/**
 * 読む先：ライブラリの入口（/library/<ID>）。ID が無ければいちばん新しい公開。
 * ライブラリにつながらないとき（共有ページなど）は、見本のファイル（sample/pages/<ID>.json）を読む。
 */
async function resolveSource() {
  let pid = id;
  try {
    if (!pid) { const r = await fetch("/library?sort=new"); if (r.ok) pid = (await r.json()).items?.[0]?.id; }
    if (pid) {
      const r = await fetch(`/library/${enc(pid)}`);
      if (r.ok && (r.headers.get("content-type") ?? "").includes("json")) return { page: await r.json(), model: `/library/${enc(pid)}/model.glb`, key: null };
    }
  } catch { /* ライブラリにつながらない → 見本 */ }
  if (!pid) pid = (await (await fetch("sample/list.json")).json()).items?.[0]?.id;
  const r = await fetch(`sample/pages/${enc(pid)}.json`);
  if (!r.ok) throw new Error(String(r.status));
  return { page: await r.json(), model: `sample/models/${enc(pid)}.glb`, key: pid };
}


function render(page) {
  const inv = page.invention;
  document.title = `${inv.name} | IMPOSSIBLE LIBRARY`;
  $("name").textContent = inv.name;
  $("summary").textContent = inv.summary;
  // 名前の下の1行：見本の印と、分類・付ける所・版・部品と実験の数・公開日・発案者
  const meta = $("meta");
  if (page.sample) meta.append(el("span", "sample", "見本"));
  if (page.creator.publicId && live) {
    const a = el("a", null, `発案者 ${page.creator.name}`);
    a.href = `creator.html?id=${enc(page.creator.publicId)}`;
    meta.append(a);
  } else meta.append(page.creator.publicId ? `発案者 ${page.creator.name}` : "発案者は匿名");
  meta.append("　" + [inv.tags.map((t) => t.label).join("・"), `${inv.mount.label}に付ける`, `Rev.${inv.revision}`,
    `部品 ${inv.parts.length} 点`, `実験 ${inv.experiments.length} 回`, `${date(page.publishedAt)} 公開`].join("　"));
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
  } else if (primary(first) && dream && first.revision !== dream.revision) {
    // REALITY で測っていない発明は、改良前の版の数値を並べて前後を比べる
    const m = primary(first);
    setFig("reality", m);
    $("reality-note").textContent = `改良前の Rev.${first.revision} を同じ条件の DREAM で測った${m.label}。`;
  } else $("reality").closest(".t").remove();
  if (!$("power").textContent && !$("runtime").textContent) experimentLog(inv);

  // 部品
  $("part-count").textContent = `${inv.parts.length} 点`;
  const list = $("parts");
  partGroups = [];
  for (const p of inv.parts) {
    const role = p.role || `${p.shape}・${p.material}`;
    let g = partGroups.find((x) => x.label === p.label && x.role === role);
    if (!g) { g = { label: p.label, role, ids: [] }; partGroups.push(g); }
    g.ids.push(p.id);
  }
  for (const g of partGroups) {
    const b = el("button");
    b.type = "button"; b.dataset.parts = g.ids.join(","); b.setAttribute("aria-pressed", "false");
    b.append(el("b", null, g.ids.length > 1 ? `${g.label}　×${g.ids.length}` : g.label), el("span", null, g.role));
    b.addEventListener("click", () => select(b.getAttribute("aria-pressed") === "true" ? null : g.ids));
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
  // 開発者向けの課題（確かめられない要望を含む）。研究者向けの理由のうち、課題にまだ無いものだけ足す
  const lines = [...inv.developer.challenges];
  for (const u of inv.research.unverifiable) if (!lines.some((l) => l.includes(u.reason))) lines.push(`${u.need}：${u.reason}`);
  for (const c of lines.slice(0, 4)) challenges.append(el("li", null, c));
  $("sample-note").textContent = page.sample
    ? "このページは見本です。研究所の通し確認で作った記録で、実在の発案者はいません。"
    : `${date(page.updatedAt)} 更新。公開を取り下げると、このページは一覧から消えます。`;
  fillSlots(page, inv);
  demand(page);
  phraseBreaks(document.querySelector("main"));
  return inv;
}

/**
 * 写真の枠（4つ）：実験の様子 → 結果の画面 → 発明台に置いた試作品の順に入れる。
 * 写真が足りない枠にはデータの板（実験の条件・大きさと重さ）を入れ、それも無ければ枠を消して、計算の前提の板を広げる。
 */
function fillSlots(page, inv) {
  const images = page.images ?? [];
  const take = (kinds) => { const i = images.findIndex((m) => kinds.includes(m.kind)); return i < 0 ? null : images.splice(i, 1)[0]; };
  const wanted = [["experiment"], ["experiment", "result"], ["result", "experiment"], ["stage"]];
  const facts = [conditionsFact(inv), sizeFact(inv)].filter(Boolean);
  let removed = 0;
  ["slot-a", "slot-b", "slot-c", "slot-d"].forEach((name, i) => {
    const slot = $(name);
    const img = take(wanted[i]) ?? take(["stage", "experiment", "result"]);
    if (img) {
      slot.className = "t w3 h2 pic" + (i === 0 ? " shutter" : "");
      const im = el("img");
      im.src = img.url; im.width = 720; im.height = 400; im.loading = "lazy"; im.alt = img.caption;
      slot.append(im, el("figcaption", null, img.caption));
      return;
    }
    const f = facts.shift();
    if (!f) { slot.remove(); if (i >= 2) removed++; return; }
    slot.className = "t w3 h2 fact" + (i % 2 ? " alt" : "");
    slot.append(el("h2", null, f.title));
    const ul = el("ul");
    for (const line of f.lines) ul.append(el("li", null, line));
    slot.append(ul);
  });
  // 3つ目の盤（推定・課題・前提）で消えた枠の分だけ、計算の前提の板を広げる
  if (removed) $("assumptions").closest(".t").classList.replace("w6", removed === 1 ? "w9" : "w12");
}

function conditionsFact(inv) {
  const e = inv.experiments[inv.experiments.length - 1];
  if (!e) return null;
  const lines = e.conditions.slice(0, 4).map((c) => `${c.label} ${c.value}`);
  if (e.criteria) lines.push(`合格の目安：${e.criteria}`);
  return { title: `実験の条件（${e.title}）`, lines };
}

function sizeFact(inv) {
  const d = inv.developer;
  const lines = [];
  if (d.totalMassKg != null) lines.push(`部品の重さの合計 ${d.totalMassKg.toFixed(2)} kg`);
  lines.push(`部品 ${inv.parts.length} 点`);
  if (d.tipAngleDeg != null) lines.push(`後ろに倒れずに上れる坂 ${d.tipAngleDeg.toFixed(1)}°`);
  if (d.powerW != null) lines.push(`必要な電力 ${Math.round(d.powerW)} W（REALITY の推定）`);
  return { title: "大きさと重さ", lines };
}

/** ほしい人と試した人の帯。ライブラリにつながっていれば、このブラウザから1回ほしいを押せる。 */
function demand(page) {
  const figs = $("want-figs");
  const fig = (v, unit, label) => { const d = el("div"); const p = el("p", "v num", String(v)); p.append(el("small", null, unit)); d.append(p, el("p", null, label)); return d; };
  const fill = () => figs.replaceChildren(
    fig(page.tried, "人", "VR で呼び出して試した人"), fig(page.wants.vr, "人", "試して、ほしいを押した人"),
    fig(page.wants.site, "人", "サイトでほしいを押した人"), fig(page.adoptions.length, "件", "研究者・開発者の採用"));
  fill();
  const act = $("want-act");
  act.replaceChildren();
  if (!live) { act.append(el("p", "note", "ほしいは PC のサイトで押せます。")); return; }
  const key = "ilab.wanted";
  const done = () => { try { return JSON.parse(localStorage.getItem(key) ?? "[]").includes(page.id); } catch { return false; } };
  const b = el("button", "btn s sm", done() ? "ほしいを送りました" : "ほしい");
  b.type = "button";
  b.disabled = done();
  b.addEventListener("click", async () => {
    const voter = voterId();
    if (!voter) return;
    b.disabled = true;
    try {
      const r = await api(`/library/${enc(page.id)}/want`, { method: "POST", body: { voter } });
      page.wants = r.wants;
      fill();
      phraseBreaks(figs);
      try { localStorage.setItem(key, JSON.stringify([...JSON.parse(localStorage.getItem(key) ?? "[]"), page.id])); } catch { /* 数はサーバーが1回に限る */ }
      b.textContent = "ほしいを送りました";
    } catch { b.disabled = false; }
  });
  act.append(b, el("p", "note", "このブラウザから1回だけ数えます。"));
}

// ---------------- 採用（登録した研究者・開発者が、理由を書いて採用する） ----------------
async function actions(page) {
  const tile = $("adopt").closest(".act");
  const text = tile.querySelector("p");
  const btn = $("adopt");
  const extra = el("div", "acts");
  const old = tile.querySelector(".acts");
  if (old) { old.before(btn); old.remove(); } // 採用のあと描き直すとき、ボタンを元の場所に戻す
  if (!live) {
    btn.hidden = true;
    text.textContent = "登録した研究者と開発者が、理由を書いて採用できます。採用は PC のサイトからできます。";
    return;
  }
  const who = await mountWho();
  const me = who.me;
  const n = page.adoptions.length;
  const names = page.adoptions.slice(0, 2).map((a) => `${a.adopter.name}（${a.adopter.affiliation || roleLabel(a.adopter.role)}）`).join("、");
  text.textContent = n ? `採用 ${n} 件：${names}${n > 2 ? " ほか" : ""}。` : "登録した研究者と開発者が、理由を書いて採用できます。採用すると発案者に知らせが届きます。";
  const adopted = me?.kind === "account" && page.adoptions.some((a) => a.adopter.id === me.id);
  const considering = me?.kind === "account" && page.considering.some((c) => c.account.id === me.id);
  btn.hidden = false;
  btn.onclick = null;
  if (adopted) {
    btn.hidden = true;
    const t = (await api("/threads").catch(() => ({ items: [] }))).items.find((x) => x.pubId === page.id);
    const a = el("a", "btn", "やりとりを開く");
    a.href = t ? `threads.html?id=${t.id}` : "threads.html";
    extra.append(a);
    text.textContent = `${me.name}として採用しました。` + (n > 1 ? `ほかに ${n - 1} 件の採用があります。` : "発案者とのやりとりが始まっています。");
  } else if (me?.kind === "creator") {
    btn.hidden = true;
    text.textContent += me.id === page.creator.publicId ? " この発明はあなたが公開したものです。" : " 採用するには、研究者か開発者として名乗ってください。";
  } else if (!me) {
    btn.onclick = () => { location.href = `accounts.html?back=${enc(`invention.html?id=${page.id}`)}`; };
  } else {
    btn.onclick = () => adoptDialog(page, me);
    if (!considering) {
      const c = el("button", "txt", "検討中にする");
      c.type = "button";
      c.addEventListener("click", async () => {
        c.disabled = true;
        try { await api(`/library/${enc(page.id)}/consider`, { method: "POST", body: { accountId: me.id } }); c.replaceWith(el("span", null, "検討中にしました")); }
        catch { c.disabled = false; }
      });
      extra.append(c);
    } else extra.append(el("span", null, "検討中にしています"));
  }
  btn.after(extra);
  extra.prepend(btn);
}

function adoptDialog(page, me) {
  const MAX = 200;
  const dlg = el("dialog", "dlg");
  dlg.setAttribute("aria-labelledby", "dlg-h");
  const form = el("form", "form");
  const h = el("h2", null, `${page.invention.name}を採用する`);
  h.id = "dlg-h";
  const label = el("label", "field");
  const s = el("span", null, "採用する理由");
  s.append(el("small", null, "必須。発案者とページを読む人に見えます"));
  const ta = el("textarea");
  ta.rows = 4; ta.maxLength = MAX; ta.required = true;
  label.append(s, ta);
  const count = el("span", "count", `0 / ${MAX}`);
  const err = el("p", "err");
  err.setAttribute("role", "alert");
  const ok = el("button", "btn p sm", "採用する");
  ok.type = "submit";
  const no = el("button", "btn s sm", "やめる");
  no.type = "button";
  no.addEventListener("click", () => dlg.close());
  const acts = el("div", "acts");
  acts.append(ok, no, count);
  form.noValidate = true;
  form.append(h, el("p", "note", `${me.name}（${me.sub}）として採用します。採用すると発案者に知らせが届き、2人だけのやりとりができます。`), label, err, acts);
  ta.addEventListener("input", () => { count.textContent = `${ta.value.length} / ${MAX}`; if (ta.value.trim()) err.textContent = ""; });
  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    const reason = ta.value.trim();
    if (!reason) { err.textContent = "採用する理由を書いてください。"; ta.focus(); return; }
    ok.disabled = true; err.textContent = "";
    try {
      await api(`/library/${enc(page.id)}/adopt`, { method: "POST", body: { accountId: me.id, reason } });
      dlg.close();
      const fresh = await api(`/library/${enc(page.id)}`);
      Object.assign(page, fresh);
      demand(page);
      phraseBreaks($("want"));
      await actions(page);
    } catch (x) { err.textContent = x.message; ok.disabled = false; }
  });
  dlg.append(form);
  dlg.addEventListener("close", () => dlg.remove());
  document.body.append(dlg);
  dlg.showModal();
  ta.focus();
}

/** 推定の板（電力・時間）の代わりに、すべての実験の記録（版・モード・数値・合否）を出す。 */
function experimentLog(inv) {
  const tile = $("power").closest(".t");
  tile.className = "t w6 h2 notes log";
  tile.replaceChildren(el("h2", null, "実験の記録"));
  const ol = el("ol");
  for (const e of inv.experiments.slice(-6)) {
    const m = e.metrics.find((x) => x.primary);
    const pass = e.passed === true ? "合格" : e.passed === false ? "不合格" : "判定なし";
    ol.append(el("li", null, `Rev.${e.revision}　${e.mode === "reality" ? "REALITY" : "DREAM"}　${e.title}　${m ? `${m.label} ${m.display} ${m.unit}` : ""}　${pass}`));
  }
  tile.append(ol);
}

// ---------------- 回せる試作品 ----------------
let select = () => {};
/** 部品のまとまり（同じ種類・同じ役割）。3D で押した部品は、そのまとまりごと光らせる。 */
let partGroups = [];

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
      const g = part ? partGroups.find((x) => x.ids.includes(part)) : null;
      select(g && g.ids.join(",") !== picked ? g.ids : null);
    }
    down = null;
  });

  select = (ids) => {
    picked = ids ? ids.join(",") : null;
    const set = new Set(ids ?? []);
    for (const m of meshes) {
      const mat = m.mesh.material;
      if (!ids) { mat.color.copy(m.color); mat.emissive.copy(m.emissive); }
      else if (set.has(m.part)) { mat.color.copy(m.color); mat.emissive.setHex(0xff6a26).multiplyScalar(0.45); }
      else { mat.color.copy(m.color).multiplyScalar(0.22); mat.emissive.setHex(0x000000); }
    }
    for (const b of document.querySelectorAll("[data-parts]")) b.setAttribute("aria-pressed", String(b.dataset.parts === picked));
    const g = partGroups.find((x) => x.ids.join(",") === picked);
    $("pick").textContent = g ? `${g.label}${g.ids.length > 1 ? ` ×${g.ids.length}` : ""}　${g.role}` : "";
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
  const src = await resolveSource();
  live = src.key === null;
  const inv = render(src.page);
  stage($("canvas"), src.model, src.key, inv);
  actions(src.page).catch(() => {});
} catch {
  $("name").textContent = "この発明を読み込めません";
  $("summary").textContent = "公開を取り下げられたか、ライブラリにつながっていません。";
}
entrance();
