import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { execFileSync, spawn } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { mkdir, readFile, rm, stat, writeFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { basename, dirname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { inflateSync } from 'node:zlib';

export const imageNames = [
    'uniform-grid.png', 'uniform-grid-zoom.png', 'crosshair.png', 'log-crosshair.png',
    'log-grid.png', 'element-hints.png', 'element-hints-children.png', 'navigation-demo.png'
];
const repository = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

export function crc32(bytes) {
    let crc = 0xffffffff;
    for (const byte of bytes) {
        crc ^= byte;
        for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ (crc & 1 ? 0xedb88320 : 0);
    }
    return (~crc) >>> 0;
}

export function pngChunks(png) {
    assert.equal(png.subarray(0, 8).toString('hex'), '89504e470d0a1a0a', 'Invalid PNG signature');
    const chunks = [];
    let offset = 8;
    while (offset < png.length) {
        assert.ok(offset + 12 <= png.length, 'Truncated PNG chunk');
        const length = png.readUInt32BE(offset);
        assert.ok(offset + length + 12 <= png.length, 'Truncated PNG data');
        const type = png.toString('ascii', offset + 4, offset + 8);
        const body = png.subarray(offset + 8, offset + 8 + length);
        assert.equal(crc32(png.subarray(offset + 4, offset + 8 + length)),
            png.readUInt32BE(offset + 8 + length), `Invalid ${type} CRC`);
        chunks.push({ type, body });
        offset += length + 12;
        if (type === 'IEND') break;
    }
    assert.equal(offset, png.length, 'Trailing PNG data');
    assert.equal(chunks[0]?.type, 'IHDR');
    assert.equal(chunks[0].body.length, 13);
    assert.equal(chunks.at(-1)?.type, 'IEND');
    assert.equal(chunks.at(-1).body.length, 0);
    return chunks;
}

export function verifyAnimation(png) {
    const chunks = pngChunks(png);
    const header = chunks[0].body;
    const width = header.readUInt32BE(0), height = header.readUInt32BE(4);
    assert.equal(width, 960, 'Animation must be 960 pixels wide');
    assert.ok(height > 0 && height <= 8192, 'Unsupported animation height');
    assert.deepEqual([...header.subarray(8)], [8, 6, 0, 0, 0], 'Expected noninterlaced RGBA PNG');
    const animation = chunks.filter(chunk => chunk.type === 'acTL');
    assert.equal(animation.length, 1);
    assert.equal(animation[0].body.length, 8);
    assert.equal(animation[0].body.readUInt32BE(0), 6, 'Expected all six navigation frames');
    assert.equal(animation[0].body.readUInt32BE(4), 0, 'Animation must loop');
    const frames = [];
    let sequence = 0;
    for (const { type, body } of chunks) {
        if (type === 'fcTL') {
            assert.equal(body.length, 26);
            assert.equal(body.readUInt32BE(0), sequence++, 'Invalid APNG sequence');
            assert.equal(body.readUInt32BE(4), width);
            assert.equal(body.readUInt32BE(8), height);
            assert.equal(body.readUInt32BE(12), 0);
            assert.equal(body.readUInt32BE(16), 0);
            assert.equal(body.readUInt16BE(20) / body.readUInt16BE(22), 1.4);
            assert.equal(body[24], 0);
            assert.equal(body[25], 0);
            frames.push([]);
        } else if (type === 'IDAT') {
            assert.equal(frames.length, 1, 'IDAT must belong to the default frame');
            frames[0].push(body);
        } else if (type === 'fdAT') {
            assert.ok(frames.length > 1 && body.length > 4);
            assert.equal(body.readUInt32BE(0), sequence++, 'Invalid APNG sequence');
            frames.at(-1).push(body.subarray(4));
        }
    }
    assert.equal(frames.length, 6);
    const rowSize = width * 4 + 1;
    const hashes = frames.map(frame => {
        const raw = inflateSync(Buffer.concat(frame), { maxOutputLength: height * rowSize });
        assert.equal(raw.length, height * rowSize, 'Invalid decompressed frame size');
        for (let row = 0; row < height; row++) assert.ok(raw[row * rowSize] <= 4, 'Invalid PNG filter');
        return sha256(raw);
    });
    assert.ok(new Set(hashes).size >= 5, 'Navigation frames did not change');
    return { width, height, frames: 6, frameDelayMs: 1400, loops: 0, crcValid: true };
}

export function findBrowser(explicit) {
    assert.ok(Number(process.versions.node.split('.')[0]) >= 22 && typeof WebSocket === 'function',
        'Node.js 22 or newer is required');
    const paths = explicit ? [resolve(explicit)] : [
        process.env['ProgramFiles(x86)'], process.env.ProgramFiles, process.env.LOCALAPPDATA
    ].filter(Boolean).flatMap(base => [
        join(base, 'Microsoft', 'Edge', 'Application', 'msedge.exe'),
        join(base, 'Google', 'Chrome', 'Application', 'chrome.exe')
    ]);
    const browser = paths.find(path => existsSync(path));
    assert.ok(browser, 'Installed Edge/Chrome not found; provide -BrowserPath. No browser is installed automatically.');
    assert.match(basename(browser), /^(msedge|chrome)\.exe$/i);
    return browser;
}

export function verifyCaptures(directory) {
    assert.ok(!existsSync(join(directory, 'error.txt')), 'Guest capture recorded an error');
    assert.match(readFileSync(join(directory, 'done.txt'), 'utf8'), /^Complete: guest-only production navigation captures/);
    const fixture = JSON.parse(readFileSync(join(directory, 'fixture.json'), 'utf8'));
    const hierarchy = JSON.parse(readFileSync(join(directory, 'hierarchy.json'), 'utf8'));
    assert.ok(fixture.Dpi >= 96 && fixture.Dpi <= 768, 'Invalid fixture DPI');
    assert.equal(hierarchy.Outcome, 0, 'Demo discovery must be complete, not partial');
    assert.ok(hierarchy.Targets > 0 && hierarchy.Entries > 0 && hierarchy.Entries <= 100);
    assert.equal(hierarchy.Children, 3, 'Demo combo-box picker must preserve three controls');
    assert.match(hierarchy.GroupRole, /^Combo box/);
    const images = imageNames.map(name => {
        const bytes = readFileSync(join(directory, name));
        const header = pngChunks(bytes)[0].body;
        return { name, width: header.readUInt32BE(0), height: header.readUInt32BE(4), sha256: sha256(bytes) };
    });
    const native = images[0];
    assert.ok(native.width >= 960 && native.height >= 480, 'Guest screen is too small for this demo');
    for (const image of images.slice(0, -1)) {
        assert.equal(image.width, native.width, 'Native still dimensions differ');
        assert.equal(image.height, native.height, 'Native still dimensions differ');
    }
    for (let index = 1; index <= 6; index++) {
        const header = pngChunks(readFileSync(join(directory, `frame-0${index}.png`)))[0].body;
        assert.equal(header.readUInt32BE(0), native.width);
        assert.equal(header.readUInt32BE(4), native.height);
    }
    const animation = verifyAnimation(readFileSync(join(directory, 'navigation-demo.png')));
    return {
        fixtureDpi: fixture.Dpi, nativeSize: { width: native.width, height: native.height },
        hints: { targets: hierarchy.Targets, entries: hierarchy.Entries, children: hierarchy.Children, role: hierarchy.GroupRole },
        images, animation
    };
}

export function verifyLinks(root = repository) {
    const changed = execFileSync('git', ['diff', '--name-only', '--', '*.md'], { cwd: root, encoding: 'utf8' });
    const added = execFileSync('git', ['ls-files', '--others', '--exclude-standard', '--', '*.md'], { cwd: root, encoding: 'utf8' });
    const files = new Set(['README.md', 'docs/design-notes/readme-demo.design.md',
        '.github/skills/capture-demo/SKILL.md', ...(changed + '\n' + added).split(/\r?\n/).filter(Boolean)]);
    let count = 0;
    function anchors(file) {
        const text = readFileSync(file, 'utf8'), counts = new Map(), result = new Set();
        for (const match of text.matchAll(/^ {0,3}#{1,6} +(.+?) *#*$/gm)) {
            const name = match[1].replace(/<[^>]*>/g, '').replace(/[`*_~]/g, '').toLowerCase()
                .replace(/[^\p{L}\p{N}\s_-]/gu, '').replace(/ /g, '-');
            const count = counts.get(name) ?? 0;
            result.add(count ? `${name}-${count}` : name);
            counts.set(name, count + 1);
        }
        for (const match of text.matchAll(/\b(?:id|name)=["']([^"']+)["']/g)) result.add(match[1]);
        return result;
    }
    for (const name of files) {
        const source = resolve(root, name.replaceAll('/', '\\'));
        for (const match of readFileSync(source, 'utf8').matchAll(/!?\[[^\]]*\]\(([^\s)]+)(?:\s+"[^"]*")?\)/g)) {
            const url = match[1];
            if (/^(?:[a-z][a-z\d+.-]*:|\/\/)/i.test(url)) continue;
            const [relative, anchor] = url.split('#');
            const target = relative ? resolve(dirname(source), decodeURIComponent(relative).replaceAll('/', '\\')) : source;
            assert.ok(target.startsWith(root + sep) && existsSync(target), `Missing local link: ${name}: ${url}`);
            if (anchor && target.endsWith('.md')) {
                assert.ok(anchors(target).has(decodeURIComponent(anchor)), `Missing local anchor: ${name}: ${url}`);
            }
            count++;
        }
    }
    return { documents: files.size, localLinks: count };
}

async function verifyBrowser(png, animation, output, executable) {
    const server = createServer((request, response) => {
        if (request.url !== '/') { response.writeHead(404); response.end(); return; }
        response.setHeader('Content-Type', 'text/html');
        response.end('<!doctype html><title>Owned APNG verification</title>');
    });
    await new Promise((resolve, reject) => {
        server.once('error', reject);
        server.listen(0, '127.0.0.1', resolve);
    });
    const profile = join(output, 'browser-profile');
    const browser = spawn(executable, [
        '--headless=new', '--disable-background-networking', '--disable-extensions', '--no-first-run',
        '--no-default-browser-check', '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank'
    ], { stdio: ['ignore', 'ignore', 'pipe'] });
    let launchError;
    browser.once('error', error => { launchError = error; });
    const errors = [];
    browser.stderr.on('data', data => errors.push(data));
    let socket, id = 0;
    const pending = new Map();
    function request(method, params = {}, sessionId) {
        return new Promise((resolve, reject) => {
            const call = { id: ++id, method, params };
            if (sessionId) call.sessionId = sessionId;
            const timeout = setTimeout(() => {
                pending.delete(call.id); reject(new Error(`Browser timeout: ${method}`));
            }, 15000);
            pending.set(call.id, {
                method,
                resolve: result => { clearTimeout(timeout); resolve(result); },
                reject: error => { clearTimeout(timeout); reject(error); }
            });
            socket.send(JSON.stringify(call));
        });
    }
    try {
        let ports;
        for (let attempt = 0; attempt < 150; attempt++) {
            if (launchError) throw launchError;
            assert.equal(browser.exitCode, null, 'Verification browser exited during startup');
            try { ports = (await readFile(join(profile, 'DevToolsActivePort'), 'utf8')).trim().split(/\r?\n/); break; }
            catch (error) { if (error.code !== 'ENOENT') throw error; await sleep(100); }
        }
        assert.ok(ports, 'Owned browser did not become ready');
        socket = new WebSocket(`ws://127.0.0.1:${ports[0]}${ports[1]}`);
        await new Promise((resolve, reject) => {
            const timeout = setTimeout(() => reject(new Error('Browser websocket startup timed out')), 15000);
            socket.addEventListener('open', () => { clearTimeout(timeout); resolve(); }, { once: true });
            socket.addEventListener('error', () => { clearTimeout(timeout); reject(new Error('Browser websocket failed')); }, { once: true });
        });
        socket.addEventListener('message', event => {
            const message = JSON.parse(event.data), call = pending.get(message.id);
            if (!call) return;
            pending.delete(message.id);
            if (message.error) call.reject(new Error(JSON.stringify(message.error)));
            else call.resolve(message.result);
        });
        socket.addEventListener('close', () => {
            for (const call of pending.values()) {
                if (call.method === 'Browser.close') call.resolve({});
                else call.reject(new Error('Owned browser disconnected'));
            }
            pending.clear();
        });
        const version = await request('Browser.getVersion');
        const { targetId } = await request('Target.createTarget', { url: `http://127.0.0.1:${server.address().port}/` });
        const { sessionId } = await request('Target.attachToTarget', { targetId, flatten: true });
        await request('Page.enable', {}, sessionId);
        await request('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/` }, sessionId);
        await request('Emulation.setDeviceMetricsOverride', {
            width: animation.width + 40, height: animation.height + 40, deviceScaleFactor: 1, mobile: false
        }, sessionId);
        const evaluated = await request('Runtime.evaluate', {
            expression: `(async () => {
                const encoded = ${JSON.stringify(png.toString('base64'))};
                const decoder = new ImageDecoder({data:Uint8Array.from(atob(encoded),x=>x.charCodeAt(0)),type:'image/png'});
                await decoder.tracks.ready;
                const info={animated:decoder.tracks.selectedTrack.animated,frames:decoder.tracks.selectedTrack.frameCount,decoded:[]};
                for(let i=0;i<info.frames;i++){
                    const {image}=await decoder.decode({frameIndex:i});
                    info.decoded.push({width:image.displayWidth,height:image.displayHeight,duration:image.duration});
                    image.close();
                }
                decoder.close();
                document.body.style.margin='0';
                const image=document.createElement('img');image.src='data:image/png;base64,'+encoded;
                document.body.replaceChildren(image);await image.decode();
                return info;
            })()`,
            awaitPromise: true, returnByValue: true
        }, sessionId);
        assert.ok(!evaluated.exceptionDetails, JSON.stringify(evaluated.exceptionDetails));
        const decoder = evaluated.result.value;
        assert.equal(decoder.animated, true);
        assert.equal(decoder.frames, 6);
        for (const frame of decoder.decoded) {
            assert.equal(frame.width, animation.width);
            assert.equal(frame.height, animation.height);
            assert.equal(frame.duration, 1400000);
        }
        const first = await request('Page.captureScreenshot', { format: 'png' }, sessionId);
        await sleep(1600);
        const second = await request('Page.captureScreenshot', { format: 'png' }, sessionId);
        assert.notEqual(first.data, second.data, 'Browser did not render an animated frame change');
        await writeFile(join(output, 'browser-frame-1.png'), Buffer.from(first.data, 'base64'));
        await writeFile(join(output, 'browser-frame-2.png'), Buffer.from(second.data, 'base64'));
        return { version: version.product, decodedFrames: decoder.frames, animationObserved: true };
    } finally {
        try {
            if (socket?.readyState === WebSocket.OPEN) await request('Browser.close');
        } finally {
            socket?.close();
            await new Promise(resolve => server.close(resolve));
            if (!launchError && browser.exitCode === null) {
                await Promise.race([new Promise(resolve => browser.once('exit', resolve)), sleep(5000)]);
                if (browser.exitCode === null) {
                    execFileSync('powershell.exe', ['-NoProfile', '-Command', `Stop-Process -Id ${browser.pid} -ErrorAction Stop`]);
                    await new Promise(resolve => browser.once('exit', resolve));
                }
            }
            await writeFile(join(output, 'browser-error.log'), Buffer.concat(errors));
            if (existsSync(profile)) await rm(profile, { recursive: true, force: true });
        }
    }
}

async function main(args) {
    if (args[0] === '--preflight') {
        const browser = findBrowser(args[1]);
        console.log(JSON.stringify({ browser, imageNames }));
        return;
    }
    const browser = findBrowser(args[3]);
    assert.equal(args[0], '--capture', 'Usage: Verify-Demo.mjs --capture <captures> <unused-verification-directory> [browser]');
    const capture = resolve(args[1]), output = resolve(args[2]);
    const report = verifyCaptures(capture);
    report.links = verifyLinks();
    await mkdir(output, { recursive: false });
    report.browser = await verifyBrowser(readFileSync(join(capture, 'navigation-demo.png')), report.animation, output, browser);
    report.capturedAtUtc = (await stat(join(capture, 'done.txt'))).mtime.toISOString();
    report.verifiedAtUtc = new Date().toISOString();
    await writeFile(join(output, 'capture-info.json'), JSON.stringify(report, null, 2) + '\n');
    console.log(JSON.stringify(report));
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    main(process.argv.slice(2)).catch(error => { console.error(error); process.exitCode = 1; });
}
