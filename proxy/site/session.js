// ログインと、ライブラリの入口を呼ぶ道具。上の帯の右（ログイン・知らせ・自分のメニュー）もここで作る。
// ログインの記録は cookie（ページのプログラムからは読めない）。本人はサーバーが決める。
import { el } from "./text.js";

const VOTER = "ilab.voter";
export const enc = encodeURIComponent;

/** サイトのほしいを数えるための、このブラウザの印（個人を表さない乱数）。 */
export function voterId() {
  try {
    let v = localStorage.getItem(VOTER);
    if (!v) { v = Array.from(crypto.getRandomValues(new Uint8Array(12)), (b) => b.toString(16).padStart(2, "0")).join(""); localStorage.setItem(VOTER, v); } // taste-skill-ignore 乱数の桁そろえ
    return v;
  } catch { return null; }
}

/** ライブラリの入口を呼ぶ。失敗したら、入口が返した文（無ければ状態の番号）を持つ Error を投げる。 */
export async function api(path, { method = "GET", body } = {}) {
  const headers = {};
  if (body !== undefined) headers["content-type"] = "application/json";
  let res;
  try {
    res = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body), credentials: "same-origin" });
  } catch { throw Object.assign(new Error("ライブラリーにつながりません。PC の中継のプログラムが動いているか確かめてください。"), { status: 0 }); }
  const isJson = (res.headers.get("content-type") ?? "").includes("json");
  const data = isJson ? await res.json().catch(() => null) : null;
  if (!res.ok || !isJson) {
    const msg = data?.message ?? (isJson ? `うまくいきませんでした（${res.status}）` : "ライブラリーにつながりません。この画面は PC のサイトで開いてください。");
    throw Object.assign(new Error(msg), { status: isJson ? res.status : 0, code: data?.code });
  }
  return data;
}

/** 今のページへ戻るための next（このサイトの中のアドレスだけ）。 */
export function hereNext() { return enc(location.pathname + location.search); }
export function safeNext(raw, fallback = "/me") {
  return typeof raw === "string" && /^\/(?!\/)[A-Za-z0-9/_?=&%.-]*$/.test(raw) ? raw : fallback;
}

/** ログインしている人の短い説明（上の帯と画面の文に使う）。 */
export function userSub(u) {
  if (!u) return "";
  if (u.kind === "account") return [u.affiliation, "開発者"].filter(Boolean).join("・");
  if (u.kind === "creator") return "発案者";
  return "運営";
}

/**
 * ログインの状態を読み、上の帯の右を作る。返す値：{ live, user, unread }。
 * live はライブラリにつながったか（つながらないときは、帯の右は出さない）。
 */
export async function mountAccount() {
  const slot = document.querySelector('[data-slot="acct"]');
  let s;
  try { s = await api("/api/session"); } catch {
    slot?.remove();
    return { live: false, user: null, unread: 0 };
  }
  if (slot) drawAccount(slot, s);
  return { live: true, user: s.user, unread: s.unread };
}

function drawAccount(slot, { user, unread }) {
  slot.replaceChildren();
  if (!user) {
    const login = el("a", "acct-link", "ログイン");
    login.href = `/login?next=${hereNext()}`;
    const join = el("a", "btn s sm", "新規登録");
    join.href = "/signup";
    slot.append(login, join);
    return;
  }
  const here = location.pathname;
  const link = (href, text) => { const a = el("a", "acct-link", text); a.href = href; if (here === href || here.startsWith(href + "/")) a.setAttribute("aria-current", "page"); return a; };
  if (user.kind === "operator") slot.append(link("/admin", "運営"));
  else {
    slot.append(link("/messages", "やりとり"));
    const n = link("/notifications", "知らせ");
    if (unread > 0) { n.append(el("span", "badge num", String(unread))); n.setAttribute("aria-label", `知らせ（新しいもの ${unread} 件）`); }
    slot.append(n);
  }
  const menu = el("details", "menu");
  const sum = el("summary", null, user.name);
  sum.setAttribute("aria-label", `${user.name}のメニュー`);
  const list = el("div", "menu-list");
  if (user.kind !== "operator") list.append(link("/me", "自分のページ"));
  list.append(link("/settings", "設定"));
  const out = el("button", "acct-link", "ログアウト");
  out.type = "button";
  out.addEventListener("click", async () => {
    out.disabled = true;
    try { await api("/api/logout", { method: "POST", body: {} }); } catch { /* 画面は入口へ戻す */ }
    location.href = "/";
  });
  list.append(out);
  menu.append(sum, list);
  slot.append(menu);
  // メニューの外を押したら閉じる
  document.addEventListener("click", (e) => { if (menu.open && !menu.contains(e.target)) menu.open = false; });
  document.addEventListener("keydown", (e) => { if (e.key === "Escape" && menu.open) { menu.open = false; sum.focus(); } });
}

/** 日時を「11月25日 21:04」の形にする（同じ年なら年を省く）。 */
export function stamp(iso) {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  const hm = `${d.getHours()}:${String(d.getMinutes()).padStart(2, "0")}`; // taste-skill-ignore 時刻の分
  const md = `${d.getMonth() + 1}月${d.getDate()}日`;
  return d.getFullYear() === new Date().getFullYear() ? `${md} ${hm}` : `${d.getFullYear()}年${md} ${hm}`;
}

/** 入力の欄（ラベル・注記・入力）を作る。 */
export function field(label, name, { type = "text", max = 60, area = false, hint = "", auto = "", value = "", required = false } = {}) {
  const f = el("label", "field");
  const s = el("span", null, label);
  if (hint) s.append(el("small", null, hint));
  const input = el(area ? "textarea" : "input");
  input.name = name;
  if (max) input.maxLength = max;
  if (area) input.rows = 3; else input.type = type;
  if (auto) input.autocomplete = auto;
  if (required) input.required = true;
  input.value = value;
  f.append(s, input);
  return f;
}

/** 送る間はボタンを止め、失敗したら理由を出す。 */
export function submitting(form, btn, err, run) {
  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    err.textContent = "";
    btn.disabled = true;
    try { await run(new FormData(form)); } catch (x) { err.textContent = x.message; }
    btn.disabled = false;
  });
}

export function errLine() {
  const p = el("p", "err");
  p.setAttribute("role", "alert");
  return p;
}
