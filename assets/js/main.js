/* 双人斯诺克 3D 官网 · 交互脚本
   结构：入场动画 / 时间线注入 / 预告片（WebAudio 配乐）/ 147 彩蛋
   无任何外部依赖，file:// 直接可用 */
(function () {
  "use strict";

  var reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  /* ── 入场动画 ─────────────────────────── */
  var io = new IntersectionObserver(function (entries) {
    entries.forEach(function (e) {
      if (e.isIntersecting) { e.target.classList.add("in"); io.unobserve(e.target); }
    });
  }, { threshold: 0.12 });
  document.querySelectorAll(".reveal").forEach(function (el) { io.observe(el); });

  /* ── 时间线注入（仅 41 夜页有容器） ──── */
  var tl = document.getElementById("timeline");
  if (tl) {
    (window.TIMELINE || []).forEach(function (it) {
      var cls = "tl-item" + (it.night ? " night" : "") + (it.major ? " major" : "");
      var el = document.createElement("div");
      el.className = cls;
      el.innerHTML =
        '<i class="tl-dot" aria-hidden="true"></i>' +
        '<div class="tl-head">' +
          '<span class="tl-date">' + it.date + "</span>" +
          '<span class="tl-ver">' + it.ver + "</span>" +
          (it.night ? '<span class="tl-moon">🌙 凌晨</span>' : "") +
        "</div>" +
        '<div class="tl-title">' + it.title + "</div>" +
        '<div class="tl-desc">' + it.desc + "</div>";
      tl.appendChild(el);
    });
    // 时间线也走入场动画
    tl.querySelectorAll(".tl-item").forEach(function (el) {
      el.classList.add("reveal");
      io.observe(el);
    });
  }

  /* ── 胜利号角（真实游戏音效，base64） ── */
  var horn = null;
  function playHorn() {
    try {
      if (!horn) horn = new Audio("data:audio/wav;base64," + window.HORN_B64);
      horn.currentTime = 0;
      horn.volume = 0.9;
      horn.play().catch(function () {});
    } catch (e) {}
  }

  /* ── 预告片 ───────────────────────────── */
  var SCENES = [
    { img: "assets/img/menu.jpg",    k: "2026.09.07",            t: "一切，从第一行代码开始",        d: 6000 },
    { img: "assets/img/hero.jpg",    k: "2026.09.13 · 03:40",    t: "一个 Unity 工程，在凌晨悄悄诞生", d: 6000 },
    { img: "assets/img/aim.jpg",     k: "俯身",                  t: "眼高 0.11 米的瞄准",            d: 6000 },
    { img: "assets/img/wide.jpg",    k: "暗厅灯池",              t: "ACES 影调下的标准赛台",         d: 6000 },
    { img: "assets/img/pocket.jpg",  k: "真实袋口",              t: "皮革颚板 · 蜂窝网兜 · 晃袋",     d: 6000 },
    { img: "assets/img/spinpad.jpg", k: "加塞",                  t: "自建滑移摩擦模型，不靠引擎猜",    d: 5000 },
    { img: "assets/img/duo.jpg",     k: "双人同屏",              t: "一台手机，两个人",              d: 6000 },
    { img: "assets/img/win.jpg",     k: "胜利号角",              t: "整个游戏唯一的一条音效",        d: 7000 },
    { img: "",                       k: "SNOOKER LAB",           t: "双人斯诺克 3D · v1.0 · 41 夜 · MIT", d: 9000, finale: true }
  ];

  var stage = document.getElementById("stage"),
      poster = document.getElementById("stage-poster"),
      screen = document.getElementById("stage-screen"),
      frame = document.getElementById("frame"),
      frameImg = document.getElementById("frame-img"),
      capK = document.getElementById("frame-kicker"),
      capT = document.getElementById("frame-title"),
      progress = document.getElementById("stage-progress"),
      playing = false, ac = null, drone = null;

  function ensureAudio() {
    if (ac) return ac;
    try {
      ac = new (window.AudioContext || window.webkitAudioContext)();
    } catch (e) { ac = null; }
    return ac;
  }
  function startScore() {
    var c = ensureAudio(); if (!c) return;
    if (c.state === "suspended") c.resume();
    var master = c.createGain(); master.gain.value = 0; master.connect(c.destination);
    master.gain.linearRampToValueAtTime(0.16, c.currentTime + 3);
    // 低音持续层：D2 + A2 三角波，低通
    var lp = c.createBiquadFilter(); lp.type = "lowpass"; lp.frequency.value = 320; lp.connect(master);
    var o1 = c.createOscillator(); o1.type = "triangle"; o1.frequency.value = 73.42;
    var o2 = c.createOscillator(); o2.type = "triangle"; o2.frequency.value = 110.0; o2.detune.value = 6;
    var g = c.createGain(); g.gain.value = 0.5;
    o1.connect(g); o2.connect(g); g.connect(lp); o1.start(); o2.start();
    // 缓慢呼吸
    var lfo = c.createOscillator(); lfo.frequency.value = 0.08;
    var lfoG = c.createGain(); lfoG.gain.value = 0.06;
    lfo.connect(lfoG); lfoG.connect(master.gain); lfo.start();
    drone = { master: master, oscs: [o1, o2, lfo] };
  }
  function stopScore(fast) {
    if (!drone || !ac) return;
    var t = ac.currentTime;
    drone.master.gain.cancelScheduledValues(t);
    drone.master.gain.setValueAtTime(drone.master.gain.value, t);
    drone.master.gain.linearRampToValueAtTime(0, t + (fast ? 0.25 : 1.6));
    var d = drone; drone = null;
    setTimeout(function () { d.oscs.forEach(function (o) { try { o.stop(); } catch (e) {} }); },
               fast ? 320 : 1800);
  }
  function click() { // 切场音：台呢上短促的一声
    var c = ensureAudio(); if (!c) return;
    var t = c.currentTime;
    var buf = c.createBuffer(1, c.sampleRate * 0.05, c.sampleRate);
    var ch = buf.getChannelData(0);
    for (var i = 0; i < ch.length; i++) ch[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / ch.length, 3);
    var src = c.createBufferSource(); src.buffer = buf;
    var bp = c.createBiquadFilter(); bp.type = "bandpass"; bp.frequency.value = 1400; bp.Q.value = 1.1;
    var gn = c.createGain(); gn.gain.value = 0.11;
    src.connect(bp); bp.connect(gn); gn.connect(c.destination); src.start(t);
  }

  function showScene(i) {
    var s = SCENES[i];
    click();
    var cap = frame.querySelector(".frame-cap");
    if (s.finale) {
      frameImg.classList.remove("on", "kb");
      capK.textContent = s.k; capT.textContent = s.t;
      cap.classList.add("on");
      stopScore(false);
      setTimeout(playHorn, 700);
    } else {
      frameImg.src = s.img; frameImg.alt = s.t;
      frameImg.classList.remove("kb");
      void frameImg.offsetWidth;           // 重新触发 Ken Burns
      if (!reduced) frameImg.classList.add("kb");
      frameImg.classList.add("on");
      capK.textContent = s.k; capT.textContent = s.t;
      cap.classList.add("on");
    }
  }

  // 时间驱动：单一 tick 按已过时间定位当前场景。
  // 后台标签页的定时器节流只会让进度条滞后，切回前台立即对准，不会补帧连跳。
  var t0 = 0, cur = -1, tickId = null;
  function tick() {
    if (!playing) return;
    var el = performance.now() - t0, acc = 0, i, s;
    for (i = 0; i < SCENES.length; i++) { acc += SCENES[i].d; if (el < acc) break; }
    if (i >= SCENES.length) { endTrailer(true); return; }
    s = SCENES[i];
    if (i !== cur) { cur = i; showScene(i); }
    var start = acc - s.d;
    progress.style.width = Math.min(100, 100 * (el - start) / s.d) + "%";
    frame.querySelector(".frame-cap").classList.toggle("on", el - start < s.d - 650);
  }

  function startTrailer() {
    if (playing) return;
    playing = true;
    poster.hidden = true; screen.hidden = false;
    ensureAudio(); startScore();
    t0 = performance.now(); cur = -1;
    tickId = setInterval(tick, 200);
    tick();
  }
  function endTrailer(natural) {
    if (tickId) { clearInterval(tickId); tickId = null; }
    stopScore(!natural);
    playing = false;
    screen.hidden = true; poster.hidden = false;
    frameImg.removeAttribute("src");
    frameImg.classList.remove("on", "kb");
    frame.querySelector(".frame-cap").classList.remove("on");
    progress.style.width = "0";
  }

  var btnTrailer = document.getElementById("btn-trailer");
  if (btnTrailer) btnTrailer.addEventListener("click", startTrailer);
  if (poster) {
    document.getElementById("stage-play").addEventListener("click", startTrailer);
    document.getElementById("stage-skip").addEventListener("click", endTrailer);
  }

  /* ── 147 彩蛋 ─────────────────────────── */
  var buf = "", egg = document.getElementById("egg"),
      eggBalls = document.getElementById("egg-balls"),
      eggCount = document.getElementById("egg-count"),
      eggOpen = false, countTimer = null;

  var OTHER = ["#f2c14e", "#0f7a4d", "#8a4b24", "#1c4f9c", "#e46a8a", "#1a1a1a"];

  function spawnBalls() {
    var n = 34, html = "";
    for (var i = 0; i < n; i++) {
      var red = i < 15;
      var size = 14 + Math.random() * 30;
      var left = Math.random() * 100;
      var dur = 3.2 + Math.random() * 4.5;
      var delay = Math.random() * 2.6;
      var rot = 160 + Math.random() * 320;
      html += '<i class="egg-ball' + (red ? "" : " other") + '" style="left:' + left.toFixed(1) +
        "%;width:" + size.toFixed(0) + "px;height:" + size.toFixed(0) +
        "px;animation-duration:" + dur.toFixed(2) + "s;animation-delay:" + delay.toFixed(2) +
        "s;--r:" + rot.toFixed(0) + "deg;" +
        (red ? "" : "--bc:" + OTHER[i % OTHER.length] + ";") + '"></i>';
    }
    eggBalls.innerHTML = html;
  }

  function openEgg() {
    if (eggOpen) return;
    eggOpen = true;
    egg.hidden = false;
    spawnBalls();
    playHorn();
    var t0 = performance.now(), D = 2600;
    (function tick(now) {
      var p = Math.min(1, (now - t0) / D);
      var eased = 1 - Math.pow(1 - p, 3);
      eggCount.textContent = Math.round(eased * 147);
      if (p < 1 && eggOpen) countTimer = requestAnimationFrame(tick);
    })(t0);
  }
  function closeEgg() {
    if (!eggOpen) return;
    eggOpen = false;
    if (countTimer) cancelAnimationFrame(countTimer);
    egg.hidden = true;
    eggBalls.innerHTML = "";
  }
  document.getElementById("egg-close").addEventListener("click", closeEgg);
  document.addEventListener("keydown", function (e) {
    if (e.key === "Escape") { closeEgg(); if (playing) endTrailer(false); return; }
    if (eggOpen) return;
    if (/^[0-9]$/.test(e.key)) {
      buf = (buf + e.key).slice(-3);
      if (buf === "147") { buf = ""; openEgg(); }
    }
  });

  // 无头测试钩子：?egg 直接开彩蛋，?trailer 自动播预告片
  if (/[?&]egg/.test(location.search)) setTimeout(openEgg, 400);
  if (/[?&]trailer/.test(location.search)) setTimeout(startTrailer, 500);

  /* ── 导航栏滚动态 / 返回顶部 / 灯箱（全站通用） ── */
  var nav = document.getElementById("nav");
  var totop = document.getElementById("totop");
  function onScroll() {
    var y = window.scrollY || 0;
    if (nav) nav.classList.toggle("scrolled", y > 24);
    if (totop) totop.classList.toggle("show", y > 560);
  }
  window.addEventListener("scroll", onScroll, { passive: true });
  onScroll();
  if (totop) totop.addEventListener("click", function () {
    window.scrollTo({ top: 0, behavior: reduced ? "auto" : "smooth" });
  });

  var shots = document.querySelectorAll(".shot");
  if (shots.length) {
    var lb = document.createElement("div");
    lb.className = "lightbox";
    lb.hidden = true;
    lb.innerHTML = '<img alt=""><div class="lightbox-cap"></div>';
    document.body.appendChild(lb);
    var lbImg = lb.querySelector("img"), lbCap = lb.querySelector(".lightbox-cap");
    shots.forEach(function (s) {
      s.addEventListener("click", function () {
        var img = s.querySelector("img");
        lbImg.src = img.src.replace("/img/", "/img/"); // 原图即灯箱图
        lbImg.alt = img.alt;
        lbCap.textContent = s.getAttribute("data-cap") || img.alt || "";
        lb.hidden = false;
        document.body.style.overflow = "hidden";
      });
    });
    function closeLb() { lb.hidden = true; lbImg.removeAttribute("src"); document.body.style.overflow = ""; }
    lb.addEventListener("click", closeLb);
    document.addEventListener("keydown", function (e) {
      if (e.key === "Escape" && !lb.hidden) closeLb();
    });
  }
})();
