export async function loginWithWallet(walletName = "Solana wallet") {
    const wallet = await discoverWallet(walletName);
    if (!wallet) return { success: false, error: "A Solana Wallet Standard wallet was not found." };
    try {
        const connect = wallet.features?.["standard:connect"];
        if (!connect) throw new Error("This wallet does not support Wallet Standard connection.");
        const connected = await connect.connect();
        const account = connected.accounts?.[0] ?? wallet.accounts?.[0];
        if (!account?.address || !account.publicKey) throw new Error("No Solana account was selected.");
        const chainState = await fetch("/api/chains", { credentials: "same-origin" }).then(response => response.json());
        const challenge = await postJson("/api/wallet-auth/solana/challenge", { address: account.address, chainKey: chainState.selectedChainKey, walletName });
        const signer = account.features?.["solana:signMessage"] ?? wallet.features?.["solana:signMessage"];
        if (!signer?.signMessage) throw new Error("This wallet does not support Solana message signing.");
        const signedResult = await signer.signMessage({ account, message: new TextEncoder().encode(challenge.message) });
        const signed = Array.isArray(signedResult) ? signedResult[0] : signedResult;
        const signature = encodeBase58(signed.signature ?? signed.signatures?.[0]);
        const verification = await postJson("/api/wallet-auth/solana/verify", { address: account.address, signature, message: challenge.message, nonce: challenge.nonce, chainKey: challenge.chainKey, walletName });
        return { success: verification.success, address: verification.address, redirectUrl: verification.redirectUrl };
    } catch (error) {
        return { success: false, error: error?.message || "Solana wallet login failed." };
    }
}

export async function connectedWallet(walletName = "Solana wallet") {
    const wallet = await discoverWallet(walletName);
    if (!wallet) throw new Error("A Solana Wallet Standard wallet was not found.");
    const connect = wallet.features?.["standard:connect"];
    if (!connect?.connect) throw new Error("This wallet does not support Wallet Standard connection.");
    const connected = await connect.connect();
    const account = connected.accounts?.[0] ?? wallet.accounts?.[0];
    if (!account?.address || !account.publicKey) throw new Error("No Solana account was selected.");
    return { wallet, account };
}

async function discoverWallet(walletName) {
    const wallets = [];
    if (Array.isArray(window.navigator?.wallets)) wallets.push(...window.navigator.wallets);
    await new Promise(resolve => {
        const handler = event => { const wallet = event.detail?.wallet ?? event.detail; if (wallet) wallets.push(wallet); };
        window.addEventListener("wallet-standard:app-ready", handler, { once: false });
        window.dispatchEvent(new Event("wallet-standard:app-ready"));
        window.setTimeout(() => { window.removeEventListener("wallet-standard:app-ready", handler); resolve(); }, 100);
    });
    return wallets.find(wallet => wallet.name?.toLowerCase().includes(walletName.toLowerCase())) ?? wallets.find(wallet => wallet.features?.["standard:connect"] && wallet.features?.["solana:signMessage"]);
}

function encodeBase58(bytes) {
    if (!bytes) throw new Error("The wallet returned no signature.");
    const alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
    const digits = [0];
    for (const value of bytes) {
        let carry = value;
        for (let i = 0; i < digits.length; i++) { carry += digits[i] * 256; digits[i] = carry % 58; carry = Math.floor(carry / 58); }
        while (carry) { digits.push(carry % 58); carry = Math.floor(carry / 58); }
    }
    let result = "";
    for (const value of bytes) { if (value !== 0) break; result += "1"; }
    for (let i = digits.length - 1; i >= 0; i--) result += alphabet[digits[i]];
    return result;
}

async function postJson(url, payload) {
    const response = await fetch(url, { method: "POST", credentials: "same-origin", headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": csrf() }, body: JSON.stringify(payload) });
    if (!response.ok) throw new Error(await response.text() || "The server rejected the wallet request.");
    return response.json();
}

function csrf() { return decodeURIComponent(document.cookie.match(/(?:^|; )XSRF-TOKEN=([^;]*)/)?.[1] || ""); }
