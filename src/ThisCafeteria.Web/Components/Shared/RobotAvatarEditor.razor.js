// Opens the avatar editor as a real modal dialog.
//
// <dialog>.showModal() is used rather than a hand-rolled overlay so the focus
// trap, the inert background and Esc all come from the platform. The catch is
// that Esc and a backdrop click close the element directly, behind Blazor's
// back — so both are cancelled here and routed through .NET instead, or the
// parent's "editor is open" flag would still be true over a closed dialog and
// the button would stop reopening it.

export function open(dialog, dotNetRef) {
    if (!dialog || dialog.dataset.raeWired === "1") {
        return;
    }

    dialog.dataset.raeWired = "1";

    dialog.addEventListener("cancel", event => {
        event.preventDefault();
        dotNetRef.invokeMethodAsync("DismissedAsync");
    });

    dialog.addEventListener("click", event => {
        // ::backdrop clicks land on the dialog element itself; clicks on the
        // content land on a descendant.
        if (event.target === dialog) {
            dotNetRef.invokeMethodAsync("DismissedAsync");
        }
    });

    if (!dialog.open) {
        dialog.showModal();
    }
}

export function close(dialog) {
    if (dialog?.open) {
        dialog.close();
    }
}
