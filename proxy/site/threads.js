// やりとり（/messages と /messages/<ID>）：ログインした人（開発者か発案者）が加わっている連絡の一覧（左）と、読んでいる1件（右）。
// 発案者は Quest からも返事を送るので、開いている間は数秒ごとに読み直す。
import { $, el, date, phraseBreaks } from "./text.js";
import { api, mountAccount, stamp } from "./session.js";

const MAX = 500;
const phone = () => matchMedia("(max-width: 760px)").matches;
let state, items = [], open = null;
const pathId = () => /^\/messages\/([a-z0-9-]{3,40})$/.exec(location.pathname)?.[1] ?? null;
let selected = pathId();

const mine = (m) => (state.me.kind === "account" ? m.from === "adopter" : m.from === "creator");
/** 相手が返事を待っているか（最後に書いたのが相手。発案者には、採用の理由がまだ返事のない最初の言葉になる）。 */
const yourTurn = (t) => (state.me.kind === "creator" ? t.lastFrom === "adopter" || t.messageCount === 0 : t.lastFrom === "creator");
const other = (t) => (state.me.kind === "creator" ? (t.adopter.affiliation ? `${t.adopter.name}（${t.adopter.affiliation}）` : t.adopter.name) : `発案者　${t.creator.name}`);

function go(id) {
  history.pushState(null, "", id ? `/messages/${id}` : "/messages");
  selected = id;
  draw();
}
addEventListener("popstate", () => { selected = pathId(); draw(); });

function drawList() {
  const waiting = items.filter(yourTurn).length;
  $("lead").textContent = waiting ? `返事を待っているやりとりが ${waiting} 件あります。` : "返事を待っているやりとりはありません。";
  $("count").textContent = `${items.length} 件`;
  const ul = $("threads");
  ul.replaceChildren();
  if (items.length === 0) {
    ul.append(el("li", "note", state.me.kind === "creator"
      ? "まだ採用されていません。開発者が採用すると、ここにその人とのやりとりができます。"
      : "まだ発明を採用していません。発明のページで採用すると、発案者とのやりとりができます。"));
    return;
  }
  for (const t of items) {
    const a = el("a", "th-item");
    a.href = `/messages/${t.id}`;
    if (t.id === selected) a.setAttribute("aria-current", "true");
    a.append(el("b", null, t.pubName), el("span", "when", stamp(t.lastAt ?? t.createdAt)), el("span", "who2", other(t)));
    a.append(t.unread > 0 ? el("span", "mark-wait state", `新しい返事 ${t.unread} 通`)
      : t.closed ? el("span", "state count", "終わったやりとり")
      : yourTurn(t) ? el("span", "mark-wait state", "返事を待っています") : el("span", "state count", `${t.messageCount} 通`));
    a.addEventListener("click", (e) => { if (e.metaKey || e.ctrlKey) return; e.preventDefault(); go(t.id); });
    const li = el("li");
    li.append(a);
    ul.append(li);
  }
}

function msgBlock(t, m) {
  const b = el("div", "msg" + (mine(m) ? " mine" : ""));
  const from = el("div", "from");
  const name = m.from === "adopter" ? t.adopter.name : t.creator.name;
  from.append(el("b", null, mine(m) ? `${name}（あなた）` : name), el("span", null, stamp(m.at)));
  b.append(from, el("p", null, m.text));
  return b;
}

function drawThread(pane, t) {
  const h = el("h2");
  const link = el("a", null, t.pubName);
  link.href = `/inventions/${t.pubId}`;
  h.append(link);
  // 発案者には、下に採用した人の登録の内容を並べるので、見出しの下は名前だけ
  pane.append(h, el("p", "sub", state.me.kind === "creator" ? `採用した開発者　${t.adopter.name}` : other(t)));
  // 発案者には、採用した人の登録の内容を見せる（ほかの人には見せない）
  if (state.me.kind === "creator" && !t.adopter.gone) {
    const kv = el("dl", "kv partner");
    const row = (k, v) => { if (!v) return; const d = el("div"); d.append(el("dt", null, k), el("dd", null, v)); kv.append(d); };
    row("所属", t.adopter.affiliation);
    row("専門の分野", t.adopter.field);
    row("紹介", t.adopter.intro);
    row("所属の確認", t.adopter.verified ? "運営が確かめました" : "運営はまだ確かめていません");
    pane.append(kv);
  }
  // 採用の理由が、このやりとりの最初の言葉になる
  const r = el("div", "reason");
  const by = `${t.adopter.name}が${date(t.createdAt)}に採用した理由`;
  r.append(el("h3", null, by), el("p", null, t.reason));
  pane.append(r);
  const list = el("div", "msgs");
  list.dataset.slot = "msgs";
  if (t.messages.length === 0) list.append(el("p", "note msg", state.me.kind === "creator" ? "まだ言葉はありません。採用の理由への返事を書いてください。" : "まだ言葉はありません。発案者が Quest かこのサイトで返事を書くと、ここに出ます。"));
  for (const m of t.messages) list.append(msgBlock(t, m));
  pane.append(list);
  if (t.closed) { pane.append(el("p", "note compose", "採用した人が退会したため、このやりとりは終わりました。")); return; }

  const form = el("form", "compose");
  const label = el("label", "field");
  const ta = el("textarea");
  ta.rows = 4; ta.maxLength = MAX; ta.name = "text";
  label.append(el("span", null, state.me.kind === "creator" ? "返事を書く" : "発案者に書く"), ta);
  const count = el("span", "count", `0 / ${MAX}`);
  const err = el("p", "err");
  err.setAttribute("role", "alert");
  const send = el("button", "btn p sm", "送る");
  send.type = "submit";
  const acts = el("div", "acts");
  acts.append(count, send);
  form.append(label, err, acts);
  ta.addEventListener("input", () => { count.textContent = `${ta.value.length} / ${MAX}`; });
  ta.addEventListener("keydown", (e) => { if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) form.requestSubmit(); });
  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    const text = ta.value.trim();
    if (!text) { err.textContent = "本文を書いてください。"; return; }
    send.disabled = true; err.textContent = "";
    try {
      await api(`/api/threads/${t.id}/messages`, { method: "POST", body: { text } });
      ta.value = ""; count.textContent = `0 / ${MAX}`;
      await refresh(true);
    } catch (x) { err.textContent = x.message; }
    send.disabled = false;
    ta.focus();
  });
  pane.append(form);
}

async function draw() {
  drawList();
  const pane = $("pane");
  if (!selected && !phone()) selected = items[0]?.id ?? null;
  document.body.classList.toggle("picked", Boolean(selected));
  drawList();
  pane.replaceChildren();
  if (phone()) {
    const b = el("button", "txt phone-only", "一覧に戻る");
    b.type = "button";
    b.addEventListener("click", () => go(null));
    pane.append(b);
  }
  if (!selected) {
    pane.append(el("h2", null, "やりとりはまだありません"), el("p", "note", "採用されると、採用した人ごとにここにやりとりができます。"));
    open = null;
  } else {
    try {
      open = await api(`/api/threads/${encodeURIComponent(selected)}`);
      mountAccount(); // 読んだので、上の帯の知らせの数を直す
      drawThread(pane, open);
    } catch (x) {
      open = null;
      pane.append(el("h2", null, "このやりとりは読めません"), el("p", "note", x.status === 404 ? "このアカウントが加わっていないやりとりか、アドレスが違います。" : x.message));
    }
  }
  phraseBreaks(document.querySelector("main"));
}

/** 読み直す。新しい言葉があれば、書きかけの文を残したまま、言葉の欄だけ入れ替える。 */
async function refresh(force = false) {
  try {
    items = sortItems((await api("/api/threads")).items);
    drawList();
    if (open) {
      const t = await api(`/api/threads/${encodeURIComponent(open.id)}`);
      if (force || t.messages.length !== open.messages.length) {
        open = t;
        const list = document.querySelector('[data-slot="msgs"]');
        list.replaceChildren(...t.messages.map((m) => msgBlock(t, m)));
        list.lastElementChild?.scrollIntoView({ block: "nearest" });
      }
    }
    phraseBreaks(document.querySelector("main"));
  } catch { /* 次に読み直すときにもう一度 */ }
}

const sortItems = (xs) => [...xs].sort((a, b) => (b.lastAt ?? b.createdAt).localeCompare(a.lastAt ?? a.createdAt));

const session = await mountAccount();
state = { ...session, me: session.user };
if (!state.live) {
  $("lead").textContent = "ライブラリーにつながりません。この画面は PC のサイト（中継のプログラム）で開いてください。";
  $("pane").remove();
  $("list-note").remove();
} else if (!state.me) {
  location.replace(`/login?next=${encodeURIComponent(location.pathname)}`);
} else {
  items = sortItems((await api("/api/threads")).items);
  await draw();
  setInterval(() => { if (document.visibilityState === "visible") refresh(); }, 8000);
}
