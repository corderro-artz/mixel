// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/ ];

// Cross-origin assets (Google Fonts) aren't in the Blazor asset manifest, so they
// are cached at runtime on first (online) load and reused offline thereafter. This
// cache is version-independent so font files survive app updates.
const runtimeCacheName = `${cacheNamePrefix}runtime`;
const runtimeCacheHosts = [ 'fonts.googleapis.com', 'fonts.gstatic.com' ];

// Replace with your base path if you are hosting on a subfolder. Ensure there is a trailing '/'.
const base = "/";
const baseUrl = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall(event) {
    console.info('Service worker: Install');

    // Fetch and cache all matching items from the assets manifest
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    // Delete unused caches, but keep the version-independent runtime (font) cache.
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName && key !== runtimeCacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    // Runtime cache-first for Google Fonts: serve from cache when present, otherwise
    // fetch and store. Falls back to whatever is cached (or a network error) offline.
    if (event.request.method === 'GET') {
        const host = new URL(event.request.url).host;
        if (runtimeCacheHosts.includes(host)) {
            const cache = await caches.open(runtimeCacheName);
            const hit = await cache.match(event.request);
            if (hit) return hit;
            try {
                const response = await fetch(event.request);
                // Only cache genuinely successful responses. Google Fonts serves both
                // the CSS and the font files with CORS, so response.ok is meaningful;
                // never cache an error/opaque response or it sticks permanently offline.
                if (response && response.ok)
                    await cache.put(event.request, response.clone());
                return response;
            } catch (e) {
                // Offline with no cached copy: nothing to serve, let the CSS fallback fonts apply.
                return Response.error();
            }
        }
    }

    let cachedResponse = null;
    if (event.request.method === 'GET') {
        // For all navigation requests, try to serve index.html from cache,
        // unless that request is for an offline resource.
        // If you need some URLs to be server-rendered, edit the following check to exclude those URLs
        const shouldServeIndexHtml = event.request.mode === 'navigate'
            && !manifestUrlList.some(url => url === event.request.url);

        const request = shouldServeIndexHtml ? 'index.html' : event.request;
        const cache = await caches.open(cacheName);
        cachedResponse = await cache.match(request);
    }

    return cachedResponse || fetch(event.request);
}
