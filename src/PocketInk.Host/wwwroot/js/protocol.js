/**
 * INPUT_PACKET_V1 binary encoder - must byte-for-byte match PocketInk.Core.Protocol.InputPacket
 * on the C# side (see PROTOCOL.md and InputPacketTests.GoldenVectorBytes). Loaded directly as a
 * <script> in the browser and also `require()`-able from Node for the golden-vector test in
 * tests/js/protocol.golden.test.js.
 */
(function (root) {
    "use strict";

    var MAGIC = 0x504b;
    var VERSION = 1; // INPUT_PACKET_V1 wire format version (byte 2 of every binary frame).
    var WIRE_SIZE = 28;

    // Handshake/control-plane protocol version, matches PocketInk.Core.Protocol.ProtocolConstants.
    // CurrentProtocolVersion. Distinct from the binary packet's VERSION byte above, even though both
    // happen to be 1 today - one versions the JSON handshake, the other the InputPacket wire layout.
    var PROTOCOL_VERSION = 1;

    var PHASE = Object.freeze({
        MOVE: 0,
        DOWN: 1,
        MOVE_CONTACT: 2,
        UP: 3,
        CANCEL: 4,
    });

    var FLAGS = Object.freeze({
        NONE: 0,
        PRIMARY: 1 << 0,
        BARREL_BUTTON: 1 << 1,
        ERASER: 1 << 2,
    });

    // Mirrors PocketInk.Core.Protocol.ControlMessageType.
    var CONTROL_TYPE = Object.freeze({
        HELLO: "hello",
        HELLO_ACK: "hello-ack",
        HEARTBEAT: "heartbeat",
        HEARTBEAT_ACK: "heartbeat-ack",
        HOTKEY: "hotkey",
        SESSION: "session",
        STATUS: "status",
        ERROR: "error",
        START_MIRROR: "start-mirror",
        STOP_MIRROR: "stop-mirror",
        RTC_OFFER: "rtc-offer",
        RTC_ANSWER: "rtc-answer",
        RTC_ICE_CANDIDATE: "rtc-ice-candidate",
        TRACKPAD_MOVE: "trackpad-move",
        TRACKPAD_CLICK: "trackpad-click",
        CURSOR_POSITION: "cursor-position",
    });

    /**
     * @param {{sequence:number, phase:number, x:number, y:number, pressure:number, flags?:number, timestampMs:number}} packet
     * @returns {ArrayBuffer} exactly 28 bytes, little-endian, matching INPUT_PACKET_V1.
     */
    function encodeInputPacket(packet) {
        var buffer = new ArrayBuffer(WIRE_SIZE);
        var view = new DataView(buffer);

        view.setUint16(0, MAGIC, true);
        view.setUint8(2, VERSION);
        view.setUint8(3, packet.phase);
        view.setUint32(4, packet.sequence >>> 0, true);
        view.setFloat32(8, packet.x, true);
        view.setFloat32(12, packet.y, true);
        view.setUint16(16, packet.pressure, true);
        view.setUint16(18, packet.flags || FLAGS.NONE, true);
        view.setBigUint64(20, BigInt(Math.max(0, Math.trunc(packet.timestampMs))), true);

        return buffer;
    }

    var api = {
        MAGIC: MAGIC,
        VERSION: VERSION,
        WIRE_SIZE: WIRE_SIZE,
        PROTOCOL_VERSION: PROTOCOL_VERSION,
        PHASE: PHASE,
        FLAGS: FLAGS,
        CONTROL_TYPE: CONTROL_TYPE,
        encodeInputPacket: encodeInputPacket,
    };

    if (typeof module !== "undefined" && module.exports) {
        module.exports = api;
    } else {
        root.PocketInkProtocol = api;
    }
})(typeof window !== "undefined" ? window : globalThis);
