// Auto-logout after IDLE_TIMEOUT_MS of no user activity, warning WARNING_LEAD_MS before it fires.
// Tuning knobs - safe to change these two values only:
const IDLE_TIMEOUT_MS = 15 * 60 * 1000;
const WARNING_LEAD_MS = 60 * 1000;

const ACTIVITY_BROADCAST_THROTTLE_MS = 1000;
const STORAGE_KEY = "artisanalbrew:lastActivity";
const LOGOUT_FORM_SELECTOR = 'form[action="/api/wallet-auth/logout"], form[action="/admin/logout"]';
const ACTIVITY_EVENTS = ["mousemove", "mousedown", "keydown", "touchstart", "scroll", "wheel"];

const dialog = document.getElementById("idle-timeout-modal");
const secondsLabel = document.getElementById("idle-timeout-seconds");
const stayButton = document.getElementById("idle-timeout-stay-button");

let lastActivityAt = Date.now();
let lastBroadcastAt = 0;
let warnTimerId = null;
let logoutTimerId = null;
let countdownIntervalId = null;

function closeDialog() {
    if (dialog.open) {
        dialog.close();
    }
    if (countdownIntervalId !== null) {
        clearInterval(countdownIntervalId);
        countdownIntervalId = null;
    }
    if (logoutTimerId !== null) {
        clearTimeout(logoutTimerId);
        logoutTimerId = null;
    }
}

function scheduleTimers() {
    if (warnTimerId !== null) {
        clearTimeout(warnTimerId);
    }
    warnTimerId = setTimeout(onWarnFire, IDLE_TIMEOUT_MS - WARNING_LEAD_MS);
}

function onWarnFire() {
    if (!document.querySelector(LOGOUT_FORM_SELECTOR)) {
        // Nobody's logged in on this page - nothing to warn about.
        return;
    }

    if (!dialog.open) {
        dialog.showModal();
    }

    let remainingSeconds = Math.round(WARNING_LEAD_MS / 1000);
    secondsLabel.textContent = String(remainingSeconds);
    countdownIntervalId = setInterval(() => {
        remainingSeconds = Math.max(remainingSeconds - 1, 0);
        secondsLabel.textContent = String(remainingSeconds);
    }, 1000);

    logoutTimerId = setTimeout(onLogoutFire, WARNING_LEAD_MS);
}

function onLogoutFire() {
    const form = document.querySelector(LOGOUT_FORM_SELECTOR);
    closeDialog();
    if (form) {
        form.requestSubmit();
    }
}

function onActivity() {
    lastActivityAt = Date.now();
    closeDialog();
    scheduleTimers();

    if (lastActivityAt - lastBroadcastAt > ACTIVITY_BROADCAST_THROTTLE_MS) {
        lastBroadcastAt = lastActivityAt;
        try {
            localStorage.setItem(STORAGE_KEY, String(lastActivityAt));
        } catch {
            // Storage unavailable (private browsing, quota, etc.) - local tracking still works.
        }
    }
}

function onStorageEvent(event) {
    if (event.key !== STORAGE_KEY || !event.newValue) {
        return;
    }

    const broadcastAt = Number(event.newValue);
    if (Number.isFinite(broadcastAt) && broadcastAt > lastActivityAt) {
        lastActivityAt = broadcastAt;
        closeDialog();
        scheduleTimers();
    }
}

ACTIVITY_EVENTS.forEach(eventName => document.addEventListener(eventName, onActivity, { passive: true }));
stayButton.addEventListener("click", onActivity);
window.addEventListener("storage", onStorageEvent);

scheduleTimers();
