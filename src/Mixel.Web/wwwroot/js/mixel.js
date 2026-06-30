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

};
