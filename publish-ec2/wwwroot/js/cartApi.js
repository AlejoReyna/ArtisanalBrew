export async function addItem(slug, quantity = 1) {
    const response = await fetch("/api/cart/items", {
        method: "POST",
        credentials: "include",
        headers: {
            "Content-Type": "application/json"
        },
        body: JSON.stringify({ slug, quantity })
    });

    if (!response.ok) {
        const body = await response.text();
        throw new Error(body || "Cart could not be updated.");
    }

    return response.json();
}
