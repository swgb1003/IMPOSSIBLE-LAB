/* IMPOSSIBLE LAB 卒業制作発表 — three.js の演出
   <div class="scene" data-scene="名前"> の中に、表示中のページの分だけ描く。
   hero : 表紙と結びの雨（マウスで奥行きが動く）
   rain : 雨よけの模式図（data-radius = かさの半径、data-side = 側面カバーの高さ）。2枚は同じ乱数の雨を使う */
window.Scenes = (function () {
  "use strict";
  var reg = [];
  var types = {};
  var mouse = { x: 0, y: 0 };
  addEventListener("pointermove", function (e) {
    mouse.x = (e.clientX / innerWidth) * 2 - 1;
    mouse.y = (e.clientY / innerHeight) * 2 - 1;
  });

  function rng(seed) {
    return function () {
      seed |= 0; seed = (seed + 0x6d2b79f5) | 0;
      var t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }

  function base(el, build) {
    var w = el.clientWidth || 1920, h = el.clientHeight || 1080;
    var renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
    renderer.setPixelRatio(Math.min(2, Math.max(1, devicePixelRatio || 1)));
    renderer.setSize(w, h, false);
    renderer.domElement.setAttribute("data-runtime", "");
    el.appendChild(renderer.domElement);
    var scene = new THREE.Scene();
    var api = build(scene, w / h, el);
    var raf = 0, last = 0, t = 0;
    function frame(now) {
      raf = requestAnimationFrame(frame);
      var dt = Math.min(0.05, (now - last) / 1000 || 0.016); last = now; t += dt;
      api.update(dt, t);
      renderer.render(scene, api.camera);
    }
    return {
      start: function () { if (!raf) { last = performance.now(); raf = requestAnimationFrame(frame); } },
      stop: function () { cancelAnimationFrame(raf); raf = 0; },
      once: function () { api.update(0.016, t); renderer.render(scene, api.camera); },
      dispose: function () { cancelAnimationFrame(raf); raf = 0; renderer.dispose(); renderer.domElement.remove(); }
    };
  }

  /* ---- 表紙・結び：奥行きのある雨 ---- */
  types.hero = function (el) {
    return base(el, function (scene, aspect) {
      var camera = new THREE.PerspectiveCamera(50, aspect, 0.1, 100);
      camera.position.set(0, 0, 12);
      var N = 520, r = rng(7);
      var pos = new Float32Array(N * 6);
      var d = [];
      for (var i = 0; i < N; i++) d.push({ x: (r() - 0.5) * 44, y: (r() - 0.5) * 26, z: -14 + r() * 20, v: 7 + r() * 7, l: 0.35 + r() * 0.7 });
      var geo = new THREE.BufferGeometry();
      geo.setAttribute("position", new THREE.BufferAttribute(pos, 3));
      var mat = new THREE.LineBasicMaterial({ color: 0xbfe6f5, transparent: true, opacity: 0.3 });
      scene.add(new THREE.LineSegments(geo, mat));
      var slide = el.closest(".slide");
      return {
        camera: camera,
        update: function (dt) {
          for (var i = 0; i < N; i++) {
            var p = d[i];
            p.y -= p.v * dt; p.x -= p.v * 0.16 * dt;
            if (p.y < -13) { p.y = 13; p.x = (r() - 0.5) * 44; }
            var k = i * 6;
            pos[k] = p.x; pos[k + 1] = p.y; pos[k + 2] = p.z;
            pos[k + 3] = p.x + p.l * 0.16; pos[k + 4] = p.y + p.l; pos[k + 5] = p.z;
          }
          geo.attributes.position.needsUpdate = true;
          camera.position.x += (mouse.x * 0.9 - camera.position.x) * 0.04;
          camera.position.y += (-mouse.y * 0.5 - camera.position.y) * 0.04;
          camera.lookAt(0, 0, 0);
          if (slide) {
            slide.style.setProperty("--px", (camera.position.x / 0.9).toFixed(3));
            slide.style.setProperty("--py", (-camera.position.y / 0.5).toFixed(3));
          }
        }
      };
    });
  };

  /* ---- 改良前後の雨よけ（模式図） ---- */
  types.rain = function (el) {
    var R = parseFloat(el.dataset.radius) || 0.58;
    var SIDE = parseFloat(el.dataset.side) || 0;
    return base(el, function (scene, aspect) {
      var camera = new THREE.PerspectiveCamera(30, aspect, 0.1, 100);
      scene.add(new THREE.HemisphereLight(0xffffff, 0x9fb0bf, 1.0));
      var sun = new THREE.DirectionalLight(0xffffff, 0.55); sun.position.set(-3, 5, 4); scene.add(sun);

      var ground = new THREE.Mesh(new THREE.CircleGeometry(2.3, 64), new THREE.MeshBasicMaterial({ color: 0xc3ccd4 }));
      ground.rotation.x = -Math.PI / 2; scene.add(ground);

      var skin = new THREE.MeshStandardMaterial({ color: 0xf7f9fa, roughness: 0.9 });
      var body = new THREE.Mesh(new THREE.CapsuleGeometry(0.22, 1.0, 8, 24), skin); body.position.y = 0.72; scene.add(body);
      var head = new THREE.Mesh(new THREE.SphereGeometry(0.16, 24, 16), skin); head.position.y = 1.64; scene.add(head);

      var H = 2.05;
      var glass = new THREE.MeshStandardMaterial({ color: 0x1f9be0, transparent: true, opacity: 0.6, roughness: 0.3, side: THREE.DoubleSide });
      var canopy = new THREE.Mesh(new THREE.CylinderGeometry(R, R, 0.05, 64), glass); canopy.position.y = H; scene.add(canopy);
      var hub = new THREE.Mesh(new THREE.CylinderGeometry(0.12, 0.12, 0.12, 24), new THREE.MeshStandardMaterial({ color: 0xe9eef2 })); hub.position.y = H - 0.08; scene.add(hub);
      if (SIDE > 0) {
        var wallMat = glass.clone(); wallMat.opacity = 0.38;
        var wall = new THREE.Mesh(new THREE.CylinderGeometry(R, R, SIDE, 64, 1, true), wallMat); wall.position.y = H - SIDE / 2; scene.add(wall);
      }

      /* 雨：2枚とも同じ乱数。1粒ごとに出る場所と時刻が決まっていて、防がれた粒だけが消える。
         粒の行き先（かさ・側面カバー・体・地面）は、最初に1回だけ計算しておく。
         風の傾きとかさの半径は、防護率が実測（約41% → 約66%）に近くなる値にしてある */
      var N = 460, r = rng(2026);
      var VX = -4.0, VY = -7.0, TOP = 3.7, LIFE = TOP / -VY;
      var drops = [];
      for (var i = 0; i < N; i++) {
        var d = { x: -2.5 + r() * 7.7, z: (r() - 0.5) * 1.5, off: r() * LIFE, end: LIFE, hit: false, cycle: -1 };
        var px = d.x, py = TOP;
        for (var lt = 0.004; lt < LIFE; lt += 0.004) {
          var x = d.x + VX * lt, y = TOP + VY * lt;
          var rr = Math.sqrt(x * x + d.z * d.z), pr = Math.sqrt(px * px + d.z * d.z);
          if (py >= H && y < H && rr < R) { d.end = lt; break; }                                 // かさで防いだ
          if (SIDE > 0 && y < H && y > H - SIDE && pr >= R && rr < R) { d.end = lt; break; }      // 側面カバーで防いだ
          if (y < 1.8 && y > 0.02 && rr < (y > 1.46 ? 0.17 : 0.23)) { d.end = lt; d.hit = true; break; } // 体に当たった
          px = x; py = y;
        }
        drops.push(d);
      }
      var pos = new Float32Array(N * 6);
      var geo = new THREE.BufferGeometry();
      geo.setAttribute("position", new THREE.BufferAttribute(pos, 3));
      scene.add(new THREE.LineSegments(geo, new THREE.LineBasicMaterial({ color: 0x3f79b5, transparent: true, opacity: 0.7 })));

      var hits = [];
      var hitGeo = new THREE.SphereGeometry(0.055, 10, 8);
      for (var j = 0; j < 40; j++) {
        var m = new THREE.Mesh(hitGeo, new THREE.MeshBasicMaterial({ color: 0xf08a3c }));
        m.visible = false; m.userData.life = 0; scene.add(m); hits.push(m);
      }
      var hitIdx = 0;
      function splash(x, y, z) { var m = hits[hitIdx++ % hits.length]; m.position.set(x, y, z); m.userData.life = 0.5; m.visible = true; }

      var clock = 0;
      return {
        camera: camera,
        update: function (dt, t) {
          clock += dt;
          var a = Math.sin(t * 0.3) * 0.14;
          camera.position.set(Math.sin(a) * 7.4 + 0.2, 1.75, Math.cos(a) * 7.4);
          camera.lookAt(0.2, 1.32, 0);
          for (var i = 0; i < N; i++) {
            var d = drops[i];
            var tt = clock + d.off, cyc = Math.floor(tt / LIFE), lt = tt - cyc * LIFE;
            var k = i * 6;
            if (cyc !== d.cycle) { d.cycle = cyc; d.done = lt >= d.end; }
            if (lt >= d.end) {
              if (!d.done) { d.done = true; if (d.hit) splash(d.x + VX * d.end, TOP + VY * d.end, d.z + 0.25); }
              pos[k] = pos[k + 1] = pos[k + 2] = pos[k + 3] = pos[k + 4] = pos[k + 5] = 0; continue;
            }
            var x = d.x + VX * lt, y = TOP + VY * lt;
            pos[k] = x; pos[k + 1] = y; pos[k + 2] = d.z;
            pos[k + 3] = x - VX * 0.035; pos[k + 4] = y - VY * 0.035; pos[k + 5] = d.z;
          }
          geo.attributes.position.needsUpdate = true;
          for (var j = 0; j < hits.length; j++) {
            var m = hits[j];
            if (!m.visible) continue;
            m.userData.life -= dt;
            if (m.userData.life <= 0) { m.visible = false; continue; }
            m.scale.setScalar(0.4 + m.userData.life * 2.0);
          }
        }
      };
    });
  };

  function sync() {
    reg = reg.filter(function (r) {
      if (r.el.isConnected) return true;
      r.inst.dispose(); return false;
    });
    if (!window.THREE) return;
    document.querySelectorAll(".scene").forEach(function (el) {
      if (el.__scene || !types[el.dataset.scene]) return;
      try { var inst = types[el.dataset.scene](el); el.__scene = inst; reg.push({ el: el, inst: inst }); }
      catch (e) { console.warn("scene", e); }
    });
  }
  function setActive(slide) {
    sync();
    reg.forEach(function (r) {
      if (r.el.closest(".slide") === slide) r.inst.start(); else r.inst.stop();
    });
  }
  return { sync: sync, setActive: setActive };
})();
