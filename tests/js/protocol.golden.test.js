/**
 * Golden-vector test: confirms protocol.js's encodeInputPacket produces byte-identical
 * output to the C# encoder, using the same vector as InputPacketTests.GoldenVectorBytes.
 * Run with: node tests/js/protocol.golden.test.js
 */
"use strict";

var assert = require("assert");
var path = require("path");
var protocol = require(path.join(__dirname, "..", "..", "src", "PocketInk.Host", "wwwroot", "js", "protocol.js"));

var EXPECTED_HEX =
    "4B5001002A0000000000003F0000803E000200000000000000000000";

function toHex(buffer) {
    return Buffer.from(buffer).toString("hex").toUpperCase();
}

(function goldenVectorBytes_MatchesCSharpEncoder() {
    var packet = {
        sequence: 42,
        phase: protocol.PHASE.MOVE,
        x: 0.5,
        y: 0.25,
        pressure: 512,
        flags: protocol.FLAGS.NONE,
        timestampMs: 0,
    };

    var encoded = protocol.encodeInputPacket(packet);

    assert.strictEqual(encoded.byteLength, protocol.WIRE_SIZE, "buffer must be WIRE_SIZE bytes");
    assert.strictEqual(toHex(encoded), EXPECTED_HEX, "encoded bytes must match C# golden vector");
})();

console.log("protocol.golden.test.js: PASS");
