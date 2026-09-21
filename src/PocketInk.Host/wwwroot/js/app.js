/**
 * PocketInk web client: pairing flow, WebSocket lifecycle (hello/heartbeat),
 * and Pointer Events capture -> binary INPUT_PACKET_V1 frames (see protocol.js).
 * Loaded as a classic <script> after protocol.js, which exposes window.PocketInkProtocol.
 */
(function () {
    "use strict";

    var proto = window.PocketInkProtocol;
    var HEARTBEAT_INTERVAL_MS = 2000;
    var RECONNECT_DELAY_MS = 1500;
    var TRACKPAD_SENSITIVITY = 1.5;
    var TRACKPAD_TAP_MAX_DRIFT_PX = 6;
    var DOUBLE_TAP_MAX_INTERVAL_MS = 300;
    var DOUBLE_TAP_MAX_DRIFT_PX = 24;

    var SUPABASE_JS_URL = "https://cdn.jsdelivr.net/npm/@supabase/supabase-js@2/dist/umd/supabase.js";

    var elements = {};
    var state = {
        ws: null,
        connected: false,
        sequence: 0,
        mode: "tablet", // "tablet" | "mirror" | "trackpad"
        tabletRect: null, // {left, top, width, height} in CSS px, relative to the surface element
        activePointerId: null,
        heartbeatTimer: null,
        reconnectTimer: null,
        peerConnection: null,
        inputChannel: null, // Low-latency RTCDataChannel for MOVE_CONTACT during mirroring (spec Stage N)
        // Remote (Supabase-signaled) mode only, alongside the WebSocket used by Local/LAN mode -
        // see the "Remote mode" section below. bootstrapPeerConnection exists only to carry
        // remoteControlChannel; it is a different RTCPeerConnection from the mirror's own `peerConnection`.
        bootstrapPeerConnection: null,
        remoteControlChannel: null,
        remoteSignalChannel: null,
        // Parsed from the Remote pairing URL (see connectRemote) - null in Local mode, where
        // same-LAN host candidates always work and no TURN relay is ever needed.
        iceServers: null,
    };

    /** Builds an RTCPeerConnection using state.iceServers when Remote mode set one, exactly like
     *  Local mode's plain `new RTCPeerConnection()` when it didn't (state.iceServers stays null
     *  for the entire lifetime of a Local-mode page load, so this is a no-op there). */
    function createPeerConnection() {
        return state.iceServers ? new RTCPeerConnection({ iceServers: state.iceServers }) : new RTCPeerConnection();
    }

    function nextSequence() {
        state.sequence = (state.sequence + 1) >>> 0;
        return state.sequence;
    }

    function showMessage(text) {
        elements.message.textContent = text;
        elements.message.hidden = false;
    }

    function hideMessage() {
        elements.message.hidden = true;
    }

    function setStatus(text) {
        elements.statusText.textContent = text;
    }

    function setLatency(rttMs) {
        elements.latency.textContent = rttMs >= 0 ? rttMs + " ms" : "";
    }

    // ---- Pairing ----

    async function ensurePaired() {
        var url = new URL(window.location.href);
        var token = url.searchParams.get("token");

        if (token) {
            url.searchParams.delete("token");
            var cleanedPath = url.pathname + (url.search ? url.search : "");
            window.history.replaceState({}, "", cleanedPath);

            try {
                var pairResponse = await fetch("/api/pair", {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({ token: token }),
                });
                if (!pairResponse.ok) {
                    showMessage("Pairing failed or the code expired. Please rescan the QR code shown on your PC.");
                    return false;
                }
                return true;
            } catch (err) {
                showMessage("Could not reach the PocketInk host. Check that you're on the same Wi-Fi network.");
                return false;
            }
        }

        try {
            var statusResponse = await fetch("/api/pair/status");
            var status = await statusResponse.json();
            if (!status.paired) {
                showMessage("Not paired. Scan the QR code shown in PocketInk on your PC.");
                return false;
            }
            return true;
        } catch (err) {
            showMessage("Could not reach the PocketInk host. Check that you're on the same Wi-Fi network.");
            return false;
        }
    }

    // ---- Tablet drawing area (matches the target monitor's aspect ratio, spec #108/#109) ----

    function computeMatchedAspectArea(width, height, aspectRatio) {
        if (width <= 0 || height <= 0 || aspectRatio <= 0) {
            return { left: 0, top: 0, width: Math.max(width, 0), height: Math.max(height, 0) };
        }

        var candidateWidth = height * aspectRatio;
        if (candidateWidth <= width) {
            return { left: (width - candidateWidth) / 2, top: 0, width: candidateWidth, height: height };
        }

        var candidateHeight = width / aspectRatio;
        return { left: 0, top: (height - candidateHeight) / 2, width: width, height: candidateHeight };
    }

    async function loadTabletArea() {
        var surfaceRect = elements.surface.getBoundingClientRect();
        try {
            var response = await fetch("/api/tablet-config");
            if (response.ok) {
                var config = await response.json();
                if (config.matchMonitorAspectRatio) {
                    state.tabletRect = computeMatchedAspectArea(surfaceRect.width, surfaceRect.height, config.monitorAspectRatio);
                    return;
                }
            }
        } catch (err) {
            // Fall through to stretch mode - drawing still works, just without aspect matching.
        }
        state.tabletRect = { left: 0, top: 0, width: surfaceRect.width, height: surfaceRect.height };
    }

    /**
     * Rotating the phone changes which of width/height is larger, which changes how much
     * letterboxing the aspect-ratio match needs - landscape is usually much closer to a typical
     * monitor's aspect ratio than portrait, so it should show most or all of the target screen
     * rather than a narrow letterboxed strip. `resize`/`orientationchange` can fire before iOS
     * Safari's viewport has actually finished settling into the new orientation, so this debounces
     * and waits a beat before recomputing rather than trusting the dimensions at event time.
     */
    function setupOrientationHandling() {
        var recalcTimer = null;
        function scheduleRecalc() {
            if (recalcTimer) {
                window.clearTimeout(recalcTimer);
            }
            recalcTimer = window.setTimeout(function () {
                recalcTimer = null;
                loadTabletArea();
            }, 200);
        }

        window.addEventListener("resize", scheduleRecalc);
        window.addEventListener("orientationchange", scheduleRecalc);
    }

    function clamp01(value) {
        return Math.max(0, Math.min(1, value));
    }

    /** Mirrors PocketInk.Core.Coordinates.VideoRectCalculator.ComputeContainRect - the actual rendered
     *  content rect of a video under object-fit: contain, so touches on letterboxed video map correctly. */
    function computeContainRect(videoWidth, videoHeight, elementWidth, elementHeight) {
        if (videoWidth <= 0 || videoHeight <= 0 || elementWidth <= 0 || elementHeight <= 0) {
            return { left: 0, top: 0, width: Math.max(elementWidth, 0), height: Math.max(elementHeight, 0) };
        }

        var videoAspect = videoWidth / videoHeight;
        var elementAspect = elementWidth / elementHeight;

        if (videoAspect > elementAspect) {
            var contentHeight = elementWidth / videoAspect;
            return { left: 0, top: (elementHeight - contentHeight) / 2, width: elementWidth, height: contentHeight };
        }

        var contentWidth = elementHeight * videoAspect;
        return { left: (elementWidth - contentWidth) / 2, top: 0, width: contentWidth, height: elementHeight };
    }

    function areaFromRect(area, localX, localY) {
        if (!area || area.width <= 0 || area.height <= 0) {
            return null;
        }

        var insideArea = localX >= area.left && localX <= area.left + area.width &&
            localY >= area.top && localY <= area.top + area.height;

        return {
            x: clamp01((localX - area.left) / area.width),
            y: clamp01((localY - area.top) / area.height),
            insideArea: insideArea,
        };
    }

    /** Returns normalized [0,1] coordinates within the active drawing/mirror area, clamped, plus whether the raw point fell inside it. */
    function normalizeClientPoint(clientX, clientY) {
        var surfaceRect = elements.surface.getBoundingClientRect();
        var localX = clientX - surfaceRect.left;
        var localY = clientY - surfaceRect.top;

        if (state.mode === "mirror") {
            var video = elements.video;
            if (!video.videoWidth || !video.videoHeight) {
                return null; // Stream metadata not loaded yet.
            }
            var contentRect = computeContainRect(video.videoWidth, video.videoHeight, surfaceRect.width, surfaceRect.height);
            return areaFromRect(contentRect, localX, localY);
        }

        return areaFromRect(state.tabletRect, localX, localY);
    }

    // ---- WebSocket lifecycle ----

    function connect() {
        var scheme = window.location.protocol === "https:" ? "wss" : "ws";
        var ws = new WebSocket(scheme + "://" + window.location.host + "/ws");
        ws.binaryType = "arraybuffer";
        state.ws = ws;

        ws.addEventListener("open", function () {
            sendJson({
                type: proto.CONTROL_TYPE.HELLO,
                protocolVersion: proto.PROTOCOL_VERSION,
                clientVersion: "pocketink-web-1.0",
            });
        });

        ws.addEventListener("message", handleServerMessage);

        ws.addEventListener("close", function () {
            state.connected = false;
            stopHeartbeat();
            setStatus("Disconnected");
            setLatency(-1);
            if (state.peerConnection) {
                // The server-side mirror session died with this connection - no point keeping
                // the local RTCPeerConnection around; drop back to tablet mode.
                state.peerConnection.close();
                state.peerConnection = null;
                setMode("tablet");
            }
            scheduleReconnect();
        });

        ws.addEventListener("error", function () {
            // The "close" event always follows; nothing extra to do here.
        });
    }

    function scheduleReconnect() {
        if (state.reconnectTimer) {
            return;
        }
        state.reconnectTimer = window.setTimeout(function () {
            state.reconnectTimer = null;
            connect();
        }, RECONNECT_DELAY_MS);
    }

    // ---- Remote (cross-network) mode: Supabase-relayed signaling instead of a LAN WebSocket ----
    //
    // The phone isn't reachable on the host's own network, so there is no WebSocket to dial
    // directly. Instead, a bootstrap RTCPeerConnection is negotiated through a short-lived
    // Supabase Realtime Broadcast channel named by the pairing code (mirrors the host's
    // SupabasePairingSession); once the host's "control" RTCDataChannel opens over it, every
    // control-plane message (hello/heartbeat/hotkey/trackpad/mirror signaling - the exact same
    // JSON envelopes as Local mode's `handleServerMessage`/`sendJson`) flows over that data
    // channel instead, and Supabase is never touched again for the rest of the session.

    function loadSupabaseJs() {
        if (window.supabase && typeof window.supabase.createClient === "function") {
            return Promise.resolve();
        }
        return new Promise(function (resolve, reject) {
            var script = document.createElement("script");
            script.src = SUPABASE_JS_URL;
            script.onload = resolve;
            script.onerror = function () { reject(new Error("Failed to load supabase-js.")); };
            document.head.appendChild(script);
        });
    }

    function sendRemoteSignal(channel, payload) {
        channel.send({ type: "broadcast", event: "signal", payload: payload });
    }

    async function handleRemoteSignal(pc, channel, payload) {
        if (payload.kind === "offer") {
            await pc.setRemoteDescription({ type: "offer", sdp: payload.sdp });
            var answer = await pc.createAnswer();
            await pc.setLocalDescription(answer);
            sendRemoteSignal(channel, { kind: "answer", sdp: pc.localDescription.sdp, sdpType: "answer" });
        } else if (payload.kind === "ice-candidate" && payload.candidate) {
            try {
                await pc.addIceCandidate({
                    candidate: payload.candidate,
                    sdpMid: payload.sdpMid,
                    sdpMLineIndex: payload.sdpMLineIndex,
                    usernameFragment: payload.usernameFragment,
                });
            } catch (err) {
                // A candidate arriving after the connection closed is harmless - ignore.
            }
        }
    }

    function attachRemoteControlChannel(dataChannel) {
        state.remoteControlChannel = dataChannel;

        dataChannel.onopen = function () {
            sendJson({
                type: proto.CONTROL_TYPE.HELLO,
                protocolVersion: proto.PROTOCOL_VERSION,
                clientVersion: "pocketink-web-1.0",
            });
        };

        dataChannel.onmessage = handleServerMessage;

        dataChannel.onclose = function () {
            state.connected = false;
            stopHeartbeat();
            setLatency(-1);
            state.remoteControlChannel = null;
            if (state.peerConnection) {
                state.peerConnection.close();
                state.peerConnection = null;
                setMode("tablet");
            }
            // Unlike Local mode's scheduleReconnect(), a Remote pairing code is single-use and
            // already consumed server-side by the time this channel opened - there's nothing left
            // to reconnect to, so the user has to rescan a fresh QR code.
            showMessage("Disconnected. Rescan the remote pairing QR code on your PC to reconnect.");
            setStatus("Disconnected");
        };
    }

    async function connectRemote(pairingCode, supabaseUrl, supabaseAnonKey) {
        if (!supabaseUrl || !supabaseAnonKey) {
            showMessage("This remote pairing link is missing its Supabase configuration.");
            setStatus("Not paired");
            return;
        }

        try {
            await loadSupabaseJs();
        } catch (err) {
            showMessage("Could not load the remote connection library. Check your internet connection.");
            setStatus("Not paired");
            return;
        }

        var supabaseClient = window.supabase.createClient(supabaseUrl, supabaseAnonKey);
        var channel = supabaseClient.channel("pocketink-pair-" + pairingCode, {
            config: { broadcast: { self: false, ack: true } },
        });
        state.remoteSignalChannel = channel;

        var pc = createPeerConnection();
        state.bootstrapPeerConnection = pc;

        pc.onicecandidate = function (event) {
            if (!event.candidate) {
                return;
            }
            sendRemoteSignal(channel, {
                kind: "ice-candidate",
                candidate: event.candidate.candidate,
                sdpMid: event.candidate.sdpMid,
                sdpMLineIndex: event.candidate.sdpMLineIndex,
                usernameFragment: event.candidate.usernameFragment,
            });
        };

        pc.ondatachannel = function (event) {
            if (event.channel.label !== "control") {
                return;
            }
            attachRemoteControlChannel(event.channel);
        };

        // Handled strictly one at a time: an ICE candidate that arrives while the offer is still
        // being applied would otherwise hit addIceCandidate before a remote description exists.
        var signalQueue = Promise.resolve();
        channel.on("broadcast", { event: "signal" }, function (message) {
            signalQueue = signalQueue.then(function () {
                return handleRemoteSignal(pc, channel, message.payload || {});
            }).catch(function () { /* one bad signal must not stall the queue */ });
        });

        // Broadcast isn't stored, so the host can only send its offer once we're actually listening -
        // "ready" is what tells it to.
        channel.subscribe(function (status) {
            if (status === "SUBSCRIBED") {
                sendRemoteSignal(channel, { kind: "ready" });
            } else if (status === "CHANNEL_ERROR" || status === "TIMED_OUT") {
                showMessage("Could not reach the pairing service (" + status + "). Check your connection and rescan.");
                setStatus("Not paired");
            }
        });
    }

    function handleServerMessage(event) {
        if (typeof event.data !== "string") {
            return; // The server never sends binary frames back; ignore defensively.
        }

        var message;
        try {
            message = JSON.parse(event.data);
        } catch (err) {
            return;
        }

        switch (message.type) {
            case proto.CONTROL_TYPE.HELLO_ACK:
                state.connected = true;
                hideMessage();
                setStatus("Connected");
                startHeartbeat();
                break;

            case proto.CONTROL_TYPE.HEARTBEAT_ACK:
                setLatency(Date.now() - message.clientTimeMs);
                break;

            case proto.CONTROL_TYPE.ERROR:
                showMessage("Server error: " + message.message);
                break;

            case proto.CONTROL_TYPE.RTC_OFFER:
                handleRtcOffer(message);
                break;

            case proto.CONTROL_TYPE.RTC_ICE_CANDIDATE:
                handleRtcIceCandidate(message);
                break;

            case proto.CONTROL_TYPE.CURSOR_POSITION:
                updateRemoteCursor(message.x, message.y);
                break;
        }
    }

    /** Positions the larger custom cursor overlay at the real OS cursor's location within the
     *  mirrored video's rendered content rect - the native Windows cursor is too small to see on
     *  a phone screen, so this stands in for it while mirroring. */
    function updateRemoteCursor(normalizedX, normalizedY) {
        if (state.mode !== "mirror") {
            return;
        }

        var video = elements.video;
        if (!video.videoWidth || !video.videoHeight) {
            return;
        }

        var surfaceRect = elements.surface.getBoundingClientRect();
        var contentRect = computeContainRect(video.videoWidth, video.videoHeight, surfaceRect.width, surfaceRect.height);

        elements.remoteCursor.style.left = (surfaceRect.left + contentRect.left + normalizedX * contentRect.width) + "px";
        elements.remoteCursor.style.top = (surfaceRect.top + contentRect.top + normalizedY * contentRect.height) + "px";
        elements.remoteCursor.hidden = false;
    }

    // ---- WebRTC screen mirroring (spec Phase 2) ----

    function startMirror() {
        if (state.peerConnection) {
            return;
        }

        var pc = createPeerConnection();
        state.peerConnection = pc;

        pc.ontrack = function (event) {
            elements.video.srcObject = event.streams[0];
        };

        pc.onicecandidate = function (event) {
            if (!event.candidate) {
                return;
            }
            sendJson({
                type: proto.CONTROL_TYPE.RTC_ICE_CANDIDATE,
                candidate: event.candidate.candidate,
                sdpMid: event.candidate.sdpMid,
                sdpMLineIndex: event.candidate.sdpMLineIndex,
                usernameFragment: event.candidate.usernameFragment,
            });
        };

        pc.ondatachannel = function (event) {
            if (event.channel.label !== "input") {
                return;
            }
            state.inputChannel = event.channel;
            state.inputChannel.onclose = function () { state.inputChannel = null; };
        };

        sendJson({ type: proto.CONTROL_TYPE.START_MIRROR });
    }

    function stopMirror() {
        sendJson({ type: proto.CONTROL_TYPE.STOP_MIRROR });
        if (state.peerConnection) {
            state.peerConnection.close();
            state.peerConnection = null;
        }
        state.inputChannel = null;
        elements.video.srcObject = null;
    }

    async function handleRtcOffer(message) {
        var pc = state.peerConnection;
        if (!pc) {
            return; // We didn't ask for mirroring (or already stopped it) - ignore a stray offer.
        }

        await pc.setRemoteDescription({ type: "offer", sdp: message.sdp });
        var answer = await pc.createAnswer();
        await pc.setLocalDescription(answer);

        sendJson({
            type: proto.CONTROL_TYPE.RTC_ANSWER,
            sdpType: "answer",
            sdp: pc.localDescription.sdp,
        });
    }

    async function handleRtcIceCandidate(message) {
        var pc = state.peerConnection;
        if (!pc || !message.candidate) {
            return;
        }

        try {
            await pc.addIceCandidate({
                candidate: message.candidate,
                sdpMid: message.sdpMid,
                sdpMLineIndex: message.sdpMLineIndex,
                usernameFragment: message.usernameFragment,
            });
        } catch (err) {
            // A candidate arriving after the connection closed is harmless - ignore.
        }
    }

    var MODE_HINT = {
        tablet: "Draw: draw on this screen, ink appears on the PC.",
        mirror: "Mirror: see and touch the PC's actual screen.",
        trackpad: "Trackpad: drag to move the cursor, tap to click, double-tap to right-click.",
    };
    var MODE_BUTTONS; // populated in setupModeButtons - {tablet, mirror, trackpad}

    function setMode(mode) {
        state.mode = mode;
        state.activePointerId = null;
        trackpad.lastX = null;
        trackpad.lastY = null;
        trackpad.lastTapTime = 0;

        elements.video.hidden = mode !== "mirror";
        elements.remoteCursor.hidden = mode !== "mirror";
        elements.modeHint.textContent = MODE_HINT[mode];

        Object.keys(MODE_BUTTONS).forEach(function (key) {
            MODE_BUTTONS[key].classList.toggle("active", key === mode);
        });

        if (mode === "mirror") {
            startMirror();
        } else if (state.peerConnection) {
            stopMirror();
        }
    }

    function setupModeButtons() {
        MODE_BUTTONS = {
            tablet: elements.modeDrawButton,
            mirror: elements.modeMirrorButton,
            trackpad: elements.modeTrackpadButton,
        };
        Object.keys(MODE_BUTTONS).forEach(function (key) {
            MODE_BUTTONS[key].addEventListener("click", function () { setMode(key); });
        });
    }

    function startHeartbeat() {
        stopHeartbeat();
        state.heartbeatTimer = window.setInterval(function () {
            sendJson({ type: proto.CONTROL_TYPE.HEARTBEAT, clientTimeMs: Date.now() });
        }, HEARTBEAT_INTERVAL_MS);
    }

    function stopHeartbeat() {
        if (state.heartbeatTimer) {
            window.clearInterval(state.heartbeatTimer);
            state.heartbeatTimer = null;
        }
    }

    function sendJson(message) {
        var wsOpen = state.ws && state.ws.readyState === WebSocket.OPEN;
        var remoteOpen = state.remoteControlChannel && state.remoteControlChannel.readyState === "open";
        if (!wsOpen && !remoteOpen) {
            return;
        }
        var json = JSON.stringify(message);
        if (wsOpen) {
            state.ws.send(json);
        } else {
            state.remoteControlChannel.send(json);
        }
    }

    function sendPointerPacket(phase, norm, sourceEvent) {
        var wsOpen = state.ws && state.ws.readyState === WebSocket.OPEN;
        var remoteOpen = state.remoteControlChannel && state.remoteControlChannel.readyState === "open";
        var channelOpen = state.inputChannel && state.inputChannel.readyState === "open";
        if (!wsOpen && !remoteOpen && !channelOpen) {
            return;
        }

        var pressureFraction = 0.5;
        if (sourceEvent && typeof sourceEvent.pressure === "number" && sourceEvent.pressure > 0) {
            pressureFraction = sourceEvent.pressure;
        }

        var flags = proto.FLAGS.NONE;
        if (sourceEvent) {
            flags |= (sourceEvent.buttons & 2) ? proto.FLAGS.BARREL_BUTTON : proto.FLAGS.PRIMARY;
        }

        var encoded = proto.encodeInputPacket({
            sequence: nextSequence(),
            phase: phase,
            x: norm.x,
            y: norm.y,
            pressure: Math.round(pressureFraction * 1024),
            flags: flags,
            timestampMs: Date.now(),
        });

        // High-frequency contact moves prefer the unreliable/unordered data channel for lower
        // latency (spec Stage N); Down/Up/Cancel stay on the WebSocket, where reliability matters
        // more than the last few milliseconds. Sequence numbers let the pipeline reorder either way.
        if (phase === proto.PHASE.MOVE_CONTACT && channelOpen) {
            state.inputChannel.send(encoded);
        } else if (wsOpen) {
            state.ws.send(encoded);
        } else if (remoteOpen) {
            state.remoteControlChannel.send(encoded);
        } else if (channelOpen) {
            state.inputChannel.send(encoded);
        }
    }

    // ---- Pointer Events capture (spec #30, #108) ----

    function setupPointerCapture() {
        var surface = elements.surface;
        surface.addEventListener("pointerdown", onPointerDown);
        surface.addEventListener("pointermove", onPointerMove);
        surface.addEventListener("pointerup", onPointerUp);
        surface.addEventListener("pointercancel", onPointerCancel);
    }

    function onPointerDown(event) {
        if (state.activePointerId !== null) {
            return; // Single active contact at a time.
        }

        if (state.mode === "trackpad") {
            onTrackpadPointerDown(event);
            return;
        }

        var norm = normalizeClientPoint(event.clientX, event.clientY);
        if (!norm || !norm.insideArea) {
            return; // Ignore touches starting in the inactive aspect-matching padding.
        }

        state.activePointerId = event.pointerId;
        elements.surface.setPointerCapture(event.pointerId);
        sendPointerPacket(proto.PHASE.DOWN, norm, event);
        event.preventDefault();
    }

    function onPointerMove(event) {
        if (event.pointerId !== state.activePointerId) {
            return;
        }

        if (state.mode === "trackpad") {
            onTrackpadPointerMove(event);
            return;
        }

        var coalesced = (typeof event.getCoalescedEvents === "function") ? event.getCoalescedEvents() : [];
        if (coalesced.length === 0) {
            coalesced = [event];
        }

        for (var i = 0; i < coalesced.length; i++) {
            var norm = normalizeClientPoint(coalesced[i].clientX, coalesced[i].clientY);
            if (norm) {
                sendPointerPacket(proto.PHASE.MOVE_CONTACT, norm, coalesced[i]);
            }
        }
        event.preventDefault();
    }

    function onPointerUp(event) {
        if (event.pointerId !== state.activePointerId) {
            return;
        }

        if (state.mode === "trackpad") {
            onTrackpadPointerUp(event);
            return;
        }

        var norm = normalizeClientPoint(event.clientX, event.clientY) || { x: 0, y: 0 };
        sendPointerPacket(proto.PHASE.UP, norm, event);
        releaseActiveContact(event.pointerId);
    }

    function onPointerCancel(event) {
        if (event.pointerId !== state.activePointerId) {
            return;
        }

        if (state.mode === "trackpad") {
            releaseActiveContact(event.pointerId);
            return;
        }

        sendPointerPacket(proto.PHASE.CANCEL, { x: 0, y: 0 }, event);
        releaseActiveContact(event.pointerId);
    }

    function releaseActiveContact(pointerId) {
        try {
            elements.surface.releasePointerCapture(pointerId);
        } catch (err) {
            // Already released (e.g. surface lost capture on disconnect) - safe to ignore.
        }
        state.activePointerId = null;
    }

    // ---- Trackpad mode (spec: "optionally a trackpad") ----
    //
    // Relative cursor movement, unlike tablet/mirror's absolute InputPacket coordinates - a
    // single-finger drag moves the PC cursor by the same delta, a tap-without-drag is a left
    // click, and a quick second tap in roughly the same spot upgrades to a right-click (rather
    // than a two-finger gesture, which would need tracking a second simultaneous pointer that the
    // rest of this client's "one active contact" model doesn't support). No drag-to-select and no
    // scroll - deliberately out of scope for a first pass at an "optional" third mode.

    var trackpad = { lastX: null, lastY: null, totalDrift: 0, lastTapTime: 0, lastTapX: 0, lastTapY: 0 };

    function onTrackpadPointerDown(event) {
        state.activePointerId = event.pointerId;
        elements.surface.setPointerCapture(event.pointerId);
        trackpad.lastX = event.clientX;
        trackpad.lastY = event.clientY;
        trackpad.totalDrift = 0;
        event.preventDefault();
    }

    function onTrackpadPointerMove(event) {
        if (trackpad.lastX === null) {
            return;
        }

        var dx = (event.clientX - trackpad.lastX) * TRACKPAD_SENSITIVITY;
        var dy = (event.clientY - trackpad.lastY) * TRACKPAD_SENSITIVITY;
        trackpad.totalDrift += Math.abs(event.clientX - trackpad.lastX) + Math.abs(event.clientY - trackpad.lastY);
        trackpad.lastX = event.clientX;
        trackpad.lastY = event.clientY;

        if (dx !== 0 || dy !== 0) {
            sendJson({ type: proto.CONTROL_TYPE.TRACKPAD_MOVE, dx: dx, dy: dy });
        }
        event.preventDefault();
    }

    function onTrackpadPointerUp(event) {
        if (trackpad.totalDrift <= TRACKPAD_TAP_MAX_DRIFT_PX) {
            var now = Date.now();
            var sinceLastTap = now - trackpad.lastTapTime;
            var driftFromLastTap = Math.abs(event.clientX - trackpad.lastTapX) + Math.abs(event.clientY - trackpad.lastTapY);
            var isDoubleTap = sinceLastTap < DOUBLE_TAP_MAX_INTERVAL_MS && driftFromLastTap < DOUBLE_TAP_MAX_DRIFT_PX;

            if (isDoubleTap) {
                sendJson({ type: proto.CONTROL_TYPE.TRACKPAD_CLICK, button: "right" });
                trackpad.lastTapTime = 0; // Consumed - a third quick tap starts a fresh pair, not a triple.
            } else {
                sendJson({ type: proto.CONTROL_TYPE.TRACKPAD_CLICK, button: "left" });
                trackpad.lastTapTime = now;
                trackpad.lastTapX = event.clientX;
                trackpad.lastTapY = event.clientY;
            }
        }
        trackpad.lastX = null;
        trackpad.lastY = null;
        releaseActiveContact(event.pointerId);
    }

    // ---- Safety: never leave a stuck contact or held modifier if the tab is hidden/closed (spec #36, #152) ----

    function cancelActiveContactAndStopSession() {
        if (state.activePointerId !== null) {
            sendPointerPacket(proto.PHASE.CANCEL, { x: 0, y: 0 });
            state.activePointerId = null;
        }
        if (state.peerConnection) {
            stopMirror();
        }
        sendJson({ type: proto.CONTROL_TYPE.SESSION, command: "stop" });
    }

    function setupSafetyHandlers() {
        document.addEventListener("visibilitychange", function () {
            if (document.hidden) {
                cancelActiveContactAndStopSession();
            }
        });
        window.addEventListener("pagehide", cancelActiveContactAndStopSession);
        window.addEventListener("beforeunload", cancelActiveContactAndStopSession);
    }

    // ---- Startup ----

    async function main() {
        elements.surface = document.getElementById("surface");
        elements.statusText = document.getElementById("statusText");
        elements.latency = document.getElementById("latency");
        elements.message = document.getElementById("message");
        elements.video = document.getElementById("mirrorVideo");
        elements.remoteCursor = document.getElementById("remoteCursor");
        elements.modeDrawButton = document.getElementById("modeDrawButton");
        elements.modeMirrorButton = document.getElementById("modeMirrorButton");
        elements.modeTrackpadButton = document.getElementById("modeTrackpadButton");
        elements.modeHint = document.getElementById("modeHint");

        var url = new URL(window.location.href);
        var connectCode = url.searchParams.get("connect");

        if (connectCode) {
            var supabaseUrl = url.searchParams.get("su");
            var supabaseKey = url.searchParams.get("sk");
            var turnUrls = url.searchParams.get("tu");
            var turnUsername = url.searchParams.get("tn");
            var turnCredential = url.searchParams.get("tc");
            url.searchParams.delete("connect");
            url.searchParams.delete("su");
            url.searchParams.delete("sk");
            url.searchParams.delete("tu");
            url.searchParams.delete("tn");
            url.searchParams.delete("tc");
            window.history.replaceState({}, "", url.pathname + (url.search ? url.search : ""));

            // Optional (Phase 4): absent when the host hasn't configured a TURN server yet, in
            // which case createPeerConnection() falls back to plain ICE with no relay, same as
            // Local mode always has.
            if (turnUrls) {
                state.iceServers = turnUrls.split(";").filter(Boolean).map(function (turnUrl) {
                    return { urls: turnUrl, username: turnUsername || "", credential: turnCredential || "" };
                });
            }

            hideMessage();
            await loadTabletArea();
            setupOrientationHandling();
            setupPointerCapture();
            setupSafetyHandlers();
            setupModeButtons();
            setMode("tablet");

            setStatus("Connecting…");
            connectRemote(connectCode, supabaseUrl, supabaseKey);
            return;
        }

        setStatus("Pairing…");
        var paired = await ensurePaired();
        if (!paired) {
            setStatus("Not paired");
            return;
        }

        hideMessage();
        await loadTabletArea();
        setupOrientationHandling();

        setupPointerCapture();
        setupSafetyHandlers();
        setupModeButtons();
        setMode("tablet");

        setStatus("Connecting…");
        connect();
    }

    main();
})();
