// サイトの名乗りとライブラリの入口を呼ぶ道具。
// ログインは形だけ：登録した研究者・開発者か、表示名で公開した発案者を選ぶと、その人として採用・やりとりができる（docs/PUBLISH_LIBRARY.md 3章）。
// 選んだ名乗りはこのブラウザにだけ残す。入口へは x-ilab-as で伝える（サーバーは 127.0.0.1 からだけ受け付ける）。
import { el } from "./text.js";

const KEY = "ilab.as";
const VOTER = "ilab.voter";
const ROLE = { researcher: "研究者", developer: "開発者" };
export const roleLabel = (r) => ROLE[r] ?? "";

export function getAs() { try { return localStorage.getItem(KEY) || ""; } catch { return ""; } }
export function setAs(v) { try { if (v) localStorage.setItem(KEY, v); else localStorage.removeItem(KEY); } catch { /* 残せないときは今の画面だけ */ } }

/** サイトのほしいを数えるための、このブラウザの印（個人を表さない乱数）。 */
export function voterId() {
  try {
    let v = localStorage.getItem(VOTER);
    if (!v) { v = Array.from(crypto.getRandomValues(new Uint8Array(12)), (b) => b.toString(16).padStart(2, "0")).join(""); localStorage.setItem(VOTER, v); }
    return v;
  } catch { return null; }
}

/** ライブラリの入口を呼ぶ。失敗したら、入口が返した文（無ければ状態の番号）を持つ Error を投げる。 */
export async function api(path, { method = "GET", body } = {}) {
  const headers = {};
  const as = getAs();
  if (as) headers["x-ilab-as"] = as;
  if (body !== undefined) headers["content-type"] = "application/json";
  let res;
  try {
    res = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  } catch { throw Object.assign(new Error("ライブラリにつながりません。PC の中継のプログラムが動いているか確かめてください"), { status: 0 }); }
  const isJson = (res.headers.get("content-type") ?? "").includes("json");
  const data = isJson ? await res.json().catch(() => null) : null;
  if (!res.ok || !isJson) {
    const msg = data?.message ?? (isJson ? `うまくいきませんでした（${res.status}）` : "ライブラリにつながりません。この画面は PC のサイトで開いてください");
    throw Object.assign(new Error(msg), { status: isJson ? res.status : 0, code: data?.code });
  }
  return data;
}

/**
 * 名乗りの一覧（研究者・開発者と、表示名で公開した発案者）を読み、上の帯の切り替えを作る。
 * 返す値：{ live, as, me, accounts, creators }。me は今の名乗り（無ければ null）。live はライブラリにつながったか。
 */
export async function mountWho() {
  const slot = document.querySelector('[data-slot="who"]');
  let accounts = [], creators = [];
  try {
    [accounts, creators] = await Promise.all([api("/accounts").then((r) => r.items), api("/creators").then((r) => r.items)]);
  } catch {
    if (slot) slot.remove();
    return { live: false, as: "", me: null, accounts, creators };
  }
  let as = getAs();
  const me = whoIs(as, accounts, creators);
  if (as && !me) { setAs(""); as = ""; } // 消えた登録の名乗りは外す
  if (slot) {
    const sel = el("select");
    sel.id = "who-select";
    sel.append(new Option("名乗らずに見る", ""));
    const ga = el("optgroup"); ga.label = "研究者・開発者";
    for (const a of accounts) ga.append(new Option(`${a.name}（${roleLabel(a.role)}）`, `account:${a.id}`));
    const gc = el("optgroup"); gc.label = "発案者";
    for (const c of creators) gc.append(new Option(c.name, `creator:${c.publicId}`));
    if (accounts.length) sel.append(ga);
    if (creators.length) sel.append(gc);
    sel.value = as;
    sel.addEventListener("change", () => { setAs(sel.value); location.reload(); });
    const label = el("label", "who-l", "名乗り");
    label.htmlFor = sel.id;
    slot.replaceChildren(label, sel);
    slot.hidden = false;
  }
  return { live: true, as, me, accounts, creators };
}

/** 名乗りの文字列（account:<ID> か creator:<公開 ID>）を、表示に使う形にする。 */
export function whoIs(as, accounts, creators) {
  const m = /^(account|creator):(.+)$/.exec(as ?? "");
  if (!m) return null;
  if (m[1] === "account") {
    const a = accounts.find((x) => x.id === m[2]);
    return a ? { kind: "account", id: a.id, name: a.name, sub: [a.affiliation, roleLabel(a.role)].filter(Boolean).join("・"), account: a } : null;
  }
  const c = creators.find((x) => x.publicId === m[2]);
  return c ? { kind: "creator", id: c.publicId, name: c.name, sub: "発案者" } : null;
}

/** 日時を「11月25日 21:04」の形にする（同じ年なら年を省く）。 */
export function stamp(iso) {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  const hm = `${d.getHours()}:${String(d.getMinutes()).padStart(2, "0")}`;
  const md = `${d.getMonth() + 1}月${d.getDate()}日`;
  return d.getFullYear() === new Date().getFullYear() ? `${md} ${hm}` : `${d.getFullYear()}年${md} ${hm}`;
}
