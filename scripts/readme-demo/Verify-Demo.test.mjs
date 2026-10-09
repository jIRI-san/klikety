import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { animationFrames, crc32, findBrowser, pngChunks, verifyAnimation, verifyCaptures, verifyLinks } from './Verify-Demo.mjs';

const animation = readFileSync(fileURLToPath(new URL('../../docs/screenshots/navigation-demo.png', import.meta.url)));

test('README, user guides and capture documentation have valid local links and anchors', () => {
    const result = verifyLinks();
    assert.ok(result.documents >= 9);
    assert.ok(result.localLinks > 0);
});

test('Published APNG has all eleven true-color mode and navigation frames', () => {
    const result = verifyAnimation(animation);
    assert.equal(result.frames, 11);
    assert.equal(result.frameDelayMs, 1400);
    assert.equal(result.width, 960);
    assert.equal(result.crcValid, true);
});

test('Animation includes every mode, nested hints and all six refinement states', () => {
    assert.deepEqual([...new Set(animationFrames.map(frame => frame.mode))],
        ['UniformGrid', 'Crosshair', 'LogCrosshair', 'LogGrid', 'ElementHints']);
    assert.ok(animationFrames.some(frame => frame.source === 'element-hints-children.png'));
    assert.deepEqual(animationFrames.slice(0, 6).map(frame => frame.source),
        ['frame-01.png', 'crosshair.png', 'log-crosshair.png', 'log-grid.png',
            'element-hints.png', 'element-hints-children.png']);
    assert.deepEqual(animationFrames.filter(frame => frame.source.startsWith('frame-')).map(frame => frame.source),
        Array.from({ length: 6 }, (_, index) => `frame-0${index + 1}.png`));
    assert.ok(animationFrames.every(frame => frame.caption.startsWith(frame.mode)));
});

test('Encoded frames match rendered references and reject reordered content', () => {
    function chunk(type, body) {
        const data = Buffer.concat([Buffer.from(type), body]);
        const length = Buffer.alloc(4), crc = Buffer.alloc(4);
        length.writeUInt32BE(body.length);
        crc.writeUInt32BE(crc32(data));
        return Buffer.concat([length, data, crc]);
    }
    const chunks = pngChunks(animation), frames = [];
    for (const { type, body } of chunks) {
        if (type === 'fcTL') frames.push([]);
        else if (type === 'IDAT') frames.at(-1).push(body);
        else if (type === 'fdAT') frames.at(-1).push(body.subarray(4));
    }
    const rendered = frames.map(data => Buffer.concat([
        animation.subarray(0, 8), chunk('IHDR', chunks[0].body),
        chunk('IDAT', Buffer.concat(data)), chunk('IEND', Buffer.alloc(0))
    ]));
    assert.equal(verifyAnimation(animation, rendered).frames, 11);
    assert.throws(() => verifyAnimation(animation, [rendered[1], rendered[0], ...rendered.slice(2)]),
        /Animation frame 1 differs from its planned render/);
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
    assert.throws(() => verifyAnimation(incomplete), /Expected all eleven mode and navigation frames/);
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
