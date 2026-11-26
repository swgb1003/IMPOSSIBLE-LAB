// 運営（運営のアカウントだけ）：通報の確認、公開された発明の取り下げ（発案者は公開し直せない）、開発者の所属の確認の印（形だけ）。
import { $, el, date, phraseBreaks } from "./text.js";
import { api, mountAccount, stamp } from "./session.js";

const STATUS = { open: "公開中", considering: "検討中", adopted: "採用", withdrawn: "取り下げ" };

async function load() {
  const [pubs, accounts, reports] = await Promise.all([
    api("/api/admin/library").then((r) => r.items), api("/api/admin/accounts").then((r) => r.items), api("/api/admin/reports").then((r) => r.items),
  ]);
  const live = pubs.filter((p) => p.status !== "withdrawn").length;
  const unverified = accounts.filter((a) => !a.verified).length;
  const open = reports.filter((r) => r.status === "open").length;
  $("lead").textContent = `まだ見ていない通報 ${open} 件。公開中 ${live} 件、取り下げ ${pubs.length - live} 件。所属をまだ確かめていない人が ${unverified} 人います。`;
  drawReports(reports);
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
    else { const a = el("a", null, p.name); a.href = `/inventions/${p.id}`; const b = el("b"); b.append(a); name.append(b); }
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
    // 詳しい内容を見た開発者（新しく見た順に3人まで名前を出す）
    const v = el("td");
    v.append(el("span", "num", `${p.viewers.length} 人`));
    const recent = [...p.viewers].sort((a, b) => b.last.localeCompare(a.last));
    if (recent.length) v.append(el("span", "sub", recent.slice(0, 3).map((x) => x.name).join("・") + (recent.length > 3 ? ` ほか ${recent.length - 3} 人` : "")));
    st.dataset.l = "状態"; n.dataset.l = "採用"; v.dataset.l = "詳しく見た"; d.dataset.l = "公開"; // 電話では列の見出しの代わりに出す
    tr.append(name, st, n, v, d, act);
    tb.append(tr);
  }
}

/** 取り下げの確かめ：発明の名前と、元に戻せないことを書いてから、理由を書いて取り下げる。 */
function confirmTakedown(row, p) {
  document.querySelector(".confirm-row")?.remove();
  const tr = el("tr", "confirm-row");
  const cell = el("td");
  cell.colSpan = 6;
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
    try { await api(`/api/admin/library/${p.id}/takedown`, { method: "POST", body: { reason: input.value.trim() } }); await load(); }
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
    name.append(el("b", null, a.name), el("span", "sub", [a.affiliation, a.sample ? "見本" : ""].filter(Boolean).join("・")),
      el("span", "sub", `${a.email || "メールアドレスなし"}${a.email && !a.emailVerified ? "（未確認）" : ""}`),
      el("span", "sub", `詳しく見た発明 ${a.viewed} 件・採用 ${a.adoptions} 件`));
    const act = el("td");
    const b = el("button", a.verified ? "btn s sm" : "btn p sm", a.verified ? "印を外す" : "印を付ける");
    b.type = "button";
    b.setAttribute("aria-label", `${a.name}：${b.textContent}`);
    b.addEventListener("click", async () => {
      b.disabled = true;
      try { await api(`/api/admin/accounts/${a.id}/verify`, { method: "POST", body: { verified: !a.verified } }); await load(); }
      catch { b.disabled = false; }
    });
    if (a.verified) act.append(el("span", "mark-ok", "確認済み"), " ");
    act.append(b);
    tr.append(name, act);
    tb.append(tr);
  }
}

/** 通報：まだ見ていないものを先に。問題なしで閉じるか、その発明を取り下げる。 */
function drawReports(reports) {
  const tb = $("reports");
  tb.replaceChildren();
  $("rep-count").textContent = `${reports.length} 件`;
  if (reports.length === 0) {
    const tr = el("tr"), td = el("td", "note empty", "通報はまだありません。");
    td.colSpan = 4;
    tr.append(td);
    tb.append(tr);
    return;
  }
  const sorted = [...reports].sort((a, b) => (a.status === "open" ? 0 : 1) - (b.status === "open" ? 0 : 1));
  for (const r of sorted) {
    const tr = el("tr");
    const what = el("td");
    const b = el("b");
    if (r.pubStatus === "withdrawn") b.textContent = r.pubName;
    else { const a = el("a", null, r.pubName); a.href = `/inventions/${r.pubId}`; b.append(a); }
    what.append(b, el("span", "sub", r.reason), ...(r.detail ? [el("span", "sub", r.detail)] : []));
    const when = el("td", "num nw", stamp(r.at));
    const st = el("td");
    st.append(el("span", r.status === "open" ? "mark-wait" : "nw", r.status === "open" ? "まだ見ていない" : r.status === "takedown" ? "取り下げた" : "問題なし"));
    if (r.note) st.append(el("span", "sub", r.note));
    const act = el("td");
    if (r.status === "open") {
      const keep = el("button", "txt", "問題なし");
      keep.type = "button";
      keep.setAttribute("aria-label", `${r.pubName}の通報を問題なしで閉じる`);
      keep.addEventListener("click", async () => { keep.disabled = true; try { await api(`/api/admin/reports/${r.id}/resolve`, { method: "POST", body: { action: "dismiss", note: "" } }); await load(); } catch { keep.disabled = false; } });
      const down = el("button", "txt", "取り下げる");
      down.type = "button";
      down.setAttribute("aria-label", `${r.pubName}を取り下げる`);
      down.addEventListener("click", async () => { down.disabled = true; try { await api(`/api/admin/reports/${r.id}/resolve`, { method: "POST", body: { action: "takedown", note: r.reason } }); await load(); } catch { down.disabled = false; } });
      const acts = el("span", "row-acts");
      acts.append(keep, down);
      act.append(acts);
    }
    when.dataset.l = "通報"; st.dataset.l = "状態";
    tr.append(what, when, st, act);
    tb.append(tr);
  }
}

await mountAccount();
try { await load(); } catch (x) {
  $("lead").textContent = x.message;
  for (const t of document.querySelectorAll(".pane")) t.remove();
}
