// Shown once per browser session to explain the shop only spends free Sepolia test ETH.
const STORAGE_KEY = "artisanalbrew:testnetNoticeSeen";

const dialog = document.getElementById("testnet-info-modal");
const closeButton = document.getElementById("testnet-info-close");
const dismissButton = document.getElementById("testnet-info-dismiss");

function markSeen() {
    try {
        sessionStorage.setItem(STORAGE_KEY, "1");
    } catch {
        // Storage unavailable (private browsing, quota, etc.) - modal will just reopen next load.
    }
}

function closeDialog() {
    if (dialog.open) {
        dialog.close();
    }
    markSeen();
}

closeButton.addEventListener("click", closeDialog);
dismissButton.addEventListener("click", closeDialog);
dialog.addEventListener("cancel", markSeen);
dialog.addEventListener("click", event => {
    if (event.target === dialog) {
        closeDialog();
    }
});

let alreadySeen = false;
try {
    alreadySeen = sessionStorage.getItem(STORAGE_KEY) === "1";
} catch {
    alreadySeen = false;
}

if (!alreadySeen && !dialog.open) {
    dialog.showModal();
}
