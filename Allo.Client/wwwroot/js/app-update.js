// Registers the service worker and tells the app when a newer build is waiting.
// Nothing reloads until the user taps Update, or until the app is next cold-started —
// a reload in the middle of adding items in a store is not acceptable.
window.alloUpdate = (() => {
    let waiting = null;
    let reloading = false;

    async function register(app) {
        if (!('serviceWorker' in navigator)) {
            return;
        }

        // A page with no controller is a first install, not an update. Captured now,
        // because claiming the page is what changes it.
        const hadController = !!navigator.serviceWorker.controller;
        const registration = await navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' });

        const announce = () => {
            if (!hadController || !registration.waiting) {
                return;
            }
            waiting = registration.waiting;
            app.invokeMethodAsync('OnUpdateReady');
        };

        announce();
        registration.addEventListener('updatefound', () => {
            const installing = registration.installing;
            installing?.addEventListener('statechange', () => {
                if (installing.state === 'installed') {
                    announce();
                }
            });
        });

        // The new worker taking over means the cached assets no longer match the running
        // app, so reload once to land on a single version.
        navigator.serviceWorker.addEventListener('controllerchange', () => {
            if (!hadController || reloading) {
                return;
            }
            reloading = true;
            location.reload();
        });

        // Catches a deploy that landed while the app sat open in the background,
        // which on a phone is most of the time.
        document.addEventListener('visibilitychange', () => {
            if (document.visibilityState === 'visible') {
                registration.update().catch(() => { });
            }
        });
    }

    function apply() {
        waiting?.postMessage('skip-waiting');
    }

    return { register, apply };
})();
