// 発明の1件のページ（/inventions/<公開 ID>）。ライブラリーの入口から読み、つながらないときは見本を読む。
// だれでも見られるのは、名前・何をするものか・3D と写真・実験の結果の要点・ほしいと採用の数。
// 依頼の原文・部品としくみ・実験の条件・改良の履歴は、ログインした開発者（と公開した発案者・運営）だけに出す。
// 文字はすべて textContent で入れる（公開データをそのまま HTML にしない）。
import { THREE, createRenderer, createScene, loadModel, measure, stageRing, dress, createAO, aim } from "./stage3d.js";
import { $, el, date, phraseBreaks } from "./text.js";
import { api, mountAccount, userSub, voterId } from "./session.js";

// 要望の確かめ方（アプリの設計の確認の画面と同じ言い方）
const STATE = { simulated: "実験で測る", simplified: "単純化して測る", demonstrated: "動きで見せる", unverifiable: "確かめられない" };

const id = /^\/inventions\/([a-z0-9-]{3,40})$/.exec(location.pathname)?.[1] ?? null;
const enc = encodeURIComponent;
/** ライブラリの入口から読めたか（見本のファイルから読んだときは、採用・ほしいを押せない）。 */
let live = false;

/**
 * 読む先：ログインしていれば詳しい内容つきの入口（/api/library/<ID>）、していなければだれでも見られる入口（/library/<ID>）。
 * ID が無ければいちばん新しい公開。ライブラリーにつながらないとき（共有ページなど）は、見本のファイル（sample/pages/<ID>.json）を読む。
 */
async function resolveSource(user) {
  let pid = id;
  const isJson = (r) => r.ok && (r.headers.get("content-type") ?? "").includes("json");
  try {
    if (!pid) { const r = await fetch("/library?sort=new"); if (r.ok) pid = (await r.json()).items?.[0]?.id; }
    if (pid) {
      const r = await fetch(user ? `/api/library/${enc(pid)}` : `/library/${enc(pid)}`, { credentials: "same-origin" });
      if (isJson(r)) return { page: await r.json(), model: `/library/${enc(pid)}/model.glb`, key: null };
    }
  } catch { /* ライブラリーにつながらない → 見本 */ }
  if (!pid) pid = (await (await fetch("/site/sample/list.json")).json()).items?.[0]?.id;
  const r = await fetch(`/site/sample/pages/${enc(pid)}.json`);
  if (!r.ok) throw new Error(String(r.status));
  return { page: await r.json(), model: `/site/sample/models/${enc(pid)}.glb`, key: pid };
}


function render(page, me) {
  const inv = page.invention;
  document.title = `${inv.name} | IMPOSSIBLE LIBRARY`;
  $("name").textContent = inv.name;
  $("name").classList.toggle("long", [...inv.name].length > 15);
  $("summary").textContent = inv.summary;
  // 名前の下の1行：見本の印と、分類・付ける所・版・部品と実験の数・公開日・発案者
  const meta = $("meta");
  if (page.sample) meta.append(el("span", "sample", "見本"));
  if (page.creator.publicId && live) {
    const a = el("a", null, `発案者 ${page.creator.name}`);
    a.href = `/creators/${enc(page.creator.publicId)}`;
    meta.append(a);
  } else meta.append(page.creator.publicId ? `発案者 ${page.creator.name}` : "発案者は匿名");
  meta.append("　" + [inv.tags.map((t) => t.label).join("・"), `${inv.mount.label}に付ける`, `Rev.${inv.revision}`,
    `部品 ${inv.partCount} 点`, `実験 ${inv.experimentCount} 回`, `${date(page.publishedAt)} 公開`].join("　"));
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

  if (page.detail) details(page, inv);
  else locked(page, inv, me);
  $("sample-note").textContent = page.sample
    ? "このページは見本です。IMPOSSIBLE LAB の通し確認で作った記録で、実在の発案者はいません。"
    : `${date(page.updatedAt)} 更新。公開を取り下げると、このページは一覧から消えます。`;
  demand(page);
  phraseBreaks(document.querySelector("main"));
  return inv;
}

/** 詳しい内容（ログインした開発者・公開した発案者・運営）：依頼の原文・改良の履歴・部品・しくみ・推定・課題・前提。 */
function details(page, inv) {
  $("request").textContent = inv.request || "依頼の文の記録なし";
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
  // 開発の課題（確かめられない要望を含む）。研究の欄（research）の理由のうち、課題にまだ無いものだけ足す
  const lines = [...inv.developer.challenges];
  for (const u of inv.research.unverifiable) if (!lines.some((l) => l.includes(u.reason))) lines.push(`${u.need}：${u.reason}`);
  for (const c of lines.slice(0, 4)) challenges.append(el("li", null, c));
  fillSlots(page, inv);
}

/**
 * だれでも見られる形：依頼の原文の板を、詳しい内容はログインした開発者が読めるという板に替える。
 * 改良の履歴・しくみ・部品・推定・課題・前提の板は出さず、写真だけを1つの盤に並べる。
 */
function locked(page, inv, me) {
  const tile = el("div", "t w6 h2 lock");
  tile.append(el("h2", null, "詳しい内容は開発者だけが読めます"),
    el("p", null, "依頼の原文、部品としくみ、実験の条件と数値、改良の履歴は、ログインした開発者に見せています。"));
  const acts = el("div", "acts");
  if (!live) tile.append(el("p", "note", "ログインは PC のサイトでできます。"));
  else if (!me) {
    // 新規登録は、となりの採用の板に置く
    const login = el("button", "btn s sm", "ログインして読む");
    login.type = "button";
    login.addEventListener("click", () => { location.href = `/login?next=${enc(location.pathname)}`; });
    acts.append(login);
    tile.append(acts);
  } else if (me.kind === "creator") tile.append(el("p", "note", "発案者のアカウントでは、ほかの人の発明の詳しい内容は読めません。"));
  $("request").closest(".t").replaceWith(tile);
  $("revisions").closest(".t").remove();
  $("adopt").closest(".t").classList.replace("w3", "w6");
  $("stage-hint").textContent = "押したまま動かすと回せます";

  // 写真と実験の記録（版・モード・主な計測・合否）の盤：2つ目の盤に4つの枠を集め、3つ目の盤は消す
  const board = $("board-how");
  board.removeAttribute("aria-labelledby");
  board.setAttribute("aria-label", "写真と実験の記録");
  for (const t of board.querySelectorAll(".how, .parts")) t.remove();
  board.append($("slot-c"), $("slot-d"));
  $("board-real").remove();
  const images = [...(page.images ?? [])];
  const order = ["experiment", "result", "stage"];
  images.sort((a, b) => order.indexOf(a.kind) - order.indexOf(b.kind));
  const slots = ["slot-a", "slot-b", "slot-c", "slot-d"].map((n) => $(n));
  const n = Math.min(images.length, 4);
  // 写真が1〜2枚なら記録と同じ行に（1枚は半分、2枚は4分の1ずつ）、3〜4枚なら写真で1行にして記録を下に
  const w = n === 1 ? "w6" : n === 3 ? "w4" : "w3";
  slots.forEach((slot, i) => {
    const img = images[i];
    if (!img) { slot.remove(); return; }
    slot.className = `t ${w} h2 pic` + (i === 0 ? " shutter" : "");
    const im = el("img");
    im.src = img.url; im.width = 720; im.height = 400; im.loading = "lazy"; im.alt = img.caption;
    slot.append(im, el("figcaption", null, img.caption));
  });
  if (inv.experiments.length) {
    const log = experimentLog(inv, el("div"));
    if (n === 0 || n >= 3) log.classList.replace("w6", "w12");
    board.append(log);
  } else if (n === 0) board.remove();
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

/** ほしいと採用の帯（ほしいは VR とサイトを合わせた人数）。ライブラリーにつながっていれば、このブラウザから1回ほしいを押せる。 */
function demand(page) {
  const figs = $("want-figs");
  const fig = (v, unit, label) => { const d = el("div"); const p = el("p", "v num", String(v)); p.append(el("small", null, unit)); d.append(p, el("p", null, label)); return d; };
  const fill = () => figs.replaceChildren(fig(page.wants, "人", "ほしいを押した人"), fig(page.adoptions, "件", "開発者の採用"));
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

// ---------------- 採用（登録した開発者が、理由を書いて採用する） ----------------
async function actions(page, me) {
  const tile = $("adopt").closest(".act");
  const text = tile.querySelector("p");
  const btn = $("adopt");
  const extra = el("div", "acts");
  const old = tile.querySelector(".acts");
  if (old) { old.before(btn); old.remove(); } // 採用のあと描き直すとき、ボタンを元の場所に戻す
  if (!live) {
    btn.hidden = true;
    text.textContent = "登録した開発者が、理由を書いて採用できます。採用は PC のサイトからできます。";
    return;
  }
  text.textContent = "登録した開発者が、理由を書いて採用できます。採用すると発案者に知らせが届きます。";
  const adopted = me?.kind === "account" && page.mine?.adopted;
  const considering = me?.kind === "account" && page.mine?.considering;
  btn.hidden = false;
  btn.onclick = null;
  if (adopted) {
    btn.hidden = true;
    const a = el("a", "btn", "やりとりを開く");
    a.href = page.mine.threadId ? `/messages/${page.mine.threadId}` : "/messages";
    extra.append(a);
    text.textContent = `${me.name}として採用しました。発案者とのやりとりが始まっています。`;
  } else if (me?.kind === "creator") {
    btn.hidden = true;
    // 詳しい内容を読めた発案者は、この発明を公開した本人
    text.textContent += page.detail ? " この発明はあなたが公開したものです。" : " 採用できるのは、登録した開発者です。";
  } else if (me?.kind === "operator") {
    btn.hidden = true;
    text.textContent += " 運営のアカウントでは採用できません。";
  } else if (!me) {
    btn.onclick = () => { location.href = `/login?next=${enc(location.pathname)}`; };
    const join = el("a", "txt", "開発者の新規登録");
    join.href = `/signup?next=${enc(location.pathname)}`;
    extra.append(join);
  } else {
    btn.onclick = () => adoptDialog(page, me);
    if (!considering) {
      const c = el("button", "txt", "検討中にする");
      c.type = "button";
      c.addEventListener("click", async () => {
        c.disabled = true;
        try { await api(`/api/library/${enc(page.id)}/consider`, { method: "POST", body: {} }); c.replaceWith(el("span", null, "検討中にしました")); }
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
  s.append(el("small", null, "必須。発案者に伝わります"));
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
  form.append(h, el("p", "note", `${me.name}（${userSub(me)}）として採用します。採用すると、名前・所属・専門・紹介が発案者に伝わり、2人だけのやりとりができます。`), label, err, acts);
  ta.addEventListener("input", () => { count.textContent = `${ta.value.length} / ${MAX}`; if (ta.value.trim()) err.textContent = ""; });
  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    const reason = ta.value.trim();
    if (!reason) { err.textContent = "採用する理由を書いてください。"; ta.focus(); return; }
    ok.disabled = true; err.textContent = "";
    try {
      await api(`/api/library/${enc(page.id)}/adopt`, { method: "POST", body: { reason } });
      dlg.close();
      const fresh = await api(`/api/library/${enc(page.id)}`);
      Object.assign(page, fresh);
      demand(page);
      phraseBreaks($("want"));
      await actions(page, me);
    } catch (x) { err.textContent = x.message; ok.disabled = false; }
  });
  dlg.append(form);
  dlg.addEventListener("close", () => dlg.remove());
  document.body.append(dlg);
  dlg.showModal();
  phraseBreaks(dlg);
  ta.focus();
}

// ---------------- 運営に知らせる（規約に反する内容を見つけたとき。ログインしなくてもよい） ----------------
const REPORT = [["rights", "他人の権利を侵している"], ["personal", "個人を特定できる情報が載っている"], ["danger", "危険な使い方を勧めている"], ["other", "そのほか"]];

function reportLink(page) {
  const b = el("button", "txt meta-btn", "運営に知らせる");
  b.type = "button";
  b.addEventListener("click", () => reportDialog(page, b));
  $("meta").append("　", b);
}

function reportDialog(page, opener) {
  const MAX = 400;
  const dlg = el("dialog", "dlg");
  dlg.setAttribute("aria-labelledby", "dlg-h");
  const form = el("form", "form");
  form.noValidate = true;
  const h = el("h2", null, "運営に知らせる");
  h.id = "dlg-h";
  const what = el("p", "note", `${page.invention.name}について、規約に反すると思う所を運営に知らせます。`);
  const fs = el("fieldset", "field");
  fs.append(el("legend", null, "知らせる理由"));
  const seg = el("div", "seg-r col");
  for (const [v, t] of REPORT) {
    const l = el("label"), i = el("input");
    i.type = "radio"; i.name = "reason"; i.value = v; i.checked = v === "rights";
    l.append(i, el("span", null, t));
    seg.append(l);
  }
  fs.append(seg);
  const label = el("label", "field");
  const s = el("span", null, "くわしく");
  s.append(el("small", null, `${MAX} 字まで。運営だけが読みます`));
  const ta = el("textarea");
  ta.rows = 3; ta.maxLength = MAX; ta.name = "detail";
  label.append(s, ta);
  const err = el("p", "err");
  err.setAttribute("role", "alert");
  const ok = el("button", "btn p sm", "知らせる");
  ok.type = "submit";
  const no = el("button", "btn s sm", "やめる");
  no.type = "button";
  no.addEventListener("click", () => dlg.close());
  const acts = el("div", "acts");
  acts.append(ok, no);
  form.append(h, what, fs, label, err, acts);
  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    ok.disabled = true; err.textContent = "";
    try {
      const d = new FormData(form);
      await api(`/api/library/${enc(page.id)}/report`, { method: "POST", body: { reason: d.get("reason"), detail: String(d.get("detail") ?? "").trim() } });
      form.replaceChildren(h, el("p", null, "運営に知らせました。運営が確かめて、規約に反していれば公開を取り下げます。"));
      phraseBreaks(form);
      const close = el("button", "btn s sm", "閉じる");
      close.type = "button";
      close.addEventListener("click", () => dlg.close());
      const a2 = el("div", "acts");
      a2.append(close);
      form.append(a2);
      close.focus();
      opener.replaceWith(el("span", null, "運営に知らせました"));
    } catch (x) { err.textContent = x.message; ok.disabled = false; }
  });
  dlg.append(form);
  dlg.addEventListener("close", () => dlg.remove());
  document.body.append(dlg);
  dlg.showModal();
  phraseBreaks(dlg);
}

/** 推定の板（電力・時間）の代わりに、すべての実験の記録（版・モード・数値・合否）を出す。 */
function experimentLog(inv, tile = $("power").closest(".t")) {
  tile.className = "t w6 h2 notes log";
  tile.replaceChildren(el("h2", null, "実験の記録"));
  const ol = el("ol");
  for (const e of inv.experiments.slice(-6)) {
    const m = e.metrics.find((x) => x.primary);
    const pass = e.passed === true ? "合格" : e.passed === false ? "不合格" : "判定なし";
    ol.append(el("li", null, `Rev.${e.revision}　${e.mode === "reality" ? "REALITY" : "DREAM"}　${e.title}　${m ? `${m.label} ${m.display} ${m.unit}` : ""}　${pass}`));
  }
  tile.append(ol);
  return tile;
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
  // 頭の上に付けるものは、床に置かずに少し下から見上げる
  const floating = inv.mount?.id === "head";
  let center = new THREE.Vector3(), radius = 0.5, yaw = 0.5, pitch = floating ? -0.2 : 0.32, manual = false, picked = null, visible = true;
  // 影と光（読み込んだあと）と、すき間の陰り（読めなければ陰りなしで描く）
  let lights = null, ao = null;
  const aoReady = createAO(renderer, scene, cam).then((x) => { ao = x; }, () => {});
  const partNames = new Set((inv.parts ?? []).map((p) => p.id));
  const ownerOf = (o) => { for (let x = o; x; x = x.parent) if (partNames.has(x.name)) return x.name; return null; };

  loadModel(url, key).then((root) => {
    scene.add(root);
    root.traverse((o) => { if (o.isMesh) meshes.push({ mesh: o, part: ownerOf(o), color: o.material.color.clone(), emissive: o.material.emissive.clone() }); });
    const m = measure(root);
    center = m.center; radius = m.fit;
    $("size").textContent = `幅 ${(m.halfWidth * 2).toFixed(2)} m　高さ ${m.size.y.toFixed(2)} m`;
    if (!floating) scene.add(stageRing(m.center, m.floor, m.halfWidth));
    dress(scene, root, m, { floating }).then((l) => { lights = l; frame(); });
    aoReady.then(() => { ao?.setRadius(m.radius); ao?.setSize(canvas.clientWidth, canvas.clientHeight); frame(); });
    frame();
  }, () => { $("fallback").hidden = false; });

  function frame() {
    const w = canvas.clientWidth, h = canvas.clientHeight;
    if (canvas.width !== Math.round(w * renderer.getPixelRatio()) || canvas.height !== Math.round(h * renderer.getPixelRatio())) {
      renderer.setSize(w, h, false); cam.aspect = w / h; cam.updateProjectionMatrix();
      ao?.setSize(w, h);
    }
    aim(cam, center, radius, yaw, pitch);
    lights?.light(yaw);
    if (ao) ao.render(); else renderer.render(scene, cam);
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
  const { user } = await mountAccount();
  const src = await resolveSource(user);
  live = src.key === null;
  const inv = render(src.page, user);
  stage($("canvas"), src.model, src.key, inv);
  actions(src.page, user).catch(() => {});
  if (live) reportLink(src.page);
} catch {
  $("name").textContent = "この発明を読み込めません";
  $("summary").textContent = "公開を取り下げられたか、ライブラリーにつながっていません。";
}
entrance();
