const TOAST_DURATION_MS = 4200;

function ensureHost() {
    let host = document.getElementById("cart-toast-host");
    if (host) {
        return host;
    }

    host = document.createElement("div");
    host.id = "cart-toast-host";
    host.className = "cart-toast-host";
    host.setAttribute("aria-live", "polite");
    host.setAttribute("aria-relevant", "additions");
    document.body.appendChild(host);
    return host;
}

function dismissToast(toast) {
    if (!toast || toast.classList.contains("cart-toast--leaving")) {
        return;
    }

    toast.classList.add("cart-toast--leaving");
    window.setTimeout(() => toast.remove(), 280);
}

function show(message, variant = "success") {
    const host = ensureHost();
    const toast = document.createElement("div");
    toast.className = `cart-toast cart-toast--${variant}`;
    toast.setAttribute("role", "status");
    toast.textContent = message;

    const dismissButton = document.createElement("button");
    dismissButton.type = "button";
    dismissButton.className = "cart-toast__dismiss";
    dismissButton.setAttribute("aria-label", "Dismiss notification");
    dismissButton.textContent = "×";
    dismissButton.addEventListener("click", () => dismissToast(toast));
    toast.appendChild(dismissButton);

    host.appendChild(toast);

    requestAnimationFrame(() => toast.classList.add("cart-toast--visible"));

    window.setTimeout(() => dismissToast(toast), TOAST_DURATION_MS);
}

window.cartToast = { show };
