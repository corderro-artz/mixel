window.mixel = {
  setModelSrc: function (el, bytes) {
    if (!el) return;
    // Tracked fix #1: store previous URL on the element itself so revocation is
    // robust regardless of how Blazor marshals the ElementReference across calls.
    if (el.__mixelUrl) URL.revokeObjectURL(el.__mixelUrl);
    const blob = new Blob([bytes], { type: "model/gltf-binary" });
    const url = URL.createObjectURL(blob);
    el.__mixelUrl = url;
    el.setAttribute("src", url);
  },
  downloadFile: function (name, bytes) {
    const blob = new Blob([bytes], { type: "application/octet-stream" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url; a.download = name;
    document.body.appendChild(a); a.click(); a.remove();
    URL.revokeObjectURL(url);
  },
  saveTheme: function (id) { try { localStorage.setItem("mixel-theme", id); } catch (e) {} },
  loadTheme: function () { try { return localStorage.getItem("mixel-theme"); } catch (e) { return null; } },
  prefersDark: function () {
    return window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches;
  },
  setThemeAttribute: function (id) { document.documentElement.setAttribute("data-theme", id); },

  initDepthCanvas: function (id, pngBytes, w, h) {
    const canvas = document.getElementById(id);
    if (!canvas) return;
    canvas.width = w;
    canvas.height = h;
    // Scale to fill the painter container (skip canvas-wrap which has no intrinsic size yet)
    const parent = canvas.closest('.depth-painter') || canvas.parentElement;
    if (parent) {
      // 56px top padding + ~44px toolbar row + 8px gap + 8px bottom padding = ~116px overhead
      const availW = parent.clientWidth  - 16;
      const availH = parent.clientHeight - 116;
      const scale  = Math.max(1, Math.min(Math.floor(availW / w), Math.floor(availH / h)));
      canvas.style.width  = (w * scale) + 'px';
      canvas.style.height = (h * scale) + 'px';
    }
    const ctx = canvas.getContext("2d");
    const imageData = ctx.createImageData(w, h);
    for (let i = 0; i < pngBytes.length; i++) imageData.data[i] = pngBytes[i];
    ctx.putImageData(imageData, 0, 0);
    canvas.__mixelPng = imageData;
  },

  renderDepthOverlay: function (id, levels, w, h, maxDepth) {
    const canvas = document.getElementById(id);
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (canvas.__mixelPng) ctx.putImageData(canvas.__mixelPng, 0, 0);
    // Subtle white tint on all painted pixels — numbers from renderDepthLabels are the primary indicator
    const overlay = ctx.createImageData(w, h);
    for (let i = 0; i < levels.length; i++) {
      if (levels[i] === 0) continue;
      overlay.data[i * 4]     = 255;
      overlay.data[i * 4 + 1] = 255;
      overlay.data[i * 4 + 2] = 255;
      overlay.data[i * 4 + 3] = 55;
    }
    if (!window.mixel._overlayCanvas) window.mixel._overlayCanvas = document.createElement("canvas");
    const tmp = window.mixel._overlayCanvas;
    if (tmp.width !== w) tmp.width = w;
    if (tmp.height !== h) tmp.height = h;
    tmp.getContext("2d").putImageData(overlay, 0, 0);
    ctx.drawImage(tmp, 0, 0);
  },

  renderDepthLabels: function (artId, labelId, levels, w, h) {
    const art = document.getElementById(artId);
    const label = document.getElementById(labelId);
    if (!art || !label) return;

    // Size label canvas to match the art canvas CSS display dimensions
    const cssW = parseInt(art.style.width)  || art.clientWidth  || art.width;
    const cssH = parseInt(art.style.height) || art.clientHeight || art.height;
    if (label.width !== cssW || label.height !== cssH) {
      label.width  = cssW;
      label.height = cssH;
    }
    label.style.width  = cssW + 'px';
    label.style.height = cssH + 'px';

    const ctx = label.getContext("2d");
    ctx.clearRect(0, 0, cssW, cssH);

    const cellW = cssW / w;
    const cellH = cssH / h;
    const fontSize = Math.max(5, Math.floor(Math.min(cellW, cellH) * 0.6));

    ctx.font = `bold ${fontSize}px monospace`;
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";

    for (let y = 0; y < h; y++) {
      for (let x = 0; x < w; x++) {
        const lvl = levels[y * w + x];
        if (lvl === 0) continue;
        const cx = x * cellW + cellW / 2;
        const cy = y * cellH + cellH / 2;
        ctx.lineWidth = Math.max(1, fontSize * 0.25);
        ctx.strokeStyle = "rgba(0,0,0,0.85)";
        ctx.strokeText(String(lvl), cx, cy);
        ctx.fillStyle = "#ffffff";
        ctx.fillText(String(lvl), cx, cy);
      }
    }
  },

  _overlayCanvas: null,

  // ---- context menus ----
  _ctxMenu: null,
  _ctxDismiss: null,

  _showMenu: function (x, y, items, onAction) {
    window.mixel._hideMenu();
    const menu = document.createElement('div');
    menu.className = 'ctx-menu';
    menu.style.left = Math.min(x, window.innerWidth  - 200) + 'px';
    menu.style.top  = Math.min(y, window.innerHeight - items.length * 40 - 12) + 'px';
    items.forEach(({ label, action }) => {
      const btn = document.createElement('button');
      btn.className = 'ctx-item';
      btn.textContent = label;
      btn.addEventListener('pointerdown', (e) => {
        e.stopPropagation();
        window.mixel._hideMenu();
        onAction(action);
      });
      menu.appendChild(btn);
    });
    document.body.appendChild(menu);
    window.mixel._ctxMenu = menu;
    // Keep named refs so _hideMenu can remove them. The previous { once: true }
    // keydown listener leaked whenever the menu was dismissed by clicking (its
    // handler never fired, so it was never auto-removed) — one per menu open.
    const onDocDown = () => window.mixel._hideMenu();
    const onKeyDown = (e) => { if (e.key === 'Escape') window.mixel._hideMenu(); };
    window.mixel._ctxDismiss = () => {
      document.removeEventListener('pointerdown', onDocDown);
      document.removeEventListener('keydown', onKeyDown);
    };
    document.addEventListener('pointerdown', onDocDown);
    document.addEventListener('keydown', onKeyDown);
  },

  _hideMenu: function () {
    if (window.mixel._ctxDismiss) { window.mixel._ctxDismiss(); window.mixel._ctxDismiss = null; }
    if (window.mixel._ctxMenu) { window.mixel._ctxMenu.remove(); window.mixel._ctxMenu = null; }
  },

  listenContextMenu: function (id, dotNetRef) {
    const canvas = document.getElementById(id);
    if (!canvas) return;
    canvas.addEventListener('contextmenu', (e) => {
      e.preventDefault();
      window.mixel._showMenu(e.clientX, e.clientY, [
        { label: '↺  Reset all depths → 1',       action: 'reset-to-one' },
        { label: '✕  Erase all depths → 0',        action: 'erase-all'   },
        { label: '⬛  Fill all → current level',   action: 'fill-all'    },
        { label: '⇅  Invert depths',               action: 'invert'      },
      ], (action) => dotNetRef.invokeMethodAsync('OnContextAction', action));
    });
  },

  listenModelContextMenu: function (el) {
    if (!el) return;
    el.addEventListener('contextmenu', (e) => {
      e.preventDefault();
      window.mixel._showMenu(e.clientX, e.clientY, [
        { label: '↺  Reset camera', action: 'reset-camera' },
      ], (action) => {
        if (action === 'reset-camera') {
          el.cameraOrbit = 'auto auto 100%';
          el.fieldOfView = 'auto';
        }
      });
    });
  },

  listenCanvasInput: function (id, dotNetRef) {
    const canvas = document.getElementById(id);
    if (!canvas) return;
    const rect = () => canvas.getBoundingClientRect();
    const toPixel = (e) => {
      const r = rect();
      const x = Math.floor((e.clientX - r.left) / r.width  * canvas.width);
      const y = Math.floor((e.clientY - r.top)  / r.height * canvas.height);
      return { x, y };
    };
    canvas.__mixelDown = false;
    canvas.addEventListener("pointerdown", (e) => {
      canvas.__mixelDown = true;
      canvas.setPointerCapture(e.pointerId);
      const {x, y} = toPixel(e);
      dotNetRef.invokeMethodAsync("OnCanvasInput", x, y, e.buttons);
    });
    canvas.addEventListener("pointermove", (e) => {
      if (!canvas.__mixelDown) return;
      const {x, y} = toPixel(e);
      dotNetRef.invokeMethodAsync("OnCanvasInput", x, y, e.buttons);
    });
    canvas.addEventListener("pointerup", () => { canvas.__mixelDown = false; });
  },

  fetchBytes: async function (url) {
    const r = await fetch(url);
    if (!r.ok) throw new Error(`fetch ${url} → ${r.status}`);
    return new Uint8Array(await r.arrayBuffer());
  },

  // ---- spritesheet slicer ----
  _paintSlicer: function (overlay, liveRect) {
    if (!overlay || !overlay.__mixelNat) return;
    const { w, h } = overlay.__mixelNat;
    const ctx = overlay.getContext("2d");
    ctx.clearRect(0, 0, overlay.width, overlay.height);
    const sX = overlay.width / w, sY = overlay.height / h;
    ctx.font = "bold 12px monospace";
    ctx.textBaseline = "top";
    const one = (rg, withLabel) => {
      ctx.lineWidth = 2;
      ctx.strokeStyle = "rgba(255,255,255,0.95)";
      ctx.strokeRect(rg.x * sX + 1, rg.y * sY + 1, rg.w * sX - 2, rg.h * sY - 2);
      if (withLabel && rg.name != null) {
        const tw = ctx.measureText(rg.name).width + 8;
        ctx.fillStyle = "rgba(0,0,0,0.75)";
        ctx.fillRect(rg.x * sX + 1, rg.y * sY + 1, tw, 16);
        ctx.fillStyle = "#ffffff";
        ctx.fillText(rg.name, rg.x * sX + 5, rg.y * sY + 3);
      }
    };
    for (const rg of (overlay.__mixelRegions || [])) one(rg, true);
    if (liveRect) one(liveRect, false);
  },

  initSlicer: function (baseId, overlayId, rgbaBytes, w, h, dotNetRef) {
    const base = document.getElementById(baseId);
    const overlay = document.getElementById(overlayId);
    if (!base || !overlay) return;

    base.width = w; base.height = h;
    const wrap = base.closest(".slicer-canvas-wrap") || base.parentElement;
    let scale = 1;
    if (wrap) {
      const availW = wrap.clientWidth - 32;
      const availH = wrap.clientHeight - 32;
      scale = Math.max(1, Math.min(Math.floor(availW / w), Math.floor(availH / h)));
    }
    const dispW = w * scale, dispH = h * scale;
    base.style.width = dispW + "px";
    base.style.height = dispH + "px";

    const bctx = base.getContext("2d");
    const imageData = bctx.createImageData(w, h);
    for (let i = 0; i < rgbaBytes.length; i++) imageData.data[i] = rgbaBytes[i];
    bctx.putImageData(imageData, 0, 0);

    overlay.width = dispW; overlay.height = dispH;
    overlay.style.width = dispW + "px";
    overlay.style.height = dispH + "px";
    overlay.__mixelNat = { w, h };
    overlay.__mixelRegions = [];

    const toPixel = (e) => {
      const r = overlay.getBoundingClientRect();
      let x = Math.floor((e.clientX - r.left) / r.width  * w);
      let y = Math.floor((e.clientY - r.top)  / r.height * h);
      x = Math.max(0, Math.min(w - 1, x));
      y = Math.max(0, Math.min(h - 1, y));
      return { x, y };
    };

    let dragging = false, sx = 0, sy = 0;
    const norm = (px, py) => ({
      x: Math.min(sx, px), y: Math.min(sy, py),
      w: Math.abs(px - sx) + 1, h: Math.abs(py - sy) + 1,
    });

    const onDown = (e) => {
      dragging = true;
      overlay.setPointerCapture(e.pointerId);
      const p = toPixel(e); sx = p.x; sy = p.y;
    };
    const onMove = (e) => {
      if (!dragging) return;
      const p = toPixel(e);
      window.mixel._paintSlicer(overlay, norm(p.x, p.y));
    };
    const onUp = (e) => {
      if (!dragging) return;
      dragging = false;
      const p = toPixel(e);
      const r = norm(p.x, p.y);
      if (r.x + r.w > w) r.w = w - r.x;
      if (r.y + r.h > h) r.h = h - r.y;
      window.mixel._paintSlicer(overlay, null);
      dotNetRef.invokeMethodAsync("OnRegionDrawn", r.x, r.y, r.w, r.h);
    };

    overlay.addEventListener("pointerdown", onDown);
    overlay.addEventListener("pointermove", onMove);
    overlay.addEventListener("pointerup", onUp);
    overlay.__mixelSlicerCleanup = () => {
      overlay.removeEventListener("pointerdown", onDown);
      overlay.removeEventListener("pointermove", onMove);
      overlay.removeEventListener("pointerup", onUp);
    };
  },

  renderSlicerRegions: function (overlayId, regions) {
    const overlay = document.getElementById(overlayId);
    if (!overlay) return;
    overlay.__mixelRegions = regions || [];
    window.mixel._paintSlicer(overlay, null);
  },

  disposeSlicer: function (overlayId) {
    const overlay = document.getElementById(overlayId);
    if (!overlay) return;
    if (overlay.__mixelSlicerCleanup) { overlay.__mixelSlicerCleanup(); overlay.__mixelSlicerCleanup = null; }
    overlay.__mixelRegions = null;
    overlay.__mixelNat = null;
  },

};
