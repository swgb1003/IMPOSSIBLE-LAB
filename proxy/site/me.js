// 自分のページ。開発者：登録の内容（採用した発明の発案者にだけ伝わる）と、採用した発明・検討中の発明。
// 発案者：公開した発明と、採用・やりとりの数。運営は運営の画面へ。
import { $, el, date, phraseBreaks } from "./text.js";
import { api, errLine, field, mountAccount, stamp } from "./session.js";

const STATUS = { open: "公開中", considering: "検討中", adopted: "採用", withdrawn: "取り下げ" };
const welcome = new URLSearchParams(location.search).has("welcome");
if (welcome) history.replaceState(null, "", "/me");

const s = await mountAccount();
if (!s.live) {
  $("lead").textContent = "ライブラリーにつながりません。PC の中継のプログラムが動いているか確かめてください。";
  for (const k of ["main", "side"]) $(k).remove();
} else if (s.user?.kind === "operator") {
  location.replace("/admin");
} else {
  const me = await api("/api/me");
  if (me.kind === "account") drawAccount(me);
  else drawCreator(me);
}
phraseBreaks(document.querySelector("main"));

function drawAccount(me) {
  $("name").textContent = me.name;
  $("lead").textContent = welcome
    ? "登録しました。発明のページから、理由を書いて採用できます。"
    : [me.affiliation, me.verified ? "所属は確認済み" : "", `${date(me.createdAt)}に登録`].filter(Boolean).join("・");
  if (!me.emailVerified) drawVerify(me);
  drawProfile($("main"), me);
  drawActivity($("side"), me);
}

/** メールアドレスをまだ確かめていないとき：確認のメールを送り直せる。 */
function drawVerify(me) {
  const box = $("verify");
  box.hidden = false;
  const p = el("p", null, `${me.email} にお送りしたメールのリンクを開くと、メールアドレスの確認が終わります。`);
  const acts = el("div", "acts");
  const b = el("button", "btn s sm", "確認のメールを送り直す");
  b.type = "button";
  const done = el("span", "note", "");
  done.setAttribute("role", "status");
  b.addEventListener("click", async () => {
    b.disabled = true;
    try { await api("/api/verify-email/resend", { method: "POST", body: {} }); done.textContent = "送り直しました。"; }
    catch (x) { done.textContent = x.message; b.disabled = false; }
  });
  const mail = el("a", "txt", "送ったメールを見る");
  mail.href = "/mail";
  acts.append(b, mail, done);
  box.append(el("h2", null, "メールアドレスをまだ確かめていません"), p, acts);
}

function drawProfile(pane, me) {
  pane.append(el("h2", null, "登録の内容"));
  const form = el("form", "form");
  form.noValidate = true;
  form.append(
    field("名前", "name", { max: 40, hint: "必須", auto: "name", value: me.name, required: true }),
    field("所属", "affiliation", { max: 60, auto: "organization", value: me.affiliation }),
    field("専門の分野", "field", { max: 60, value: me.field }),
    field("ひとことの紹介", "intro", { max: 200, area: true, hint: "200 字まで", value: me.intro }),
    el("p", "note", "名前・所属・専門・紹介は、あなたが採用した発明の発案者にだけ伝わります。"),
  );
  const kv = el("dl", "kv");
  const row = (k, v) => { const d = el("div"); d.append(el("dt", null, k), el("dd", null, v)); kv.append(d); };
  row("メールアドレス", `${me.email}（ほかの人には見せません）`);
  const err = errLine();
  const ok = el("button", "btn p sm", "保存する");
  ok.type = "submit";
  const saved = el("span", "note", "");
  saved.setAttribute("role", "status");
  const acts = el("div", "acts");
  acts.append(ok, saved);
  form.append(kv, err, acts);
  form.addEventListener("input", () => { saved.textContent = ""; });
  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    const d = new FormData(form);
    const body = {
      name: String(d.get("name")).trim(), affiliation: String(d.get("affiliation")).trim(), field: String(d.get("field")).trim(), intro: String(d.get("intro")).trim(),
    };
    if (!body.name) { err.textContent = "名前を書いてください。"; form.elements.name.focus(); return; }
    ok.disabled = true; err.textContent = "";
    try {
      const r = await api("/api/me", { method: "POST", body });
      $("name").textContent = r.name;
      saved.textContent = "保存しました。";
      await mountAccount();
    } catch (x) { err.textContent = x.message; }
    ok.disabled = false;
  });
  pane.append(form);
}

function linkList(items, sub, href) {
  const links = el("div", "links");
  for (const p of items) {
    const a = el("a");
    a.href = href(p);
    a.append(el("b", null, p.name), el("span", null, sub(p)));
    links.append(a);
  }
  return links;
}

function drawActivity(pane, me) {
  pane.append(el("h2", null, `採用した発明　${me.adopted.length} 件`));
  if (me.adopted.length === 0) {
    const p = el("p", "note gap-t");
    const a = el("a", null, "発明品一覧");
    a.href = "/inventions";
    p.append("まだ採用した発明はありません。", a, "から探せます。");
    pane.append(p);
  } else pane.append(linkList(me.adopted, (p) => `${date(p.at)}に採用・やりとりを開く`, (p) => `/messages/${p.threadId}`));
  pane.append(el("h3", null, `検討中の発明　${me.considering.length} 件`));
  if (me.considering.length === 0) pane.append(el("p", "note", "検討中にした発明はありません。"));
  else pane.append(linkList(me.considering, (p) => `${date(p.at)}に検討中にした`, (p) => `/inventions/${p.id}`));
}

function drawCreator(me) {
  $("name").textContent = me.named ? me.name : "発案者";
  $("lead").textContent = me.email ? `${me.email} でログインしています。` : "Quest の番号で入っています。";
  if (me.publicId) { const a = el("a", "btn s sm", "ほかの人から見えるページ"); a.href = `/creators/${me.publicId}`; $("head-acts").append(a); }
  const main = $("main");
  main.className = "t span12 pane";
  $("side").remove();
  main.append(el("div", "blk-h"));
  main.firstChild.append(el("h2", null, "公開した発明"), el("span", "num", `${me.inventions.length} 件`));
  if (me.inventions.length === 0) { main.append(el("p", "note", "まだ公開した発明はありません。Quest の保存の画面から公開できます。")); return; }
  const tbl = el("table", "tbl");
  const head = el("thead");
  const hr = el("tr");
  for (const [t, c] of [["発明", ""], ["状態", ""], ["ほしい", "num"], ["採用", "num"], ["やりとり", ""]]) hr.append(el("th", c || null, t));
  head.append(hr);
  const tb = el("tbody");
  for (const p of me.inventions) {
    const tr = el("tr");
    const name = el("td");
    const b = el("b");
    if (p.status === "withdrawn") b.textContent = p.name;
    else { const a = el("a", null, p.name); a.href = `/inventions/${p.id}`; b.append(a); }
    name.append(b, el("span", "sub", `Rev.${p.revision}・${date(p.publishedAt)}に公開${p.sample ? "・見本" : ""}`));
    const st = el("td");
    st.append(el("span", p.status === "adopted" ? "now nw" : "nw", p.status === "withdrawn" && p.withdrawnBy === "admin" ? "運営が取り下げ" : STATUS[p.status] ?? p.status));
    const w = el("td", "num", String(p.wants.vr + p.wants.site)), n = el("td", "num", String(p.adoptions.length));
    const th = el("td");
    if (p.adoptions.length === 0) th.append(el("span", "note", "まだありません"));
    for (const a of p.adoptions) {
      const l = el("a", "th-link", `${a.adopter.name}${a.messageCount ? `（${a.messageCount} 通・${stamp(a.lastAt ?? a.at)}）` : ""}`);
      l.href = `/messages/${a.threadId}`;
      th.append(l);
    }
    st.dataset.l = "状態"; w.dataset.l = "ほしい"; n.dataset.l = "採用"; th.dataset.l = "やりとり";
    tr.append(name, st, w, n, th);
    tb.append(tr);
  }
  tbl.append(head, tb);
  main.append(tbl);
}
