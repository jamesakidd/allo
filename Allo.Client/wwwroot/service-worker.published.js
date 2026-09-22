// Caches the whole app shell so the list opens in a dead spot, and stays out of the way
// of the API. Based on the Blazor template, with two changes that matter in a store:
// an update never applies until the user asks for it, and an update only downloads the
// parts of the build that actually changed.

self.importScripts('./service-worker-assets.js');

self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));
self.addEventListener('message', event => {
    // The page sends this when the user taps Update on the snackbar, and not before.
    if (event.data === 'skip-waiting') {
        self.skipWaiting();
    }
});

const cacheNamePrefix = 'allo-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
// Our own record of what went into a cache, so the next build can tell what it can keep.
const hashesKey = 'allo-asset-hashes';

const base = '/';
const baseUrl = new URL(base, self.origin);

const offlineAssetsInclude = [/\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/];
const offlineAssetsExclude = [/^service-worker\.js$/];

const shellAssets = self.assetsManifest.assets
    .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
    .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)));
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall() {
    const cache = await caches.open(cacheName);
    const previous = await previousBuild();

    await Promise.all(shellAssets.map(async asset => {
        // Most of a new build is byte-identical to the last one. Carrying those entries
        // over turns an update into a few hundred KB instead of the entire payload.
        const unchanged = previous.cache && previous.hashes[asset.url] === asset.hash
            ? await previous.cache.match(asset.url)
            : null;
        if (unchanged) {
            await cache.put(asset.url, unchanged);
            return;
        }
        const response = await fetch(new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
        if (!response.ok) {
            // Fail the install. A half-cached shell is worse than no service worker:
            // it would serve a broken app offline and never correct itself.
            throw new Error(`Service worker: ${asset.url} returned ${response.status}`);
        }
        await cache.put(asset.url, response);
    }));

    const hashes = Object.fromEntries(shellAssets.map(asset => [asset.url, asset.hash]));
    await cache.put(hashesKey, new Response(JSON.stringify(hashes)));
}

// The newest older cache that recorded its hashes, if there is one.
async function previousBuild() {
    for (const key of await caches.keys()) {
        if (!key.startsWith(cacheNamePrefix) || key === cacheName) {
            continue;
        }
        const cache = await caches.open(key);
        const recorded = await cache.match(hashesKey);
        if (recorded) {
            return { cache, hashes: await recorded.json() };
        }
    }
    return { cache: null, hashes: {} };
}

async function onActivate() {
    const keys = await caches.keys();
    await Promise.all(keys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
    // Take over the open pages immediately; they reload themselves once we do.
    await self.clients.claim();
}

async function onFetch(event) {
    const request = event.request;
    const url = new URL(request.url);

    // The API is never cached. A stale 200 for a sync pull or an auth check would be far
    // worse than an honest failure, which the sync queue already knows how to retry.
    const isApi = url.origin === self.origin && (url.pathname.startsWith('/api/') || url.pathname === '/healthz');
    if (request.method !== 'GET' || url.origin !== self.origin || isApi) {
        return fetch(request);
    }

    // Any in-app URL gets the cached shell; Blazor routes from there.
    const shouldServeIndexHtml = request.mode === 'navigate'
        && !manifestUrlList.some(manifestUrl => manifestUrl === request.url);
    const cache = await caches.open(cacheName);
    const cached = await cache.match(shouldServeIndexHtml ? 'index.html' : request);
    return cached || fetch(request);
}
