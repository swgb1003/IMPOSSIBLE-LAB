// 知らせ：採用・検討中・やりとりの返事・所属の確認・取り下げ。開くと、すべて読んだことにする（この画面では新しいものに印を残す）。
import { $, el, phraseBreaks } from "./text.js";
import { api, mountAccount, stamp } from "./session.js";

/** 知らせの文と行き先。 */
function say(n) {
  switch (n.kind) {
    case "adopted": return [`${n.fromName}が${n.pubName}を採用しました。採用の理由への返事を書けます。`, `/messages/${n.threadId}`];
    case "considering": return [`開発者が1人、${n.pubName}を検討中にしました。`, `/inventions/${n.pubId}`];
    case "message": return [`${n.fromName}から、${n.pubName}のやりとりに返事が届きました。`, `/messages/${n.threadId}`];
    case "verified": return ["運営があなたの所属を確かめました。名前の横に確認済みの印が付きます。", "/me"];
    case "takedown": return [`運営が${n.pubName}の公開を取り下げました。${n.note ? `理由：${n.note}` : ""}`, "/me"];
    default: return ["知らせが届きました。", "/me"];
  }
}

const s = await mountAccount();
if (!s.live) {
  $("lead").textContent = "ライブラリーにつながりません。PC の中継のプログラムが動いているか確かめてください。";
} else {
  const { items, unread } = await api("/api/notifications");
  $("lead").textContent = unread ? `新しい知らせが ${unread} 件あります。` : "新しい知らせはありません。";
  $("count").textContent = `${items.length} 件`;
  const ul = $("list");
  if (items.length === 0) ul.append(el("li", "note", "知らせはまだありません。採用されたときや返事が届いたときに、ここに出ます。"));
  for (const n of items) {
    const [text, href] = say(n);
    const a = el("a", "th-item" + (n.read ? "" : " fresh"));
    a.href = href;
    a.append(el("b", null, text), el("span", "when", stamp(n.at)));
    if (!n.read) a.append(el("span", "state mark-wait", "新しい"));
    const li = el("li");
    li.append(a);
    ul.append(li);
  }
  if (unread) { await api("/api/notifications/read", { method: "POST", body: {} }).catch(() => {}); await mountAccount(); }
}
phraseBreaks(document.querySelector("main"));
