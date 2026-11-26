// 読む画面（利用規約・個人情報の扱い・見つからない・開けない）：上の帯の右を作り、文節で折り返し、板を順に入れる。
import { phraseBreaks } from "./text.js";
import { mountAccount } from "./session.js";

phraseBreaks(document.querySelector("main"));
mountAccount();
// 板は読み込んだときに順に入る（スクロールは待たない）
let i = 0;
const parts = document.querySelectorAll(".board > .t, .terms li, .short li");
for (const t of parts) t.style.setProperty("--d", `${Math.min(i++, 14) * 50}ms`);
requestAnimationFrame(() => requestAnimationFrame(() => { for (const t of parts) t.classList.add("in"); }));
