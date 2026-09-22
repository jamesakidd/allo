// Drag down at the top of the list to sync, the way every other mobile list works.
window.alloPullToRefresh = {
    register(element, handler) {
        const threshold = 70;
        let startY = null;

        element.addEventListener('touchstart', e => {
            startY = element.scrollTop <= 0 ? e.touches[0].clientY : null;
        }, { passive: true });

        element.addEventListener('touchmove', e => {
            if (startY === null) {
                return;
            }
            const pulled = Math.max(0, e.touches[0].clientY - startY);
            element.style.setProperty('--allo-pull', Math.min(pulled, threshold * 1.5) + 'px');
        }, { passive: true });

        const end = () => {
            if (startY === null) {
                return;
            }
            const pulled = parseFloat(element.style.getPropertyValue('--allo-pull')) || 0;
            element.style.setProperty('--allo-pull', '0px');
            startY = null;
            if (pulled >= threshold) {
                handler.invokeMethodAsync('OnPullToRefresh');
            }
        };
        element.addEventListener('touchend', end, { passive: true });
        element.addEventListener('touchcancel', end, { passive: true });
    },
};
