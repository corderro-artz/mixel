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
    const overlay = ctx.createImageData(w, h);
    for (let i = 0; i < levels.length; i++) {
      const lvl = levels[i];
      if (lvl === 0) continue;
      const hue = Math.round((lvl - 1) / Math.max(maxDepth - 1, 1) * 270);
      const [r, g, b] = window.mixel._hslToRgb(hue / 360, 1, 0.5);
      overlay.data[i * 4]     = r;
      overlay.data[i * 4 + 1] = g;
      overlay.data[i * 4 + 2] = b;
      overlay.data[i * 4 + 3] = 160;
    }
    const tmp = document.createElement("canvas");
    tmp.width = w; tmp.height = h;
    tmp.getContext("2d").putImageData(overlay, 0, 0);
    ctx.drawImage(tmp, 0, 0);
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

  _hslToRgb: function (h, s, l) {
    let r, g, b;
    if (s === 0) { r = g = b = l; }
    else {
      const hue2rgb = (p, q, t) => {
        if (t < 0) t += 1; if (t > 1) t -= 1;
        if (t < 1/6) return p + (q - p) * 6 * t;
        if (t < 1/2) return q;
        if (t < 2/3) return p + (q - p) * (2/3 - t) * 6;
        return p;
      };
      const q = l < 0.5 ? l * (1 + s) : l + s - l * s;
      const p = 2 * l - q;
      r = hue2rgb(p, q, h + 1/3);
      g = hue2rgb(p, q, h);
      b = hue2rgb(p, q, h - 1/3);
    }
    return [Math.round(r * 255), Math.round(g * 255), Math.round(b * 255)];
  }
};
