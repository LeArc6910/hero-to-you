// The pieces the web playtests share: a run's notes and checks, a browser page with the game loaded, the game's
// playtest remote control (Client/Playtest.cs), and pressing the game's own buttons with real clicks and taps where
// the game says they are drawn (Ui.LastDrawn, the "buttons" command). Docs/Playtest.md.
'use strict';
const fs = require('fs');
const path = require('path');

const DefaultUrl = 'https://learc6910.github.io/hero-to-you/';

function createRun(out) {
  fs.mkdirSync(out, { recursive: true });
  const run = { out, report: [], errors: [], passed: 0, failed: 0 };
  run.note = line => {
    run.report.push(line);
    console.log(line);
  };
  run.check = (ok, what) => {
    run.note(`- ${ok ? '통과' : '**실패**'}: ${what}`);
    if (ok) run.passed++;
    else {
      run.failed++;
      run.errors.push(`check failed: ${what}`);
    }
    return ok;
  };
  run.write = () => {
    run.note(`\n## 에러\n`);
    if (run.errors.length === 0) run.note('- 없음');
    for (const error of [...new Set(run.errors)]) run.note(`- ${error}`);
    run.note(`\n통과 ${run.passed}, 실패 ${run.failed}`);
    fs.writeFileSync(path.join(out, 'report.md'), run.report.join('\n') + '\n');
  };
  return run;
}

const Profiles = {
  pc: { name: 'pc', context: { viewport: { width: 1280, height: 720 } } },
  // The smallest PC window the game promises, with the biggest text setting (Docs/WebDemo.md 작은 화면).
  'pc-small': { name: 'pc-small', textSize: 130, context: { viewport: { width: 800, height: 600 } } },
  phone: { name: 'phone', touch: true, context: { viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true } },
  'phone-wide': { name: 'phone-wide', touch: true, context: { viewport: { width: 844, height: 390 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true } },
};

// One browser page with the game loaded and its console collected.
async function open(browser, run, profile, url) {
  const context = await browser.newContext({ ...profile.context });
  // The end screen's [기록 복사] writes to the clipboard; a headless browser refuses unless allowed.
  await context.grantPermissions(['clipboard-read', 'clipboard-write'], { origin: new URL(url).origin }).catch(() => {});
  const page = await context.newPage();
  const game = { run, profile, context, page, logs: [], touch: !!profile.touch };
  page.on('console', m => {
    const text = m.text();
    if (text.includes('memorysetup')) return;
    game.logs.push(text);
    if (m.type() === 'error' || /Exception|NullReference|IndexOutOfRange|KeyNotFound|ArgumentOutOfRange/.test(text))
      run.errors.push(`${profile.name}: ${text.split('\n')[0]}`);
  });
  page.on('pageerror', e => run.errors.push(`${profile.name}: page error ${e.message}`));
  const started = Date.now();
  await page.goto(url + '?' + Date.now(), { waitUntil: 'load', timeout: 180000 });
  await page.waitForFunction(() => !!window.unityInstance, null, { timeout: 240000 });
  game.loadSeconds = (Date.now() - started) / 1000;
  await page.waitForTimeout(1500);
  Object.assign(game, helpers(game));
  await game.send('probe on');
  await game.wait(300);
  return game;
}

function helpers(game) {
  const { page, run, profile } = game;
  const h = {};
  h.wait = ms => page.waitForTimeout(ms);

  // Sends playtest commands and returns the [playtest] lines they answered with (one per command).
  h.send = async commands => {
    const mark = game.logs.length;
    const expected = commands.split(';').filter(c => c.trim()).length;
    await page.evaluate(text => window.unityInstance.SendMessage('Game', 'Playtest', text), commands);
    const answered = () => {
      const joined = [];
      for (const l of game.logs.slice(mark)) {
        const text = l.replace(/\r?\n$/, '');
        if (text.startsWith('[playtest] ')) joined.push(text.slice(11));
        else if (text.startsWith('[playtest]+') && joined.length > 0) joined[joined.length - 1] += text.slice(11);
      }
      return joined;
    };
    const deadline = Date.now() + 4000;
    while (answered().length < expected && Date.now() < deadline) await page.waitForTimeout(15);
    for (let count = -1; count !== game.logs.length && Date.now() < deadline; ) {
      count = game.logs.length;
      await page.waitForTimeout(40);
    }
    const lines = answered();
    for (const line of lines) if (/^\w+ error/.test(line)) run.errors.push(`${profile.name}: playtest ${line}`);
    return lines;
  };

  // A command answering with JSON after its name.
  h.ask = async command => {
    const word = command.split(' ')[0];
    const lines = await h.send(command);
    const line = lines.reverse().find(l => l.startsWith(word + ' '));
    if (!line) return {};
    try {
      return JSON.parse(line.slice(word.length + 1));
    } catch (e) {
      run.errors.push(`${profile.name}: bad JSON from ${command}: ${line.slice(0, 200)}`);
      return {};
    }
  };
  h.state = () => h.ask('report');
  h.texts = async () => (await h.ask('texts')).texts || [];
  h.allText = async () => (await h.texts()).join(' | ');

  h.canvas = () =>
    page.evaluate(() => {
      const r = document.querySelector('canvas').getBoundingClientRect();
      return { left: r.left, top: r.top, width: r.width, height: r.height };
    });

  h.buttons = async () => {
    const answer = await h.ask('buttons');
    const box = await h.canvas();
    const sx = box.width / (answer.width || box.width);
    const sy = box.height / (answer.height || box.height);
    return (answer.buttons || []).map(b => ({
      ...b,
      cx: box.left + (b.rect[0] + b.rect[2] / 2) * sx,
      cy: box.top + (b.rect[1] + b.rect[3] / 2) * sy,
      size: [b.rect[2], b.rect[3]],
    }));
  };

  const matches = (b, label, row) =>
    (label instanceof RegExp ? label.test(b.label) : b.label === label) && (!row || (b.row || '') === row);

  h.find = async (label, row) => (await h.buttons()).filter(b => matches(b, label, row));

  // A real press where the button is drawn: a click on PC, a tap on a touch screen. A button scrolled out of its
  // list is scrolled to with a drag first. False when there is none to press.
  h.press = async (label, row, { quiet } = {}) => {
    for (let attempt = 0; attempt < 30; attempt++) {
      const all = await h.find(label, row);
      const shown = all.find(b => b.shown && b.enabled);
      if (shown) {
        await h.tapAt(shown.cx, shown.cy);
        await page.waitForTimeout(350);
        return true;
      }
      const hidden = all.find(b => !b.shown);
      if (hidden) {
        const box = await h.canvas();
        await page.mouse.move(box.left + box.width / 2, box.top + box.height / 2);
        await page.mouse.wheel(0, hidden.cy > box.top + box.height / 2 ? 200 : -200);
      }
      await page.waitForTimeout(300);
    }
    if (!quiet) run.errors.push(`${profile.name}: no button "${label}"${row ? ` in row "${row}"` : ''} to press`);
    return false;
  };

  h.tapAt = async (x, y) => {
    if (game.touch) await page.touchscreen.tap(x, y);
    else {
      await page.mouse.move(x, y);
      await page.mouse.down();
      await page.waitForTimeout(60);
      await page.mouse.up();
    }
  };

  // Holds a finger (or rests the mouse) on a point for `ms`, and returns the report read while holding: the
  // long-press tooltip shows only then. `during` runs while holding (a picture).
  h.hold = async (x, y, ms = 800, during = null) => {
    let report;
    if (game.touch) {
      const cdp = await page.context().newCDPSession(page);
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x, y }] });
      await page.waitForTimeout(ms);
      report = await h.state();
      if (during) await during();
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
    } else {
      await page.mouse.move(x, y);
      await page.waitForTimeout(ms);
      report = await h.state();
      if (during) await during();
      await page.mouse.move(2, 2);
    }
    await page.waitForTimeout(200);
    return report;
  };

  h.shot = async (name, caption) => {
    const file = path.join(run.out, `${profile.name}-${name}.png`);
    await page.screenshot({ path: file });
    run.note(`- 사진 ${caption}: \`${path.basename(file)}\``);
    await h.layout(name);
  };

  // Every screenshot also checks the layout: no two buttons one can press cover each other. Hexes, cards in a
  // scrolled list, the story's tap-anywhere and the units are hit areas that may touch by design; a window over the
  // screen (tip, talk, level-up, gear, report, history) leaves the screen under it drawn, so those are skipped.
  const LayoutIgnore = new Set(['칸', '직업', '이야기', '대상', '도움말']);
  h.layout = async name => {
    const state = await h.state();
    if (state.tip || state.talkWaiting || state.levelUp || state.gearOpen || state.reportOpen || state.historyOpen) return;
    const buttons = (await h.buttons()).filter(b => b.shown && b.enabled && !LayoutIgnore.has(b.row || ''));
    const clash = [];
    for (let i = 0; i < buttons.length; i++)
      for (let j = i + 1; j < buttons.length; j++) {
        const [ax, ay, aw, ah] = buttons[i].rect;
        const [bx, by, bw, bh] = buttons[j].rect;
        if (ax + 1 < bx + bw && bx + 1 < ax + aw && ay + 1 < by + bh && by + 1 < ay + ah) clash.push(`${buttons[i].label} / ${buttons[j].label}`);
      }
    run.check(clash.length === 0, `${name}: 단추가 겹치지 않는다${clash.length ? ' (' + clash.slice(0, 3).join(', ') + ')' : ''}`);
    // No line of text wider than its place (English and the biggest text size included).
    const spills = (await h.ask('spills')).spills || [];
    run.check(spills.length === 0, `${name}: 글자가 칸을 넘치지 않는다${spills.length ? ' (' + spills.slice(0, 3).map(t => t.slice(0, 40)).join(' | ') + ')' : ''}`);
  };

  // Frame times and memory over a few seconds on the screen now (GameRoot.Perf.cs), noted in the report.
  h.perf = async (name, ms = 3000) => {
    await h.send('perf reset');
    await page.waitForTimeout(ms);
    const perf = await h.ask('perf');
    run.note(`- 성능 ${name}: 프레임 ${perf.frames}, 평균 ${perf.avgMs} ms, 95% ${perf.p95Ms} ms, 최대 ${perf.maxMs} ms, GC ${perf.gc}번, 할당 ${perf.allocKbPerFrame} KB/프레임, 힙 ${perf.heapMb} MB`);
    return perf;
  };

  // Polls the game's report until the test passes, up to `ms`.
  h.until = async (test, ms = 10000) => {
    const deadline = Date.now() + ms;
    let last = {};
    while (Date.now() < deadline) {
      last = await h.state();
      if (test(last)) return last;
      await page.waitForTimeout(300);
    }
    return null;
  };

  return h;
}

// Waits (up to 45 minutes) until the page says it is the given commit (version.txt, written by the web demo build).
async function waitForCommit(url, sha) {
  const deadline = Date.now() + 45 * 60 * 1000;
  for (;;) {
    const deployed = await deployedCommit(url);
    if (deployed === sha) return deployed;
    if (Date.now() > deadline) throw new Error(`the page is still ${deployed || 'unknown'}, not ${sha}`);
    console.log(`waiting for ${sha.slice(0, 7)} (the page is ${deployed.slice(0, 7) || 'unknown'})`);
    await new Promise(r => setTimeout(r, 30000));
  }
}

async function deployedCommit(url) {
  try {
    const response = await fetch(url + 'version.txt?' + Date.now());
    return response.ok ? (await response.text()).trim() : '';
  } catch (e) {
    return '';
  }
}

// WebGL in headless Chromium, drawn in software.
const LaunchArgs = ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'];

module.exports = { DefaultUrl, createRun, Profiles, open, waitForCommit, deployedCommit, LaunchArgs };
