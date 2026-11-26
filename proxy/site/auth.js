// ログインまわりの画面（ログイン・新規登録・Quest の番号で入る・パスワードの再設定・新しいパスワード・メールアドレスの確認）。
// 確認と再設定のリンクの札は # の後ろにあり、サーバーのアドレスの記録には残らない。読んだら札をアドレスから消す。
import { $, phraseBreaks } from "./text.js";
import { api, mountAccount, safeNext } from "./session.js";

const page = document.body.dataset.page;
const q = new URLSearchParams(location.search);
const next = safeNext(q.get("next"), "/me");
const form = $("form"), err = $("err");
const btn = form?.querySelector('button[type="submit"]');

/** 送る間はボタンを止め、失敗したら理由を出す（入れ直す欄に移る）。 */
function onSubmit(run, focusOnError) {
  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    err.textContent = "";
    btn.disabled = true;
    try { await run(new FormData(form)); } catch (x) {
      err.textContent = x.message;
      btn.disabled = false;
      (focusOnError?.(x) ?? form.querySelector("input"))?.focus();
    }
  });
}

/** アドレスの # から札を取り出し、アドレスからは消す。 */
function takeToken() {
  const t = new URLSearchParams(location.hash.slice(1)).get("token") ?? "";
  if (location.hash) history.replaceState(null, "", location.pathname + location.search);
  return /^[A-Za-z0-9_-]{20,100}$/.test(t) ? t : "";
}

const go = (user) => { location.href = user?.kind === "operator" && next === "/me" ? "/admin" : next; };

if (page === "login") {
  if (q.has("next")) $("why").hidden = false;
  onSubmit(async (d) => {
    const email = String(d.get("email")).trim(), password = String(d.get("password"));
    if (!email || !password) throw new Error("メールアドレスとパスワードを入れてください。");
    const r = await api("/api/login", { method: "POST", body: { email, password } });
    go(r.user);
  }, () => form.elements.password);
  form.elements.email.focus();
}

if (page === "signup") {
  onSubmit(async (d) => {
    const body = {
      name: String(d.get("name")).trim(), affiliation: String(d.get("affiliation")).trim(),
      field: String(d.get("field")).trim(), intro: String(d.get("intro")).trim(),
      email: String(d.get("email")).trim(), password: String(d.get("password")),
      agreed: d.has("agreed"),
    };
    if (!body.name) throw Object.assign(new Error("名前を書いてください。"), { field: "name" });
    if (!body.email) throw Object.assign(new Error("メールアドレスを書いてください。"), { field: "email" });
    if ([...body.password].length < 8) throw Object.assign(new Error("パスワードは 8 文字以上にしてください。"), { field: "password" });
    if (!body.agreed) throw Object.assign(new Error("利用規約と個人情報の扱いに同意してください。"), { field: "agreed" });
    await api("/api/signup", { method: "POST", body });
    location.href = q.has("next") ? next : "/me?welcome";
  }, (x) => form.elements[x.field ?? (x.code === "email_taken" || x.code === "bad_email" ? "email" : x.code === "weak_password" ? "password" : "name")]);
}

if (page === "pair") {
  const input = form.elements.code;
  input.addEventListener("input", () => { input.value = input.value.replace(/\D/g, "").slice(0, 6); });
  onSubmit(async (d) => {
    const code = String(d.get("code")).replace(/\D/g, "");
    if (code.length !== 6) throw new Error("6 桁の番号を入れてください。");
    await api("/api/pair", { method: "POST", body: { code } });
    location.href = q.has("next") ? next : "/me";
  });
  input.focus();
}

if (page === "forgot") {
  onSubmit(async (d) => {
    const email = String(d.get("email")).trim();
    if (!email) throw new Error("メールアドレスを入れてください。");
    await api("/api/password/forgot", { method: "POST", body: { email } });
    form.hidden = true;
    $("done").hidden = false;
  });
}

if (page === "reset") {
  const token = takeToken();
  if (!token) {
    form.hidden = true;
    err.textContent = "";
    $("done").hidden = false;
    $("done").replaceChildren();
    const p = document.createElement("p");
    p.textContent = "このリンクは使えません。もう一度、再設定のメールを送ってください。";
    const a = document.createElement("a");
    a.className = "btn p sm"; a.href = "/forgot"; a.textContent = "再設定のメールを送る";
    const acts = document.createElement("div");
    acts.className = "acts";
    acts.append(a);
    $("done").append(p, acts);
  } else {
    onSubmit(async (d) => {
      const password = String(d.get("password"));
      if ([...password].length < 8) throw new Error("パスワードは 8 文字以上にしてください。");
      await api("/api/password/reset", { method: "POST", body: { token, password } });
      form.hidden = true;
      $("done").hidden = false;
    });
  }
}

if (page === "verify") {
  const token = takeToken();
  const state = $("state");
  if (!token) state.textContent = "このリンクは使えません。自分のページから、確認のメールを送り直してください。";
  else {
    try {
      await api("/api/verify-email", { method: "POST", body: { token } });
      state.textContent = "メールアドレスを確かめました。";
    } catch (x) { state.textContent = x.message; }
  }
  $("acts").hidden = false;
}

await mountAccount();
phraseBreaks(document.querySelector("main"));
