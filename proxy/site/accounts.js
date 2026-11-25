// 研究者と開発者：登録している人の一覧（左）と、選んだ人か登録の欄（右）。
// ログインは形だけ：人を選んで「この人として操作する」と、このブラウザの名乗りになる。
import { $, el, date, phraseBreaks } from "./text.js";
import { api, mountWho, setAs, roleLabel } from "./who.js";

const params = new URLSearchParams(location.search);
// 採用の画面から来たとき、名乗ったあとに戻る先（このサイトの発明のページだけ）
const back = /^invention\.html\?id=[a-z0-9-]{3,40}$/.test(params.get("back") ?? "") ? params.get("back") : null;
const phone = () => matchMedia("(max-width: 760px)").matches;
let state;
let selected = params.get("id");
let mode = params.has("new") ? "new" : "view";

function go(next) {
  const q = new URLSearchParams();
  if (next.id) q.set("id", next.id);
  if (next.mode === "new") q.set("new", "");
  if (back) q.set("back", back);
  history.pushState(null, "", `accounts.html${q.size ? "?" + q.toString() : ""}`);
  selected = next.id ?? null; mode = next.mode ?? "view";
  draw();
}
addEventListener("popstate", () => { const p = new URLSearchParams(location.search); selected = p.get("id"); mode = p.has("new") ? "new" : "view"; draw(); });

function lead() {
  const n = state.accounts.length, v = state.accounts.filter((a) => a.verified).length;
  const p = $("lead");
  p.replaceChildren();
  if (back && state.me?.kind !== "account") {
    p.append("採用するには、研究者か開発者として名乗ってください。一覧から自分を選ぶか、登録してください。");
    return;
  }
  if (back && state.me?.kind === "account") {
    p.append(el("b", null, `${state.me.name}として名乗っています。`), " ");
    const a = el("a", null, "発明のページに戻って採用する");
    a.href = back;
    p.append(a);
    return;
  }
  p.append(`登録 ${n} 人、運営が所属を確かめた人 ${v} 人。登録した人だけが、理由を書いて発明を採用できます。`);
}

function drawList() {
  $("count").textContent = `${state.accounts.length} 人`;
  const ul = $("people");
  ul.replaceChildren();
  if (state.accounts.length === 0) {
    ul.append(el("li", "note", "まだだれも登録していません。右の欄から登録できます。"));
    return;
  }
  for (const a of state.accounts) {
    const li = el("li");
    const row = el("a", "person");
    row.href = `accounts.html?id=${a.id}${back ? `&back=${encodeURIComponent(back)}` : ""}`;
    if (a.id === selected && mode === "view") row.setAttribute("aria-current", "true");
    const name = el("span", "p-name");
    name.append(el("b", null, a.name), el("span", null, a.affiliation || "所属なし"));
    const n = el("span", "p-n num");
    n.append(el("small", "p-n-l", "採用"), String(a.adoptions ?? 0));
    n.append(el("small", null, "件"));
    const mark = el("span", "p-mark");
    mark.append(a.sample ? el("span", "mark-sample", "見本") : a.verified ? el("span", "mark-ok", "確認済み") : el("span", "mark-off", "未確認"));
    if (a.sample && a.verified) mark.prepend(el("span", "mark-ok", "確認済み"), " ");
    row.append(name, el("span", "p-role", roleLabel(a.role)), el("span", "p-field", a.field || "—"), n, mark);
    row.addEventListener("click", (e) => { if (e.metaKey || e.ctrlKey) return; e.preventDefault(); go({ id: a.id }); });
    li.append(row);
    ul.append(li);
  }
}

async function drawPerson(pane, id) {
  let a;
  try { a = await api(`/accounts/${encodeURIComponent(id)}`); } catch (e) {
    pane.append(el("h2", null, "この人は見つかりません"), el("p", "note", e.message));
    return;
  }
  pane.append(el("h2", null, a.name), el("p", "sub", [a.affiliation, roleLabel(a.role)].filter(Boolean).join("・")));
  if (a.intro) pane.append(el("p", "intro", a.intro));
  const kv = el("dl", "kv");
  const add = (k, v) => { const d = el("div"); d.append(el("dt", null, k), el("dd", null, v)); kv.append(d); };
  add("専門", a.field || "—");
  add("所属の確認", a.verified ? "運営が所属を確かめた" : "まだ確かめていない");
  add("登録", date(a.createdAt));
  pane.append(kv);
  pane.append(el("h3", null, `採用した発明　${a.adopted.length} 件`));
  if (a.adopted.length === 0) pane.append(el("p", "note", "まだ採用した発明はありません。"));
  else {
    const links = el("div", "links");
    for (const p of a.adopted) {
      const l = el("a");
      l.href = `invention.html?id=${p.id}`;
      l.append(el("b", null, p.name), el("span", null, `${date(p.at)} 採用`));
      links.append(l);
    }
    pane.append(links);
  }
  if (a.sample) pane.append(el("p", "note", "この人は見本です。実在の人と所属ではありません。"));
  const foot = el("div", "foot-act");
  if (state.as === `account:${a.id}`) {
    foot.append(el("p", "now", "いまこの人として操作しています"));
    const off = el("button", "txt", "名乗りをやめる");
    off.type = "button";
    off.addEventListener("click", () => { setAs(""); location.reload(); });
    foot.append(off);
    if (back) { const b = el("a", "btn p sm", "発明のページに戻る"); b.href = back; foot.prepend(b); }
  } else {
    const b = el("button", "btn p sm", "この人として操作する");
    b.type = "button";
    b.addEventListener("click", () => { setAs(`account:${a.id}`); if (back) location.href = back; else location.reload(); });
    foot.append(b);
  }
  pane.append(foot);
}

function drawForm(pane) {
  pane.append(el("h2", null, "研究者・開発者として登録する"));
  const form = el("form", "form");
  form.noValidate = true;
  const field = (label, name, max, { area = false, hint = "" } = {}) => {
    const f = el("label", "field");
    const s = el("span", null, label);
    if (hint) s.append(el("small", null, hint));
    const input = el(area ? "textarea" : "input");
    input.name = name; input.maxLength = max;
    if (area) input.rows = 3; else input.type = "text";
    f.append(s, input);
    return f;
  };
  const role = el("fieldset", "field");
  role.append(el("legend", null, "立場"));
  const seg = el("div", "seg-r");
  for (const [v, t] of [["researcher", "研究者"], ["developer", "開発者"]]) {
    const l = el("label"), i = el("input");
    i.type = "radio"; i.name = "role"; i.value = v; i.checked = v === "researcher";
    l.append(i, el("span", null, t));
    seg.append(l);
  }
  role.append(seg);
  form.append(
    field("名前", "name", 40, { hint: "必須" }),
    field("所属", "affiliation", 60, { hint: "大学・会社・個人など" }),
    role,
    field("専門の分野", "field", 60),
    field("ひとことの紹介", "intro", 200, { area: true, hint: "200 字まで" }),
  );
  const err = el("p", "err");
  err.setAttribute("role", "alert");
  const acts = el("div", "acts");
  const ok = el("button", "btn p sm", "登録する");
  ok.type = "submit";
  const cancel = el("button", "btn s sm", "やめる");
  cancel.type = "button";
  cancel.addEventListener("click", () => go({ id: state.accounts[0]?.id ?? null }));
  acts.append(ok, cancel);
  form.append(el("p", "note", "パスワードはありません。登録すると、この人として名乗り、採用とやりとりができます。"), err, acts);
  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    const d = new FormData(form);
    const body = { name: String(d.get("name")).trim(), affiliation: String(d.get("affiliation")).trim(), role: d.get("role"), field: String(d.get("field")).trim(), intro: String(d.get("intro")).trim() };
    if (!body.name) { err.textContent = "名前を書いてください。"; form.elements.name.focus(); return; }
    ok.disabled = true;
    try {
      const a = await api("/accounts", { method: "POST", body });
      setAs(`account:${a.id}`);
      location.href = back ?? `accounts.html?id=${a.id}`;
    } catch (x) { err.textContent = x.message; ok.disabled = false; }
  });
  pane.append(form);
  form.elements.name.focus({ preventScroll: true });
}

async function draw() {
  lead();
  drawList();
  const pane = $("pane");
  pane.replaceChildren();
  if (!selected && mode !== "new" && !phone()) {
    selected = state.me?.kind === "account" ? state.me.id : state.accounts[0]?.id ?? null;
    if (!selected) mode = "new";
    drawList();
  }
  document.body.classList.toggle("picked", Boolean(selected) || mode === "new");
  if (phone()) {
    const b = el("button", "txt phone-only", "一覧に戻る");
    b.type = "button";
    b.addEventListener("click", () => go({}));
    pane.append(b);
  }
  if (mode === "new") drawForm(pane);
  else if (selected) await drawPerson(pane, selected);
  phraseBreaks(document.querySelector("main"));
}

state = await mountWho();
$("new").addEventListener("click", () => go({ mode: "new" }));
if (!state.live) {
  $("lead").textContent = "ライブラリにつながりません。この画面は PC のサイト（中継のプログラム）で開いてください。";
  $("new").remove();
  $("pane").remove();
  $("list-note").remove();
} else {
  await draw();
}
