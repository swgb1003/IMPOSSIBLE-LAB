// 設定：パスワードの変更、ログアウト（この端末・すべての端末）、退会（開発者だけ）。
import { $, el, phraseBreaks } from "./text.js";
import { api, errLine, field, mountAccount } from "./session.js";

const s = await mountAccount();
if (!s.live || !s.user) {
  $("lead").textContent = "ライブラリーにつながりません。PC の中継のプログラムが動いているか確かめてください。";
  for (const k of ["password", "sessions"]) $(k).remove();
} else {
  const u = s.user;
  $("lead").textContent = u.email ? `${u.email} でログインしています。` : "Quest の番号で入っています。";
  drawPassword($("password"), u);
  drawSessions($("sessions"));
  if (u.kind === "account") drawLeave($("leave"));
}
phraseBreaks(document.querySelector("main"));

function drawPassword(pane, u) {
  pane.append(el("h2", null, "パスワードの変更"));
  if (!u.email) {
    pane.append(el("p", "note gap-t", "Quest の番号で入ったときは、パスワードはありません。次に入るときも、Quest で新しい番号を出してください。"));
    return;
  }
  const form = el("form", "form");
  form.noValidate = true;
  const err = errLine();
  const ok = el("button", "btn p sm", "パスワードを変える");
  ok.type = "submit";
  const done = el("span", "note", "");
  done.setAttribute("role", "status");
  const acts = el("div", "acts");
  acts.append(ok, done);
  form.append(
    field("今のパスワード", "current", { type: "password", max: 128, auto: "current-password", required: true }),
    field("新しいパスワード", "next", { type: "password", max: 128, hint: "8 文字以上", auto: "new-password", required: true }),
    el("p", "note", "変えると、この端末のほかは、すべてログアウトします。"),
    err, acts,
  );
  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    const d = new FormData(form);
    const current = String(d.get("current")), next = String(d.get("next"));
    if ([...next].length < 8) { err.textContent = "新しいパスワードは 8 文字以上にしてください。"; form.elements.next.focus(); return; }
    ok.disabled = true; err.textContent = ""; done.textContent = "";
    try {
      await api("/api/me/password", { method: "POST", body: { current, next } });
      form.reset();
      done.textContent = "パスワードを変えました。";
    } catch (x) { err.textContent = x.message; form.elements.current.focus(); }
    ok.disabled = false;
  });
  pane.append(form);
}

function drawSessions(pane) {
  pane.append(el("h2", null, "ログアウト"));
  const acts = el("div", "acts gap-t");
  const here = el("button", "btn s sm", "この端末でログアウト");
  here.type = "button";
  here.addEventListener("click", async () => { here.disabled = true; await api("/api/logout", { method: "POST", body: {} }).catch(() => {}); location.href = "/login"; });
  const all = el("button", "txt", "すべての端末でログアウト");
  all.type = "button";
  all.addEventListener("click", async () => { all.disabled = true; await api("/api/logout-all", { method: "POST", body: {} }).catch(() => {}); location.href = "/login"; });
  acts.append(here, all);
  pane.append(el("p", "note gap-t", "ほかの PC やブラウザで入ったままのときは、すべての端末でログアウトできます。"), acts);
}

function drawLeave(pane) {
  pane.hidden = false;
  pane.append(el("h2", null, "退会"));
  pane.append(el("p", "note gap-t", "登録とログインを消します。採用した発明のページには退会した人として残り、発案者とのやりとりは終わります。元に戻せません。"));
  const b = el("button", "btn neg sm", "退会する");
  b.type = "button";
  b.addEventListener("click", leaveDialog);
  const acts = el("div", "acts gap-t");
  acts.append(b);
  pane.append(acts);
}

function leaveDialog() {
  const dlg = el("dialog", "dlg");
  dlg.setAttribute("aria-labelledby", "dlg-h");
  const form = el("form", "form");
  form.noValidate = true;
  const h = el("h2", null, "退会する");
  h.id = "dlg-h";
  const err = errLine();
  const ok = el("button", "btn neg sm", "退会する");
  ok.type = "submit";
  const no = el("button", "btn s sm", "やめる");
  no.type = "button";
  no.addEventListener("click", () => dlg.close());
  const acts = el("div", "acts");
  acts.append(ok, no);
  form.append(h, el("p", "note", "確かめるため、パスワードを入れてください。退会すると元に戻せません。"),
    field("パスワード", "password", { type: "password", max: 128, auto: "current-password", required: true }), err, acts);
  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    ok.disabled = true; err.textContent = "";
    try {
      await api("/api/me/delete", { method: "POST", body: { password: String(new FormData(form).get("password")) } });
      location.href = "/";
    } catch (x) { err.textContent = x.message; ok.disabled = false; form.elements.password.focus(); }
  });
  dlg.append(form);
  dlg.addEventListener("close", () => dlg.remove());
  document.body.append(dlg);
  dlg.showModal();
  phraseBreaks(dlg);
  form.elements.password.focus();
}
