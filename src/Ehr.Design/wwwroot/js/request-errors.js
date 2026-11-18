// Shows a message when an htmx request fails, so a save never fails silently: no connection, or an error from the
// server. The page provides the place for it, an element with id "request-error", and the next request that
// succeeds clears it.
"use strict";

(() => {
    function messageFor(detail) {
        const status = detail.xhr ? detail.xhr.status : 0;
        if (detail.requestConfig.verb === "get") {
            return "Couldn't load that. Try again in a moment.";
        }

        return status === 0 || status >= 500
            ? "Not saved: the server is unavailable. Your changes are still on screen; try again in a moment."
            : "Not saved. Reload the page and try again.";
    }

    // A message about a state starts with it and a colon ("Not saved: ..."); the state is marked up as the notice's
    // leading words (docs/brand: Notice). The text is the same either way.
    function show(event) {
        const area = document.getElementById("request-error");
        if (area) {
            const message = messageFor(event.detail);
            const colon = message.indexOf(": ");
            if (colon > 0) {
                const state = document.createElement("span");
                state.className = "sc-notice__state";
                state.textContent = message.slice(0, colon + 1);
                area.replaceChildren(state, message.slice(colon + 1));
            } else {
                area.textContent = message;
            }

            area.hidden = false;
        }
    }

    // No response at all, then a response with an error status.
    document.addEventListener("htmx:sendError", show);
    document.addEventListener("htmx:responseError", show);

    document.addEventListener("htmx:afterRequest", (event) => {
        const area = document.getElementById("request-error");
        if (area && event.detail.successful) {
            area.hidden = true;
        }
    });
})();
