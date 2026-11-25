// 運営：公開された発明の取り下げ（発案者は公開し直せない）と、研究者・開発者の所属の確認の印（形だけ）。
import { $, el, date, phraseBreaks } from "./text.js";
import { api, roleLabel } from "./who.js";

const STATUS = { open: "公開中", considering: "検討中", adopted: "採用", withdrawn: "取り下げ" };

async function load() {
  const [pubs, accounts] = await Promise.all([api("/admin/library").then((r) => r.items), api("/accounts").then((r) => r.items)]);
  const live = pubs.filter((p) => p.status !== "withdrawn").length;
  const unverified = accounts.filter((a) => !a.verified).length;
  $("lead").textContent = `公開中 ${live} 件、取り下げ ${pubs.length - live} 件。所属をまだ確かめていない人が ${unverified} 人います。`;
  $("pubs-count").textContent = `${pubs.length} 件`;
  $("acc-count").textContent = `${accounts.length} 人`;
  drawPubs(pubs);
  drawAccounts(accounts);
  phraseBreaks(document.querySelector("main"));
}

function drawPubs(pubs) {
  const tb = $("pubs");
  tb.replaceChildren();
  for (const p of pubs) {
    const tr = el("tr");
    const name = el("td");
    if (p.status === "withdrawn") name.append(el("b", null, p.name));
    else { const a = el("a", null, p.name); a.href = `invention.html?id=${p.id}`; const b = el("b"); b.append(a); name.append(b); }
    name.append(el("span", "sub", (p.sample ? "見本・" : "") + p.creator.name));
    const st = el("td");
    if (p.status === "withdrawn") {
      st.append(el("span", null, p.withdrawnBy === "admin" ? "運営が取り下げ" : "発案者が取り下げ"));
      if (p.withdrawReason) st.append(el("span", "sub", p.withdrawReason));
    } else st.append(el("span", "nw", STATUS[p.status] ?? p.status));
    const act = el("td");
    if (p.status !== "withdrawn") {
      const b = el("button", "txt", "取り下げる");
      b.type = "button";
      b.setAttribute("aria-label", `${p.name}を取り下げる`);
      b.addEventListener("click", () => confirmTakedown(tr, p));
      act.append(b);
    } else act.append(el("span", "note", "—"));
    const n = el("td", "num", String(p.adoptions)), d = el("td", "num nw", date(p.publishedAt));
    st.dataset.l = "状態"; n.dataset.l = "採用"; d.dataset.l = "公開"; // 電話では列の見出しの代わりに出す
    tr.append(name, st, n, d, act);
    tb.append(tr);
  }
}

/** 取り下げの確かめ：発明の名前と、元に戻せないことを書いてから、理由を書いて取り下げる。 */
function confirmTakedown(row, p) {
  document.querySelector(".confirm-row")?.remove();
  const tr = el("tr", "confirm-row");
  const cell = el("td");
  cell.colSpan = 5;
  tr.append(cell);
  row.after(tr);
  const box = el("form", "confirm");
  const label = el("label", "field");
  const input = el("input");
  input.type = "text"; input.maxLength = 200; input.name = "reason";
  label.append(el("span", null, "理由（発案者に見えます）"), input);
  const err = el("p", "err");
  const acts = el("div", "acts");
  const ok = el("button", "btn neg sm", "取り下げる");
  ok.type = "submit";
  const no = el("button", "btn s sm", "やめる");
  no.type = "button";
  no.addEventListener("click", () => tr.remove());
  acts.append(ok, no);
  box.append(el("p", null, `${p.name}を一覧から消します。発案者はこの発明を公開し直せません。`), label, err, acts);
  box.addEventListener("submit", async (e) => {
    e.preventDefault();
    ok.disabled = true;
    try { await api(`/admin/library/${p.id}/takedown`, { method: "POST", body: { reason: input.value.trim() } }); await load(); }
    catch (x) { err.textContent = x.message; ok.disabled = false; }
  });
  cell.append(box);
  input.focus();
}

function drawAccounts(accounts) {
  const tb = $("accounts");
  tb.replaceChildren();
  for (const a of accounts) {
    const tr = el("tr");
    const name = el("td");
    name.append(el("b", null, a.name), el("span", "sub", [a.affiliation, roleLabel(a.role), a.sample ? "見本" : ""].filter(Boolean).join("・")));
    const act = el("td");
    const b = el("button", a.verified ? "btn s sm" : "btn p sm", a.verified ? "印を外す" : "印を付ける");
    b.type = "button";
    b.setAttribute("aria-label", `${a.name}：${b.textContent}`);
    b.addEventListener("click", async () => {
      b.disabled = true;
      try { await api(`/admin/accounts/${a.id}/verify`, { method: "POST", body: { verified: !a.verified } }); await load(); }
      catch { b.disabled = false; }
    });
    if (a.verified) act.append(el("span", "mark-ok", "確認済み"), " ");
    act.append(b);
    tr.append(name, act);
    tb.append(tr);
  }
}

try { await load(); } catch (x) {
  $("lead").textContent = x.message;
  for (const t of document.querySelectorAll(".pane")) t.remove();
}
