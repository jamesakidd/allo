// Long-press on a task row starts selection mode. No native API is involved: a timer starts
// when a finger or button goes down on a row and is cancelled by lifting it, or by moving
// far enough that it's a scroll rather than a hold.
window.alloLongPress = (() => {
    const HOLD_MS = 500;
    const MOVE_TOLERANCE_PX = 10;

    let page = null;            // the Tasks page currently on screen
    let swallowNextClick = false;
    let selectEntryPushed = false;

    // Releasing after a long-press is also a tap, and that tap would undo the selection it
    // just made. Swallow it here — the window's capture phase runs before Blazor sees the
    // event. Any new press starts a new gesture, so the flag can't outlive the one it's for
    // (touch browsers often send no click at all after a hold).
    window.addEventListener('click', e => {
        if (swallowNextClick) {
            swallowNextClick = false;
            e.preventDefault();
            e.stopImmediatePropagation();
        }
    }, true);
    window.addEventListener('pointerdown', () => { swallowNextClick = false; }, true);

    // Android's back gesture should end selection, not leave the page. Entering selection
    // pushes a history entry; going back pops it, which lands here.
    window.addEventListener('popstate', () => {
        if (selectEntryPushed && !(history.state && history.state.alloSelecting)) {
            selectEntryPushed = false;
            page?.invokeMethodAsync('OnSelectionBack');
        }
    });

    function register(container, dotnet) {
        page = dotnet;
        let timer = null;
        let origin = null;
        const cancel = () => { clearTimeout(timer); timer = null; };

        container.addEventListener('pointerdown', e => {
            const row = e.target.closest('[data-task-id]');
            // Only on task rows, only with the primary button, and only when the page says
            // selection is available (there must be another list to move to).
            if (!row || e.button > 0 || container.dataset.longPress !== 'on') {
                return;
            }
            origin = { x: e.clientX, y: e.clientY };
            cancel();
            timer = setTimeout(() => {
                timer = null;
                swallowNextClick = true;
                // Best effort: Android honours it, iPhones don't allow it.
                if (navigator.vibrate) {
                    try { navigator.vibrate(30); } catch { }
                }
                page?.invokeMethodAsync('OnLongPress', row.dataset.taskId);
            }, HOLD_MS);
        });
        container.addEventListener('pointermove', e => {
            if (timer && origin
                && Math.hypot(e.clientX - origin.x, e.clientY - origin.y) > MOVE_TOLERANCE_PX) {
                cancel();
            }
        });
        for (const type of ['pointerup', 'pointercancel', 'pointerleave']) {
            container.addEventListener(type, cancel);
        }
        // The browser's own long-press menu (copy, select all) — on task rows only.
        container.addEventListener('contextmenu', e => {
            if (e.target.closest('[data-task-id]')) {
                e.preventDefault();
            }
        });
    }

    function unregister() {
        page = null;
    }

    function enterSelection() {
        if (!selectEntryPushed) {
            history.pushState({ alloSelecting: true }, '');
            selectEntryPushed = true;
        }
    }

    // Leaving by Cancel or after a move: take our history entry back off, so the next
    // back press leaves the page as it normally would.
    function leaveSelection() {
        if (selectEntryPushed && history.state && history.state.alloSelecting) {
            selectEntryPushed = false;   // first, so the popstate this causes isn't taken as Back
            history.back();
        } else {
            selectEntryPushed = false;
        }
    }

    return { register, unregister, enterSelection, leaveSelection };
})();
