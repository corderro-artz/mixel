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
  setThemeAttribute: function (id) { document.documentElement.setAttribute("data-theme", id); }
};
