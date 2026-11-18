// The search for a reference code as a clinician types: a diagnosis, a medication or an allergen. An input marked
// data-code-search="<kind>" gets its list's options from /codes, a moment after the typing stops, so the browser's own
// list (arrow keys, Enter) offers the matches. The form checks the name again on the server. Without JavaScript the
// name is typed in full. Nothing about the patient is sent, and nothing is kept in browser storage.
"use strict";

(() => {
    let timer = 0;
    let latest = 0;
    document.addEventListener("input", (event) => {
        const input = event.target.closest("input[data-code-search]");
        const list = input?.list;
        if (!list) {
            return;
        }

        clearTimeout(timer);
        const typed = input.value.trim();
        if (typed.length < 2) {
            list.replaceChildren();
            return;
        }

        timer = setTimeout(async () => {
            const request = ++latest;
            try {
                const address = `/codes?kind=${encodeURIComponent(input.dataset.codeSearch)}&q=${encodeURIComponent(typed)}`;
                const response = await fetch(address, { credentials: "same-origin", headers: { Accept: "text/html" } });
                if (response.ok && request === latest) {
                    list.innerHTML = await response.text();
                }
            } catch {
                // No suggestions this time: the name can still be typed in full.
            }
        }, 120);
    });
})();
