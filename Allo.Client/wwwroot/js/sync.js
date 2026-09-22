// Tells the sync coordinator when it's worth syncing: the app came back to the
// foreground, or the network came back.
window.alloSync = {
    register(coordinator) {
        const wake = () => coordinator.invokeMethodAsync('OnWake');
        document.addEventListener('visibilitychange', () => {
            if (document.visibilityState === 'visible') {
                wake();
            }
        });
        window.addEventListener('online', wake);
    },
};
