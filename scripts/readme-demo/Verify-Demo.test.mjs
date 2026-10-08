import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { crc32, findBrowser, pngChunks, verifyAnimation, verifyCaptures } from './Verify-Demo.mjs';

const animation = readFileSync(fileURLToPath(new URL('../../docs/screenshots/navigation-demo.png', import.meta.url)));

test('Published APNG has all six true-color timed frames', () => {
    const result = verifyAnimation(animation);
    assert.equal(result.frames, 6);
    assert.equal(result.frameDelayMs, 1400);
    assert.equal(result.width, 960);
    assert.equal(result.crcValid, true);
});

test('CRC validation rejects corrupt frames', () => {
    const corrupt = Buffer.from(animation);
    corrupt[corrupt.length - 1] ^= 1;
    assert.throws(() => verifyAnimation(corrupt), /Invalid IEND CRC/);
});

test('A CRC-valid but incomplete animation is rejected', () => {
    const incomplete = Buffer.from(animation);
    let offset = 8;
    while (incomplete.toString('ascii', offset + 4, offset + 8) !== 'acTL') {
        offset += incomplete.readUInt32BE(offset) + 12;
    }
    incomplete.writeUInt32BE(5, offset + 8);
    const length = incomplete.readUInt32BE(offset);
    incomplete.writeUInt32BE(crc32(incomplete.subarray(offset + 4, offset + 8 + length)), offset + 8 + length);
    assert.throws(() => verifyAnimation(incomplete), /Expected all six navigation frames/);
});

test('Truncated PNGs fail instead of becoming static fallbacks', () => {
    assert.throws(() => pngChunks(animation.subarray(0, animation.length - 1)), /Truncated/);
});

test('Browser preflight refuses an unavailable explicit browser', () => {
    assert.throws(() => findBrowser('C:\\missing-demo-browser\\msedge.exe'), /Installed Edge\/Chrome not found/);
});

test('A gallery without guest completion and hierarchy evidence is rejected', () => {
    assert.throws(() => verifyCaptures(fileURLToPath(new URL('../../docs/screenshots', import.meta.url))), /ENOENT/);
});
