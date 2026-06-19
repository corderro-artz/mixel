window.mixel = {
  _urls: new WeakMap(),
  setModelSrc: function (el, bytes) {
    if (!el) return;
    const old = this._urls.get(el);
    if (old) URL.revokeObjectURL(old);
    const blob = new Blob([bytes], { type: "model/gltf-binary" });
    const url = URL.createObjectURL(blob);
    this._urls.set(el, url);
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
  }
};
