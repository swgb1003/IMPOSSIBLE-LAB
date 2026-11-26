// 送ったメール（このデモではメールを外へ送らない）。左に一覧、右に読んでいる1通。リンクはこのサイトの中だけ。
import { $, el, phraseBreaks } from "./text.js";
import { api, mountAccount, stamp } from "./session.js";

const phone = () => matchMedia("(max-width: 760px)").matches;
let items = [], selected = null;

function draw() {
  const ul = $("list");
  ul.replaceChildren();
  $("count").textContent = `${items.length} 通`;
  if (items.length === 0) ul.append(el("li", "note", "まだ送ったメールはありません。"));
  for (const m of items) {
    const b = el("button", "th-item");
    b.type = "button";
    if (m.id === selected) b.setAttribute("aria-current", "true");
    b.append(el("b", null, m.subject), el("span", "when", stamp(m.at)), el("span", "who2", m.to));
    b.addEventListener("click", () => { selected = m.id; draw(); });
    const li = el("li");
    li.append(b);
    ul.append(li);
  }
  const pane = $("pane");
  pane.replaceChildren();
  document.body.classList.toggle("picked", Boolean(selected));
  if (phone()) {
    const back = el("button", "txt phone-only", "一覧に戻る");
    back.type = "button";
    back.addEventListener("click", () => { selected = null; draw(); });
    pane.append(back);
  }
  const m = items.find((x) => x.id === selected);
  if (!m) { pane.append(el("h2", null, "メールを選んでください"), el("p", "note", "左の一覧から選ぶと、本文とリンクが出ます。")); return; }
  const kv = el("dl", "kv");
  const row = (k, v) => { const d = el("div"); d.append(el("dt", null, k), el("dd", null, v)); kv.append(d); };
  row("宛先", m.to);
  row("送った日時", stamp(m.at));
  pane.append(el("h2", null, m.subject), kv);
  const body = el("div", "mail-body");
  // 本文のリンクの行だけ、このサイトの中のリンクにする（ほかは文字のまま）
  for (const line of m.body.split("\n")) {
    if (line === m.link && /^\/(verify|reset)#token=[A-Za-z0-9_-]+$/.test(line)) {
      const a = el("a", null, `${location.origin}${line}`);
      a.href = line;
      const p = el("p");
      p.append(a);
      body.append(p);
    } else body.append(el("p", line ? null : "gap", line));
  }
  pane.append(body);
  phraseBreaks(pane);
}

const s = await mountAccount();
if (!s.live) $("lead").textContent = "ライブラリーにつながりません。PC の中継のプログラムが動いているか確かめてください。";
else {
  items = (await api("/api/mail")).items;
  if (!phone()) selected = items[0]?.id ?? null;
  draw();
}
phraseBreaks(document.querySelector("main"));
