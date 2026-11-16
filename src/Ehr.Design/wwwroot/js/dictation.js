// Dictating a note (UC 9 Transcribe dictated audio, UC 10 Structure and validate note fields; Samir Mukshe). The script
// for Areas/Clinical/Pages/Patients/Dictate: it records the microphone, sends the recording to the clinician dictation
// API (Ehr.Web, Api/Dictation/DictationApi.cs, whose header is the contract) in numbered chunks, and shows the
// transcript as the clinician speaks. After Stop it asks for the result and shows the History, Assessment and Plan the
// model proposed and validation let through, each awaiting review, or says why the fields couldn't be sorted.
//
// What it handles, from UC 9 and UC 10's alternative courses:
// - no microphone, or one the browser blocks: says how to fix it, and the page stays usable;
// - a dropped connection: recording carries on, the chunks wait here in order, and they are sent again with backoff,
//   from the one the API expects next (409 out_of_order), so the transcript resumes where it paused;
// - a passage too quiet, too noisy or too uncertain to trust: marked in the transcript, to say again;
// - the model unavailable or too slow: the transcript stays as plain text, with the API's message;
// - an order or allergy change dictated: listed as not carried over, never filled into a field (FR-14);
// - Cancel: the session is deleted, so its audio and transcript are discarded, and the screen is cleared.
// Nothing here saves anything (FR-13). The audio goes up in whatever format the browser records (AGENTS.md: never
// hard-code one), and every piece of text is set with textContent, never parsed as HTML. Wired here rather than in
// attributes, so a strict CSP can be added later.
"use strict";

(() => {
    const root = document.querySelector("[data-dictation]");
    if (!root) {
        return;
    }

    const part = (name) => root.querySelector(`[data-dictation-${name}]`);
    const api = root.dataset.dictationApi;
    const token = root.querySelector("input[name=__RequestVerificationToken]")?.value ?? "";
    const startButton = part("start");
    const stopButton = part("stop");
    const cancelButton = part("cancel");
    const statusArea = part("status");
    const transcriptArea = part("transcript");
    const unclearNotice = part("unclear");
    const sortMessage = part("sort-message");
    const notCarried = part("not-carried");
    const notCarriedList = part("not-carried-list");
    const fieldNames = ["history", "assessment", "plan"];

    // Words for the clinician beside each marked passage and each code the API sends.
    const unclearWords = {
        too_quiet: "too quiet to hear clearly",
        too_noisy: "too much background noise",
        low_confidence: "the speech model wasn't sure",
    };
    const warningWords = {
        possible_correction: "You may have corrected this later in the dictation: check the highlighted words.",
        near_unclear_passage: "A passage beside its source was hard to hear: check the highlighted words.",
    };
    const instructionWords = { order: "Order", allergy_change: "Allergy change" };

    // How long to wait before sending again after a failure: doubling, up to the most.
    const firstRetryMs = 1000;
    const mostRetryMs = 10000;
    // Warn this many seconds before the dictation's limit.
    const warnSeconds = 30;

    let state = fresh();

    function fresh() {
        return {
            session: null,
            stream: null,
            recorder: null,
            queue: [],
            nextSeq: 0,
            sending: false,
            paused: false,
            retryMs: firstRetryMs,
            retryTimer: 0,
            stopRequested: false,
            stopping: false,
            ended: false,
            cancelled: false,
            transcript: null,
        };
    }

    function say(text, isError = false) {
        statusArea.textContent = text;
        statusArea.classList.toggle("error", isError);
    }

    function buttons(recording) {
        startButton.disabled = recording;
        stopButton.disabled = !recording;
        cancelButton.disabled = !recording && state.session === null;
    }

    async function call(method, path, body, type) {
        const headers = { RequestVerificationToken: token };
        if (type) {
            headers["Content-Type"] = type;
        }

        return fetch(api + path, { method, headers, body, credentials: "same-origin" });
    }

    async function json(response) {
        try {
            return await response.json();
        } catch {
            return {};
        }
    }

    // The API's own words when it gives them; a status-based fallback otherwise.
    async function refusal(response) {
        const body = await json(response);
        return body.message || `Dictation failed (${response.status}). Try again in a moment.`;
    }

    // A failure worth sending again: no connection, the service busy, unavailable or slow, or the cloud app unable to
    // reach the on-prem app for a moment.
    function transient(status) {
        return status === 0 || status === 429 || status >= 500;
    }

    // UC 9 step 1: the microphone first, so nothing starts on the server when it can't be recorded.
    async function start() {
        if (!navigator.mediaDevices?.getUserMedia || !window.MediaRecorder) {
            say("This browser can't record audio. Use a current version of Chrome, Edge, Firefox or Safari, or type the note.", true);
            return;
        }

        // A finished dictation still on screen is discarded before the next starts.
        if (state.session) {
            call("DELETE", `/${state.session.sessionId}`).catch(() => { });
        }

        state = fresh();
        resetScreen();
        cancelButton.textContent = "Cancel";
        try {
            state.stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        } catch (error) {
            say(microphoneProblem(error), true);
            return;
        }

        let response;
        try {
            response = await call("POST", "");
        } catch {
            endRecording();
            say("The dictation service can't be reached. Check your connection and try again.", true);
            return;
        }

        if (response.status !== 201) {
            endRecording();
            say(await refusal(response), true);
            return;
        }

        state.session = await json(response);
        // No mimeType: the browser picks the format it records (WebM in Chromium, MP4 in WebKit), and the server
        // decodes either.
        const recorder = new MediaRecorder(state.stream);
        state.recorder = recorder;
        recorder.addEventListener("dataavailable", (event) => {
            if (event.data && event.data.size > 0 && !state.cancelled) {
                state.queue.push({ seq: state.nextSeq++, blob: event.data, type: event.data.type || recorder.mimeType || "audio/*" });
                pump();
            }
        });
        recorder.addEventListener("stop", () => {
            stopTracks();
            if (!state.cancelled) {
                state.stopRequested = true;
                pump();
            }
        });
        recorder.start(1000);
        buttons(true);
        say("Recording. Speak your note, then select Stop.");
    }

    function microphoneProblem(error) {
        switch (error?.name) {
            case "NotAllowedError":
            case "SecurityError":
                return "The microphone is blocked. Allow it for this site in the browser's settings (the icon beside the address), then select Dictate again. You can still type the note.";
            case "NotFoundError":
            case "OverconstrainedError":
                return "No microphone was found. Connect one, then select Dictate again. You can still type the note.";
            case "NotReadableError":
                return "The microphone is in use by another app. Close it, then select Dictate again.";
            default:
                return "The microphone couldn't be started. You can still type the note.";
        }
    }

    // Sends the waiting chunks in order, one at a time. On a failure worth retrying the chunks stay here and are sent
    // again later; the recording carries on meanwhile (UC 9: the connection drops).
    async function pump() {
        if (state.sending || state.cancelled || state.ended || !state.session) {
            return;
        }

        state.sending = true;
        try {
            while (state.queue.length > 0 && !state.cancelled) {
                const chunk = state.queue[0];
                let response;
                try {
                    response = await call("POST", `/${state.session.sessionId}/audio?seq=${chunk.seq}`, chunk.blob, chunk.type);
                } catch {
                    pauseAndRetry();
                    return;
                }

                if (state.cancelled) {
                    return;
                }

                if (response.ok) {
                    const received = await json(response);
                    state.queue.shift();
                    resumed();
                    renderTranscript(received.transcript);
                    warnNearLimit(received.audioSeconds);
                    continue;
                }

                const body = await json(response);
                if (response.status === 409 && body.error === "out_of_order" && typeof body.expectedSeq === "number") {
                    // The API already has everything before expectedSeq: send from there.
                    state.queue = state.queue.filter((waiting) => waiting.seq >= body.expectedSeq);
                    continue;
                }

                if (response.status === 409 && body.error === "stopped") {
                    state.queue = [];
                    break;
                }

                if (transient(response.status)) {
                    pauseAndRetry();
                    return;
                }

                if (response.status === 413) {
                    // Over the dictation's limit: what was sent is kept, and the dictation stops here.
                    state.queue = [];
                    say(body.message || "The dictation is over its time limit, so it has been stopped.", true);
                    if (state.recorder?.state === "recording") {
                        state.recorder.stop();
                    }

                    break;
                }

                fail(body.message || `Dictation failed (${response.status}).`);
                return;
            }
        } finally {
            state.sending = false;
        }

        if (state.stopRequested && state.queue.length === 0) {
            finish();
        }
    }

    function pauseAndRetry() {
        if (!state.paused) {
            state.paused = true;
            say("The connection to the dictation service dropped. Still recording on this device; the transcript is paused and catches up when the connection returns.", true);
        }

        clearTimeout(state.retryTimer);
        state.retryTimer = setTimeout(pump, state.retryMs);
        state.retryMs = Math.min(state.retryMs * 2, mostRetryMs);
    }

    function resumed() {
        state.retryMs = firstRetryMs;
        if (state.paused) {
            state.paused = false;
            say(state.stopRequested ? "Connection back. Sending the rest of the recording." : "Connection back. Recording; the transcript has caught up.");
        }
    }

    function warnNearLimit(audioSeconds) {
        const left = Math.floor((state.session?.maxSeconds ?? 0) - (audioSeconds ?? 0));
        if (state.recorder?.state === "recording" && !state.paused && left <= warnSeconds) {
            say(left > 0 ? `About ${left} seconds of dictation left. Stop soon, then dictate the rest separately.` : "The dictation has reached its time limit.");
        }
    }

    function stop() {
        stopButton.disabled = true;
        if (state.recorder?.state === "recording") {
            say("Stopping…");
            state.recorder.stop();
        }
    }

    // UC 9 step 3 and UC 10: stop, then ask for the result until the fields are sorted, or can't be.
    async function finish() {
        if (state.stopping || state.cancelled) {
            return;
        }

        state.stopping = true;
        say("Finishing the transcript and proposing the fields. This can take up to a minute.");
        let result = await ask("stop");
        while (result && result.sorting === "pending" && !state.cancelled) {
            result = await ask("result");
        }

        if (result && !state.cancelled) {
            state.ended = true;
            buttons(false);
            cancelButton.disabled = false;
            cancelButton.textContent = "Discard";
            say(result.sorting === "done"
                ? "Done. Review each proposed field; nothing is in the note until you approve it."
                : "Done, but the fields couldn't be sorted. The transcript is kept as plain text.");
        }
    }

    // One stop or result call, sent again with backoff while the connection is down. Null when it can't go on.
    async function ask(route) {
        for (;;) {
            if (state.cancelled) {
                return null;
            }

            let response;
            try {
                response = await call("POST", `/${state.session.sessionId}/${route}`);
            } catch {
                response = { ok: false, status: 0 };
            }

            if (response.ok) {
                state.retryMs = firstRetryMs;
                const result = await json(response);
                renderResult(result);
                return result;
            }

            if (!transient(response.status)) {
                fail(await refusal(response));
                return null;
            }

            say("Waiting for the dictation service to answer…", true);
            await new Promise((resolve) => setTimeout(resolve, state.retryMs));
            state.retryMs = Math.min(state.retryMs * 2, mostRetryMs);
        }
    }

    function fail(message) {
        endRecording();
        state.ended = true;
        buttons(false);
        cancelButton.disabled = state.session === null;
        say(message, true);
    }

    // UC 9 and UC 10, any step: the clinician cancels. The session is deleted, discarding its audio, transcript and
    // proposed fields; a session whose delete doesn't arrive is discarded by the service after ten idle minutes.
    async function cancel() {
        const session = state.session;
        state.cancelled = true;
        clearTimeout(state.retryTimer);
        endRecording();
        state.queue = [];
        resetScreen();
        buttons(false);
        cancelButton.disabled = true;
        cancelButton.textContent = "Cancel";
        if (session) {
            try {
                await call("DELETE", `/${session.sessionId}`);
            } catch {
                // Nothing to do: the service discards it on its own.
            }
        }

        state = fresh();
        say("Dictation discarded. Nothing was kept.");
    }

    function endRecording() {
        if (state.recorder && state.recorder.state !== "inactive") {
            state.recorder.stop();
        }

        stopTracks();
    }

    function stopTracks() {
        state.stream?.getTracks().forEach((track) => track.stop());
    }

    function resetScreen() {
        state.transcript = null;
        transcriptArea.replaceChildren(element("p", "Your words appear here as you speak.", "placeholder"));
        unclearNotice.hidden = true;
        sortMessage.textContent = "The fields are proposed once you stop.";
        for (const name of fieldNames) {
            const field = root.querySelector(`[data-dictation-field="${name}"]`);
            field.querySelector("[data-field-status]").textContent = "Not dictated yet";
            field.querySelector("[data-field-value]").replaceChildren();
            field.classList.remove("pending", "empty");
        }

        notCarried.hidden = true;
        notCarriedList.replaceChildren();
    }

    function element(tag, text, className) {
        const node = document.createElement(tag);
        if (text !== undefined) {
            node.textContent = text;
        }

        if (className) {
            node.className = className;
        }

        return node;
    }

    // The transcript, its segments marked: unclear passages (with why), words not final yet, and the ranges to
    // highlight (a field's sources, FR-08). Offsets are UTF-16, as JavaScript strings index.
    function renderTranscript(transcript, highlights = []) {
        if (!transcript) {
            return;
        }

        state.transcript = transcript;
        const text = transcript.text ?? "";
        if (text.length === 0) {
            transcriptArea.replaceChildren(element("p", "Listening…", "placeholder"));
            return;
        }

        const segments = transcript.segments ?? [];
        const points = new Set([0, text.length]);
        for (const range of [...segments, ...highlights]) {
            points.add(Math.max(0, Math.min(text.length, range.start)));
            points.add(Math.max(0, Math.min(text.length, range.end)));
        }

        const sorted = [...points].sort((a, b) => a - b);
        const paragraph = element("p");
        for (let i = 0; i + 1 < sorted.length; i++) {
            const [from, to] = [sorted[i], sorted[i + 1]];
            const segment = segments.find((s) => s.start <= from && to <= s.end);
            const highlighted = highlights.some((h) => h.start <= from && to <= h.end);
            let node = document.createTextNode(text.slice(from, to));
            if (segment?.unclear) {
                const mark = element("mark", undefined, "unclear");
                mark.title = `Say this again: ${unclearWords[segment.unclear] ?? "it may be wrong"}.`;
                mark.append(node);
                node = mark;
            } else if (segment && !segment.final) {
                const span = element("span", undefined, "tentative");
                span.append(node);
                node = span;
            }

            if (highlighted) {
                const mark = element("mark", undefined, "source-highlight");
                mark.append(node);
                node = mark;
            }

            paragraph.append(node);
        }

        transcriptArea.replaceChildren(paragraph);
        unclearNotice.hidden = !segments.some((s) => s.unclear);
    }

    function renderResult(result) {
        renderTranscript(result.transcript);
        if (result.sorting === "pending") {
            sortMessage.textContent = "Proposing the fields…";
        } else if (result.sorting === "unavailable") {
            sortMessage.textContent = result.message || "The fields couldn't be sorted, so the transcript is kept as plain text.";
        } else {
            sortMessage.textContent = "Each proposed field is awaiting your review. Select one to see the words it came from.";
        }

        for (const name of fieldNames) {
            renderField(name, result.fields?.[name], result.sorting);
        }

        const items = result.notCarriedOver ?? [];
        notCarried.hidden = items.length === 0;
        notCarriedList.replaceChildren(...items.map((item) => {
            const text = (result.transcript?.text ?? "").slice(item.start, item.end);
            const li = element("li");
            li.append(element("strong", `${instructionWords[item.reason] ?? "Instruction"}: `), document.createTextNode(`“${text}”`));
            highlightOnFocus(li, [item]);
            return li;
        }));
    }

    function renderField(name, field, sorting) {
        const container = root.querySelector(`[data-dictation-field="${name}"]`);
        const status = container.querySelector("[data-field-status]");
        const value = container.querySelector("[data-field-value]");
        container.classList.toggle("pending", field?.status === "pending");
        container.classList.toggle("empty", field?.status === "empty");
        value.replaceChildren();
        if (field?.status === "pending") {
            status.textContent = "Awaiting review";
            const text = element("p", field.value, "field-value");
            highlightOnFocus(text, field.sources ?? []);
            value.append(text);
            for (const warning of field.warnings ?? []) {
                const note = element("p", warningWords[warning.code] ?? "Check this field against the transcript.", "field-warning");
                highlightOnFocus(note, [warning]);
                value.append(note);
            }
        } else if (field?.status === "empty") {
            status.textContent = "Left empty";
            const notDictated = (field.reasons ?? []).includes("not_dictated");
            value.append(element("p", notDictated
                ? "Nothing was dictated for this field. Fill it in during review if it's needed."
                : "No suggestion: what was proposed didn't pass the automatic checks. Fill it in during review."));
        } else {
            status.textContent = sorting === "unavailable" ? "Not sorted" : sorting === "pending" ? "Proposing…" : "Not dictated yet";
        }
    }

    // Hovering or focusing the element highlights its ranges in the transcript; the keyboard reaches it with Tab.
    function highlightOnFocus(node, ranges) {
        node.tabIndex = 0;
        const on = () => renderTranscript(state.transcript, ranges);
        const off = () => renderTranscript(state.transcript);
        node.addEventListener("mouseenter", on);
        node.addEventListener("focus", on);
        node.addEventListener("mouseleave", off);
        node.addEventListener("blur", off);
    }

    startButton.addEventListener("click", start);
    stopButton.addEventListener("click", stop);
    cancelButton.addEventListener("click", cancel);

    // Keyboard first (NFR-12): Alt+Shift+D starts dictation, and stops it while recording. Not Alt+D, which Chrome,
    // Edge and Firefox use on Windows to jump to the address bar: a page can take it over, but then the clinician loses
    // it, and Firefox can be set to stop pages overriding its shortcuts. No browser assigns Alt+Shift+D. By key
    // position, since Option changes the letter typed on a Mac.
    document.addEventListener("keydown", (event) => {
        if (event.altKey && event.shiftKey && !event.ctrlKey && !event.metaKey && event.code === "KeyD") {
            event.preventDefault();
            if (!stopButton.disabled) {
                stop();
            } else if (!startButton.disabled) {
                start();
            }
        }
    });

    // Leaving the page mid-dictation discards it, as Cancel would.
    window.addEventListener("pagehide", () => {
        if (state.session && !state.cancelled) {
            endRecording();
            fetch(`${api}/${state.session.sessionId}`, {
                method: "DELETE",
                headers: { RequestVerificationToken: token },
                credentials: "same-origin",
                keepalive: true,
            }).catch(() => { });
        }
    });
})();
