/* IMPOSSIBLE LAB 卒業制作発表 — ページ送りと編集モード
   発表：→／Space／PageDown 次へ、←／PageUp 前へ、Home／End、F 全画面、N 発表者メモ
   編集：E で切り替え。クリックで選ぶ → ドラッグで動かす／右下の四角で大きさ／ダブルクリックで文字を書き換え
        Delete で消す、Ctrl+D で複製、Ctrl+Z で元に戻す、Ctrl+S で保存（index.html を上書きする） */
(function () {
  "use strict";
  var stage = document.getElementById("stage");
  var slides = Array.prototype.slice.call(stage.querySelectorAll(".slide"));
  var cur = -1, step = 0, scale = 1, editing = false, dirty = false;

  /* ---------- スクリプトが足す部品（保存のときは取り除く） ---------- */
  function make(tag, attrs, html) {
    var n = document.createElement(tag);
    for (var k in attrs) n.setAttribute(k, attrs[k]);
    n.setAttribute("data-runtime", "");
    if (html) n.innerHTML = html;
    return n;
  }
  var chrome = make("div", { class: "chrome" }, '<span class="wm">IMPOSSIBLE LAB</span><span class="pg"></span><span class="bar"></span>');
  stage.appendChild(chrome);
  var notes = make("div", { id: "notes" });
  var help = make("div", { id: "help" }, "→ 次へ　← 前へ　F 全画面　N メモ　E 編集");
  var grip = make("div", { id: "grip" });
  var bar = make("div", { id: "bar" },
    '<button data-a="prev">◀ 前</button><button data-a="next">次 ▶</button><button data-a="step">画面の切り替え</button><span>｜</span>' +
    '<button data-a="text">文字を足す</button><button data-a="img">画像を差し替え</button><button data-a="smaller">文字 小</button><button data-a="bigger">文字 大</button>' +
    '<button data-a="dup">複製</button><button data-a="del">削除</button><button data-a="reset">位置を戻す</button><button data-a="undo">元に戻す</button>' +
    '<i class="sp"></i><span id="hint">クリックで選ぶ・ドラッグで動かす・ダブルクリックで文字を書く</span>' +
    '<button data-a="save" class="go">保存</button><button data-a="exit">編集を終える</button>');
  var file = make("input", { type: "file", accept: "image/*", style: "display:none" });
  document.body.append(notes, help, grip, bar, file);

  /* ---------- 大きさ合わせ ---------- */
  function fit() {
    var top = editing ? bar.offsetHeight : 0;
    scale = Math.min(innerWidth / 1920, (innerHeight - top) / 1080);
    stage.style.transform = "translate(-50%, calc(-50% + " + top / 2 + "px)) scale(" + scale + ")";
    placeGrip();
  }
  addEventListener("resize", fit);

  /* ---------- 数字のカウントアップ ---------- */
  var counters = [];
  function stopCounters() { counters.forEach(function (c) { c(); }); counters = []; }
  function countUp(el, delay) {
    var orig = el.textContent, m = orig.match(/\d+(\.\d+)?/);
    if (!m) return;
    var target = parseFloat(m[0]), dec = (m[1] || "").length ? m[1].length - 1 : 0;
    var t0 = performance.now() + delay, raf = 0, done = false;
    function put(v) { el.textContent = orig.replace(m[0], v.toFixed(dec)); }
    put(0);
    function tick(now) {
      var p = Math.min(1, Math.max(0, (now - t0) / 1300));
      put(target * (1 - Math.pow(1 - p, 3)));
      if (p < 1) raf = requestAnimationFrame(tick); else finish();
    }
    function finish() { if (done) return; done = true; cancelAnimationFrame(raf); el.textContent = orig; }
    raf = requestAnimationFrame(tick);
    counters.push(finish);
  }

  /* ---------- ページ送り ---------- */
  function maxStep(s) { return parseInt(s.dataset.steps || "0", 10); }
  function show(i, st) {
    i = Math.max(0, Math.min(slides.length - 1, i));
    var s = slides[i];
    step = Math.max(0, Math.min(maxStep(s), st || 0));
    if (i !== cur) {
      stopCounters();
      slides.forEach(function (x, k) { x.classList.toggle("active", k === i); });
      cur = i;
      if (!editing && !/[?&]still/.test(location.search)) {
        s.querySelectorAll("[data-count]").forEach(function (el) {
          var host = el.closest("[data-in]");
          var d = host ? parseFloat(getComputedStyle(host).getPropertyValue("--i") || 0) * 90 + 300 : 300;
          countUp(el, d);
        });
      }
      select(null);
      if (window.Scenes) Scenes.setActive(s);
      try { history.replaceState(null, "", "#" + (i + 1)); } catch (e) { /* file:// でも動く */ }
    }
    if (s.hasAttribute("data-steps")) s.dataset.step = step;
    chrome.classList.toggle("on-light", s.classList.contains("light"));
    chrome.classList.toggle("bare", s.hasAttribute("data-bare"));
    chrome.querySelector(".pg").textContent = (i + 1) + " / " + slides.length;
    chrome.querySelector(".bar").style.width = ((i + 1) / slides.length * 100) + "%";
    var n = s.querySelector(".notes");
    notes.innerHTML = "<b>" + (i + 1) + " / " + slides.length + "</b>" + (n ? n.innerHTML : "");
  }
  function next() { if (step < maxStep(slides[cur])) show(cur, step + 1); else if (cur < slides.length - 1) show(cur + 1, 0); }
  function prev() { if (step > 0) show(cur, step - 1); else if (cur > 0) show(cur - 1, maxStep(slides[cur - 1])); }

  addEventListener("keydown", function (e) {
    var typing = document.activeElement && document.activeElement.isContentEditable;
    if (editing) return editKey(e, typing);
    if (e.ctrlKey || e.metaKey || e.altKey) return;
    switch (e.key) {
      case "ArrowRight": case "ArrowDown": case "PageDown": case " ": case "Enter": e.preventDefault(); next(); break;
      case "ArrowLeft": case "ArrowUp": case "PageUp": case "Backspace": e.preventDefault(); prev(); break;
      case "Home": show(0, 0); break;
      case "End": show(slides.length - 1, 0); break;
      case "f": case "F": if (document.fullscreenElement) document.exitFullscreen(); else document.documentElement.requestFullscreen(); break;
      case "n": case "N": document.body.classList.toggle("show-notes"); break;
      case "e": case "E": setEditing(true); break;
    }
  });
  stage.addEventListener("click", function (e) {
    if (editing) return;
    if (e.clientX < innerWidth * 0.25) prev(); else next();
  });

  /* ---------- 編集モード ---------- */
  var sel = null, undo = [];
  function setEditing(on) {
    if (!on && sel && sel.isContentEditable) endText();
    editing = on;
    document.body.classList.toggle("editing", on);
    stopCounters(); select(null); fit();
    if (!on) show(cur, step);
  }
  function snapshot() {
    var s = slides[cur], c = s.cloneNode(true);
    clean(c);
    undo.push({ i: cur, html: c.innerHTML, cls: s.className });
    if (undo.length > 60) undo.shift();
    dirty = true;
  }
  function restore() {
    var u = undo.pop(); if (!u) return;
    select(null);
    slides[u.i].innerHTML = u.html;
    show(u.i, step);
    if (window.Scenes) Scenes.setActive(slides[u.i]);
  }
  function select(el) {
    if (sel && sel !== el) { if (sel.isContentEditable) endText(); sel.classList.remove("sel"); }
    sel = el;
    if (el) el.classList.add("sel");
    placeGrip();
  }
  function placeGrip() {
    if (!editing || !sel || sel.isContentEditable) { grip.style.display = "none"; return; }
    var r = sel.getBoundingClientRect();
    grip.style.display = "block";
    grip.style.left = (r.right - 6) + "px"; grip.style.top = (r.bottom - 6) + "px";
  }
  function getT(el) { var t = (el.style.translate || "0px 0px").split(" "); return [parseFloat(t[0]) || 0, parseFloat(t[1] || 0) || 0]; }
  function isTexty(el) { return !el.querySelector("img, canvas, svg, .scene, [data-el]") && el.tagName !== "IMG"; }
  function beginText(el) {
    snapshot();
    el.setAttribute("contenteditable", "true"); el.focus(); placeGrip();
  }
  function endText() {
    if (!sel) return;
    sel.removeAttribute("contenteditable"); sel.blur(); placeGrip();
  }

  var drag = null;
  stage.addEventListener("pointerdown", function (e) {
    if (!editing) return;
    var el = e.target.closest("[data-el]");
    if (el && el.isContentEditable) return;           // 文字を書いている最中
    if (!el || !slides[cur].contains(el)) { select(null); return; }
    select(el);
    var t = getT(el);
    drag = { mode: "move", x: e.clientX, y: e.clientY, tx: t[0], ty: t[1], moved: false };
    e.preventDefault();
  });
  grip.addEventListener("pointerdown", function (e) {
    if (!sel) return;
    var r = sel.getBoundingClientRect();
    drag = { mode: "size", x: e.clientX, y: e.clientY, w: r.width / scale, h: r.height / scale, moved: false };
    e.preventDefault(); e.stopPropagation();
  });
  addEventListener("pointermove", function (e) {
    if (!drag || !sel) return;
    var dx = (e.clientX - drag.x) / scale, dy = (e.clientY - drag.y) / scale;
    if (!drag.moved) { if (Math.abs(dx) + Math.abs(dy) < 4) return; drag.moved = true; snapshot(); }
    if (drag.mode === "move") {
      sel.style.translate = Math.round(drag.tx + dx) + "px " + Math.round(drag.ty + dy) + "px";
    } else {
      sel.style.width = Math.max(40, Math.round(drag.w + dx)) + "px";
      if (!isTexty(sel) && sel.tagName !== "IMG" && sel.tagName !== "FIGURE") sel.style.height = Math.max(40, Math.round(drag.h + dy)) + "px";
    }
    placeGrip();
  });
  addEventListener("pointerup", function () { drag = null; });
  stage.addEventListener("dblclick", function (e) {
    if (!editing) return;
    var el = e.target.closest("[data-el]");
    if (!el) return;
    select(el);
    if (isTexty(el)) beginText(el);
    else {
      var inner = e.target.closest("b, span, p, h3, li, figcaption, em, small");
      if (inner && el.contains(inner) && isTexty(inner)) { snapshot(); sel.classList.remove("sel"); sel = inner; inner.classList.add("sel"); inner.setAttribute("contenteditable", "true"); inner.focus(); placeGrip(); }
    }
  });
  stage.addEventListener("input", function () { dirty = true; });

  function act(a) {
    var s = slides[cur];
    if (a === "prev") return show(cur - 1, 0);
    if (a === "next") return show(cur + 1, 0);
    if (a === "step") return show(cur, step < maxStep(s) ? step + 1 : 0);
    if (a === "exit") return setEditing(false);
    if (a === "save") return save();
    if (a === "undo") return restore();
    if (a === "text") {
      snapshot();
      var p = document.createElement("p");
      p.className = "free"; p.setAttribute("data-el", ""); p.textContent = "ここに文字";
      s.insertBefore(p, s.querySelector(".notes")); select(p); return;
    }
    if (!sel) { flash("先に、動かしたいものをクリックして選んでください"); return; }
    if (a === "img") {
      var im = sel.tagName === "IMG" ? sel : sel.querySelector("img");
      if (!im) { flash("画像を選んでください"); return; }
      if (sel.classList.contains("shots")) im = sel.querySelectorAll("img")[step] || im;
      file.onchange = function () {
        var f = file.files[0]; if (!f) return;
        var rd = new FileReader();
        rd.onload = function () { snapshot(); im.src = rd.result; };
        rd.readAsDataURL(f); file.value = "";
      };
      file.click(); return;
    }
    if (a === "smaller" || a === "bigger") {
      snapshot();
      var fs = parseFloat(getComputedStyle(sel).fontSize);
      sel.style.fontSize = Math.max(12, Math.round(fs + (a === "bigger" ? 2 : -2))) + "px"; placeGrip(); return;
    }
    if (a === "dup") {
      snapshot();
      var c = sel.cloneNode(true); c.classList.remove("sel");
      c.querySelectorAll("[data-runtime]").forEach(function (n) { n.remove(); });
      var t = getT(sel); c.style.translate = (t[0] + 30) + "px " + (t[1] + 30) + "px";
      sel.after(c); select(c); if (window.Scenes) Scenes.setActive(s); return;
    }
    if (a === "del") { snapshot(); var d = sel; select(null); d.remove(); return; }
    if (a === "reset") { snapshot(); sel.style.translate = ""; sel.style.width = ""; sel.style.height = ""; sel.style.fontSize = ""; if (!sel.getAttribute("style")) sel.removeAttribute("style"); placeGrip(); return; }
  }
  bar.addEventListener("click", function (e) { var b = e.target.closest("button"); if (b) act(b.dataset.a); });
  bar.addEventListener("pointerdown", function (e) { if (!(document.activeElement && document.activeElement.isContentEditable)) e.preventDefault(); });

  function editKey(e, typing) {
    var mod = e.ctrlKey || e.metaKey;
    if (mod && (e.key === "s" || e.key === "S")) { e.preventDefault(); save(); return; }
    if (typing) { if (e.key === "Escape") { endText(); } return; }
    if (mod && (e.key === "z" || e.key === "Z")) { e.preventDefault(); restore(); return; }
    if (mod && (e.key === "d" || e.key === "D")) { e.preventDefault(); act("dup"); return; }
    if (mod) return;
    switch (e.key) {
      case "e": case "E": case "Escape": if (e.key === "Escape" && sel) select(null); else setEditing(false); break;
      case "PageDown": e.preventDefault(); show(cur + 1, 0); break;
      case "PageUp": e.preventDefault(); show(cur - 1, 0); break;
      case "Delete": case "Backspace": if (sel) { e.preventDefault(); act("del"); } break;
      case "ArrowLeft": case "ArrowRight": case "ArrowUp": case "ArrowDown":
        if (!sel) { if (e.key === "ArrowRight") show(cur + 1, 0); if (e.key === "ArrowLeft") show(cur - 1, 0); break; }
        e.preventDefault(); snapshot();
        var t = getT(sel), k = e.shiftKey ? 10 : 1;
        if (e.key === "ArrowLeft") t[0] -= k; if (e.key === "ArrowRight") t[0] += k;
        if (e.key === "ArrowUp") t[1] -= k; if (e.key === "ArrowDown") t[1] += k;
        sel.style.translate = t[0] + "px " + t[1] + "px"; placeGrip(); break;
    }
  }

  /* ---------- 保存 ---------- */
  function clean(root) {
    root.querySelectorAll("[data-runtime]").forEach(function (n) { n.remove(); });
    root.querySelectorAll("[contenteditable]").forEach(function (n) { n.removeAttribute("contenteditable"); });
    root.querySelectorAll(".sel").forEach(function (n) { n.classList.remove("sel"); });
    root.querySelectorAll("[class='']").forEach(function (n) { n.removeAttribute("class"); });
    return root;
  }
  function serialize() {
    var doc = clean(document.documentElement.cloneNode(true));
    doc.querySelectorAll(".slide").forEach(function (s) {
      s.classList.remove("active"); s.removeAttribute("data-step");
      s.style.removeProperty("--px"); s.style.removeProperty("--py");
      if (!s.getAttribute("style")) s.removeAttribute("style");
    });
    var b = doc.querySelector("body"); b.removeAttribute("class");
    doc.querySelector("#stage").removeAttribute("style");
    return "<!doctype html>\n" + doc.outerHTML + "\n";
  }
  var handle = null;
  async function save() {
    if (sel && sel.isContentEditable) endText();
    stopCounters();
    var html = serialize();
    try {
      if (window.showSaveFilePicker) {
        if (!handle) handle = await showSaveFilePicker({ suggestedName: "index.html", types: [{ description: "HTML", accept: { "text/html": [".html"] } }] });
        var w = await handle.createWritable(); await w.write(html); await w.close();
        flash("保存しました：" + handle.name);
      } else {
        var a = document.createElement("a");
        a.href = URL.createObjectURL(new Blob([html], { type: "text/html" })); a.download = "index.html"; a.click();
        flash("index.html をダウンロードしました。元のファイルと置き換えてください");
      }
      dirty = false;
    } catch (err) { if (err && err.name !== "AbortError") flash("保存できませんでした：" + err.message); }
  }
  function flash(msg) {
    var h = document.getElementById("hint"); if (h) h.textContent = msg;
    help.textContent = msg; help.classList.add("on");
    clearTimeout(flash.t); flash.t = setTimeout(function () { help.classList.remove("on"); }, 3500);
  }
  addEventListener("beforeunload", function (e) { if (dirty) { e.preventDefault(); e.returnValue = ""; } });

  /* ---------- 開始 ---------- */
  fit();
  var q = new URLSearchParams(location.search);
  var start = parseInt(q.get("p") || location.hash.slice(1) || "1", 10) - 1;
  show(isNaN(start) ? 0 : start, parseInt(q.get("s") || "0", 10));
  if (q.has("edit")) setEditing(true);
  if (q.has("still")) document.documentElement.classList.add("still");
  help.classList.add("on"); setTimeout(function () { help.classList.remove("on"); }, 5000);
  window.Deck = { show: show, next: next, prev: prev, serialize: serialize };
})();
