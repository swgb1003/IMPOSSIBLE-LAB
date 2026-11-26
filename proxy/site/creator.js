// 発案者のページ：表示名で公開した発明だけで作る（公開した数・採用された数・発明の一覧）。
import { $, el, date, phraseBreaks } from "./text.js";
import { api, mountAccount } from "./session.js";

const STATUS = { open: "公開中", considering: "検討中", adopted: "採用" };
const id = location.pathname.split("/")[2] ?? "";

const fig = (v, unit, label) => {
  const d = el("div");
  const p = el("p", "v num", String(v));
  p.append(el("small", null, unit));
  d.append(p, el("p", null, label));
  return d;
};

await mountAccount();
try {
  const c = await api(`/api/creators/${encodeURIComponent(id)}`);
  document.title = `${c.displayName} | IMPOSSIBLE LIBRARY`;
  $("name").textContent = c.displayName;
  $("figs").append(fig(c.published, "件", "公開した発明"), fig(c.adopted, "件", "採用された発明"));
  const tb = $("rows");
  for (const p of c.inventions) {
    const tr = el("tr");
    const a = el("a", null, p.name);
    a.href = `/inventions/${p.id}`;
    const name = el("td");
    const nb = el("b");
    nb.append(a);
    name.append(nb, el("span", "sub", p.summary));
    const st = el("td");
    st.append(el("span", p.status === "adopted" ? "now nw" : "nw", STATUS[p.status] ?? p.status));
    const w = el("td", "num", String(p.wants)), n = el("td", "num", String(p.adoptions)), d = el("td", "num nw", date(p.publishedAt));
    st.dataset.l = "状態"; w.dataset.l = "ほしい"; n.dataset.l = "採用"; d.dataset.l = "公開";
    tr.append(name, st, w, n, d);
    tb.append(tr);
  }
} catch (x) {
  $("name").textContent = "この発案者は見つかりません";
  $("lead").textContent = x.status === 0 ? x.message : "公開を取り下げたか、匿名で公開している発案者です。";
  document.querySelector(".pane").remove();
}
phraseBreaks(document.querySelector("main"));
