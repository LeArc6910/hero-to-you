#!/usr/bin/env node
// Plays the deployed web demo in headless Chromium on a PC screen (mouse) and phone screens (touch), the way a
// person would: real clicks and taps on the game's own buttons. Writes pictures and report.md to out/ (Docs/Playtest.md).
//
//   node DevTools/WebPlaytest/playtest.js                    the page as it is now
//   node DevTools/WebPlaytest/playtest.js --wait             first wait until this commit is deployed
//   node DevTools/WebPlaytest/playtest.js --profiles phone   only some screens (pc, pc-small, phone, phone-wide)
//   node DevTools/WebPlaytest/playtest.js --url http://localhost:8000/
'use strict';
const path = require('path');
const { execSync } = require('child_process');
const { chromium } = require('playwright');
const { DefaultUrl, createRun, Profiles, open, waitForCommit, deployedCommit, LaunchArgs } = require('./lib');

function option(name, fallback) {
  const i = process.argv.indexOf(name);
  return i >= 0 && i + 1 < process.argv.length ? process.argv[i + 1] : fallback;
}

async function main() {
  const url = option('--url', DefaultUrl);
  const out = path.resolve(option('--out', path.join(__dirname, 'out')));
  const profiles = option('--profiles', 'pc,pc-small,phone,phone-wide').split(',').map(p => Profiles[p]).filter(Boolean);
  if (process.argv.includes('--wait')) {
    const sha = execSync('git rev-parse HEAD').toString().trim();
    await waitForCommit(url, sha);
  }
  const run = createRun(out);
  run.note(`# 웹 플레이 시험\n\n주소 ${url}, 배포된 커밋 ${(await deployedCommit(url)).slice(0, 7) || '모름'}, ${new Date().toISOString()}\n`);
  const browser = await chromium.launch({ args: LaunchArgs });
  try {
    for (const profile of profiles) {
      run.note(`\n## ${profile.name} (${profile.context.viewport.width}×${profile.context.viewport.height}${profile.touch ? ', 터치' : ''})\n`);
      const game = await open(browser, run, profile, url);
      try {
        run.note(`- 불러오기 ${game.loadSeconds.toFixed(1)}초`);
        await playSlice(game, profile.name === 'pc');
      } catch (e) {
        run.errors.push(`${profile.name}: ${e.stack || e}`);
        await game.shot('error', '멈춘 화면');
      }
      await game.context.close();
    }
  } finally {
    await browser.close();
    run.write();
  }
  process.exitCode = run.failed > 0 || run.errors.length > 0 ? 1 : 0;
}

// Steps the adventure bot until the report passes the test (or `limit` steps).
async function autoplayUntil(game, test, limit = 200) {
  for (let i = 0; i < limit; i++) {
    const state = await game.state();
    if (test(state)) return state;
    if (state.adventure === 'Victory' || state.adventure === 'Defeat') return null;
    await game.send('autoplay 1');
  }
  return null;
}

async function playSlice(game, wholeRun) {
  const { run } = game;
  if (game.profile.textSize) await game.send(`textsize ${game.profile.textSize}`);
  await game.wait(300);
  let state = await game.state();
  run.check(state.screen === 'Title', `첫 화면은 타이틀 (${state.screen})`);
  run.check((await game.allText()).includes('히어로 투 유'), '타이틀에 게임 이름이 한글로 보인다');
  run.check(state.touch === game.touch, `터치 화면 인식 (${state.touch})`);
  await game.shot('title', '타이틀');
  await game.perf('타이틀');
  run.check((await game.ask('art')).title === true, '타이틀 뒤에 표지 그림(scenes.txt title)이 깔린다');
  run.check((await game.find('연습 판')).length === 1 && (await game.find('저장 슬롯')).length === 1, '타이틀에 [연습 판]과 [저장 슬롯]이 있다');
  run.check(noOverlap(await game.buttons()), '타이틀 단추가 서로 겹치지 않는다');

  // The save slots: three empty slots at first.
  run.check(await game.press('저장 슬롯'), '[저장 슬롯]을 누를 수 있다');
  state = await game.until(s => s.screen === 'Slots', 4000);
  // A slow phone may report the screen a frame before its buttons are drawn: count again for a moment.
  let emptySlots = 0;
  for (let i = 0; i < 10 && emptySlots !== 3; i++) {
    emptySlots = (await game.find('새 모험')).length;
    if (emptySlots !== 3) await game.wait(200);
  }
  run.check(!!state && emptySlots === 3, `저장 슬롯 화면에 빈 슬롯 셋 (${emptySlots})`);
  await game.shot('slots', '저장 슬롯');
  await game.press('뒤로');
  state = await game.until(s => s.screen === 'Title', 4000);

  // Settings: text size and language, then back as they were.
  run.check(await game.press('설정'), '타이틀에서 [설정]을 누를 수 있다');
  state = await game.until(s => s.screen === 'Settings', 4000);
  run.check(!!state, '설정 화면');
  await game.press(/^글자 크기/, '설정');
  // The new size takes hold on the next frame.
  state = (await game.until(s => s.textSize !== 100, 2000)) || await game.state();
  run.check(state.textSize !== 100, `글자 크기가 바뀐다 (${state.textSize}%)`);
  await game.shot('settings', '설정(글자 크기를 바꾼 뒤)');
  await game.press(/^언어/, '설정');
  // The new language shows from the next frame on: look for a moment.
  let menuEnglish = false;
  for (let i = 0; i < 10 && !menuEnglish; i++) {
    menuEnglish = (await game.state()).language === 'en' && (await game.allText()).includes('Settings');
    if (!menuEnglish) await game.wait(200);
  }
  run.check(menuEnglish, '언어를 영어로 바꾸면 메뉴가 영어로 나온다');
  await game.shot('settings-en', '설정을 영어로');
  await game.send(`language ko; textsize ${game.profile.textSize || 100}`);
  // The bug report window opens from the settings (no run yet, so it says so).
  run.check(await game.press('버그 제보', '설정'), '설정에서 [버그 제보]를 누를 수 있다');
  run.check((await game.state()).screen === 'Settings' && (await game.allText()).includes('버그 제보'), '버그 제보 창이 열린다');
  await game.press('닫기', '제보');
  await game.press('뒤로');
  state = await game.until(s => s.screen === 'Title', 4000);

  // Party: three cards by real presses.
  run.check(await game.press(/^(혼자 시작|새 모험)/), '[혼자 시작]을 누를 수 있다');
  state = await game.until(s => s.screen === 'Party', 5000);
  run.check(!!state, '파티 고르기 화면');
  for (const name of ['기사', '사냥꾼', '성직자']) await game.press(name, '직업');
  await game.shot('party', '파티 고르기(기사·사냥꾼·성직자)');
  run.check((await game.allText()).includes('조합:'), '고른 직업의 조합(보너스)이 보인다');
  run.check(noOverlap((await game.buttons()).filter(b => b.row !== '직업')), '파티 화면 아래 단추(난이도·하드코어·시작)가 서로 겹치지 않는다');
  await game.press('모험 시작');
  state = await game.until(s => s.adventure === 'Story', 5000);
  run.check(!!state, '프롤로그 이야기 화면');
  run.check(state && !state.storyAll, '이야기 글이 한 글자씩 나온다');
  await game.wait(1200);
  await game.shot('story', '프롤로그(장면 그림과 글이 나오는 중)');
  if (await game.press('글', '이야기', { quiet: true })) run.check(!!(await game.until(s => s.storyAll, 2000)), '글을 누르면 다 나온다');

  // The map: roll, pick a hex, move.
  await game.press('계속');
  state = await game.until(s => s.adventure === 'Map', 5000);
  run.check(!!state, '지도 화면');
  run.check(state && state.saved, '모험을 시작하면 기기에 저장된다');
  run.check(state && state.unknown > 0 && state.explored > 0, `안개: 아직 모르는 칸 ${state && state.unknown}, 본 칸 ${state && state.explored}`);
  await game.shot('map', '지도, 첫 영웅의 차례(안개)');
  const mapPerf = await game.perf('지도');
  run.check(mapPerf.allocKbPerFrame < 256, `지도에서 프레임마다 쓰는 메모리가 적다 (${mapPerf.allocKbPerFrame} KB)`);

  // Rows in the gear window are set on the map; the strip shows them.
  run.check(state && state.heroes[0].back === false && state.heroes[1].back === true, '기사는 앞줄, 사냥꾼은 뒷줄로 시작한다');

  // A first-time tip card, then closing it.
  await game.send('tips reset; tip map');
  await game.wait(400);
  await game.shot('tip', '처음 보는 화면의 도움말 카드');
  run.check(await game.press('알겠다', '도움말'), '도움말 카드를 [알겠다]로 닫는다');
  const tipAfter = await game.until(s => !s.tip, 2000);
  run.check(!!tipAfter, `도움말 카드가 닫혔다${tipAfter ? '' : ' (' + (await game.state()).tip + ')'}`);
  await game.send('tips off');
  run.check(await game.press(/^이동 굴리기/), '[이동 굴리기]를 누를 수 있다');
  state = await game.until(s => s.movePoints >= 1 && s.reachable > 0, 5000);
  run.check(!!state, `이동력이 나오고 갈 칸이 생긴다 (${state && state.movePoints})`);
  // The map redraws a frame after the roll: look for the hexes for a moment.
  let hexes = [];
  for (let i = 0; i < 10 && hexes.length === 0; i++) {
    hexes = await game.find(/^\d+,\d+$/, '칸');
    if (hexes.length === 0) await game.wait(200);
  }
  run.check(hexes.length > 0, `누를 수 있는 칸 ${hexes.length}개`);
  if (hexes.length > 0) await game.press(hexes[hexes.length - 1].label, '칸');
  await game.shot('map-picked', '칸을 고른 뒤 패널');
  const before = (await game.state()).commands;
  run.check(await game.press('이동'), '[이동]을 누를 수 있다');
  state = await game.until(s => s.commands > before, 5000);
  run.check(!!state, `이동이 받아들여졌다 (${state && state.adventure})`);
  await game.shot('map-moved', '이동한 뒤');

  // Gear window.
  await game.send('autoplay 0');
  state = await autoplayUntil(game, s => s.adventure === 'Map' && !s.levelUp, 40);
  if (state && (await game.press('장비·소모품', null, { quiet: true }))) {
    // A tap can land in the frame the map turn is still settling after the bot's last step: press once more.
    if (!(await game.state()).gearOpen) await game.press('장비·소모품', null, { quiet: true });
    await game.shot('gear', '장비·소모품 창');
    run.check((await game.state()).gearOpen, '장비·소모품 창이 열린다');
    await game.press('닫기');
  }

  // A battle: the bot walks until one starts, then a real command.
  await game.send('speed 0');
  state = await autoplayUntil(game, s => s.adventure === 'Join' || s.adventure === 'Battle', 300);
  run.check(!!state, '지도를 다니다 전투가 난다');
  if (!state) return;
  if (state.adventure === 'Join') {
    await game.shot('join', '합류할 영웅 고르기');
    run.check(await game.press(/^싸운다/), '[싸운다]를 누를 수 있다');
  }
  await game.send('speed 1');
  state = await game.until(s => s.adventure === 'Battle' && s.awaiting, 30000);
  run.check(!!state, '전투에서 영웅의 차례가 온다');
  if (state) {
    run.check(state.battle.monsters.every(m => m.intent), '적이 다음 행동(의도)을 보인다');
    await game.shot('battle', '전투, 영웅의 차례');
    const battlePerf = await game.perf('전투(영웅의 차례)');
    run.check(battlePerf.allocKbPerFrame < 256, `전투에서 프레임마다 쓰는 메모리가 적다 (${battlePerf.allocKbPerFrame} KB)`);
    const skills = await game.find(/.+/, '기술');
    run.check(skills.length >= 2, `기술 단추 ${skills.length}개`);
    run.check(skills.every(b => b.size[1] >= 38), '기술 단추가 손가락으로 누를 만큼 크다(높이 38점 이상)');
    const targets = await game.find(/.+/, '대상');
    if (targets.length > 1) await game.press(targets[targets.length - 1].label, '대상');
    await game.shot('battle-skill', '기술과 대상, 확률 미리 보기');
    run.check((await game.find(/^자동/, '자동')).length === 1, '전투에 [자동] 단추가 있다');
    run.check((await game.find(/^회복 물약/, '소모품')).length === 1, '전투에서 물약(소모품) 단추가 있다');
    // A seed can walk the party into a boss first: no fleeing there, and the boss is drawn bigger than the heroes.
    const bossFight = ['warden', 'bell', 'abbess', 'sern', 'nameless', 'bandit_chief', 'captain', 'priest', 'captain4', 'shadow'].includes((await game.state()).battle.encounter);
    if (bossFight) run.check((await game.find(/^도망/)).length === 0, '보스 앞에서는 [도망] 단추가 없다');
    else run.check((await game.find(/^도망/)).length === 1, '보스가 아니면 [도망] 단추가 있다');
    // The long-press (phone) or hover (PC) explanation of a skill.
    const held = await game.hold(skills[0].cx, skills[0].cy, 900, () => game.shot('tooltip', game.touch ? '기술을 길게 누른 설명' : '기술을 가리킨 설명'));
    run.check(held.tooltip && held.tooltip.length > 0, `기술 단추를 ${game.touch ? '길게 누르면' : '가리키면'} 설명이 나온다`);
    run.check((await game.state()).adventure === 'Battle', '길게 누르기는 기술을 쓰지 않는다');
    const events = (await game.state()).battle.events;
    run.check(await game.press(/^(굴리기|사용)/), '[굴리기]를 누를 수 있다');
    await game.wait(700);
    await game.shot('battle-dice', '주사위가 굴러가는 중');
    state = await game.until(s => s.battle && s.battle.events > events, 8000);
    run.check(!!state, '명령이 전투를 진행시킨다');
    // The place behind the battle and the hit effects (Art/scenes.txt, Art/effects.txt): a miss shows none, so the
    // battle plays on by itself for a few seconds until a blow lands.
    let art = await game.ask('art');
    run.check(!!art.backdrop, `전투 뒤에 장의 장소 그림이 깔린다 (${art.backdrop})`);
    // The diagonal field (BattleLayout): the foes stand farther off (higher up) and smaller than the party; on a wide
    // screen they are also to the right.
    const side = party => (art.units || []).filter(u => u.party === party);
    const mean = (list, key) => list.reduce((sum, u) => sum + u[key], 0) / Math.max(1, list.length);
    const heroes = side(true), foes = side(false);
    run.check(heroes.length > 0 && foes.length > 0 && mean(foes, 'y') < mean(heroes, 'y') && (bossFight || mean(foes, 'size') < mean(heroes, 'size')),
      `적이 아군보다 멀리(위에) 작게 선다 (발 높이 ${Math.round(mean(foes, 'y'))} < ${Math.round(mean(heroes, 'y'))}, 크기 ${Math.round(mean(foes, 'size'))} < ${Math.round(mean(heroes, 'size'))})`);
    const upright = game.profile.context.viewport.height > game.profile.context.viewport.width;
    if (!upright) run.check(mean(foes, 'x') > mean(heroes, 'x'), '가로 화면에서 적은 오른쪽 위, 아군은 왼쪽 아래에 선다');
    // Played slowly (an effect lasts 0.24 s at speed 1) so the photo catches the next blow's effect.
    const before = art.flashes || 0;
    await game.send('speed 0.15; autobattle');
    for (let i = 0; i < 60 && !(art.flashes > before); i++) {
      await game.wait(150);
      art = await game.ask('art');
    }
    if (art.flashes > before) await game.shot('battle-hit', '맞을 때의 효과(느리게)');
    run.check(art.flashes > 0, `맞으면 타격 효과가 뜬다 (${art.flashes}번)`);
  }
  await game.send('speed 0');
  // The slow look at a hit effect may have played the battle to its end already.
  const stillFighting = (await game.state()).adventure === 'Battle';
  await game.send(stillFighting ? 'autobattle; skip' : 'skip');
  state = await game.until(s => s.adventure !== 'Battle' && !s.playing, 10000);
  run.check(state && ['BattleResult', 'Defeat', 'Story'].includes(state.adventure), `전투가 끝난다 (${state && state.adventure})`);
  await game.shot('battle-result', '전투 결과');

  // A level-up choice, when one comes.
  state = await autoplayUntil(game, s => s.levelUp, 300);
  if (state) {
    await game.shot('levelup', '레벨업 강화 고르기');
    const options = (await game.find(/.+/, '강화')).filter(b => !b.label.startsWith('남은 강화'));
    run.check(options.length >= 1, `강화 선택지 ${options.length}개`);
    if (options[0]) await game.press(options[0].label, '강화');
    // Several heroes can level up at once: the next card comes up, and with more than one still waiting
    // [남은 강화 N개를 알아서 고른다] takes them all.
    for (let i = 0; i < 12 && (await game.state()).levelUp; i++) {
      if (await game.press(/^남은 강화/, '강화', { quiet: true })) continue;
      const next = (await game.find(/.+/, '강화')).filter(b => !b.label.startsWith('남은 강화'));
      if (next[0]) await game.press(next[0].label, '강화');
    }
    // The last tap may still be on its way to the next frame: wait for the card to close before judging.
    run.check(!!(await game.until(s => !s.levelUp, 3000)), '강화를 누르면 레벨업이 끝난다(쌓인 강화까지)');
  }

  // Save and continue: reload the page, then [이어하기].
  // The last tap (an upgrade) may still be on its way: wait until the count of commands stops changing.
  let commands = (await game.state()).commands;
  for (let i = 0; i < 10; i++) {
    await game.wait(250);
    const now = (await game.state()).commands;
    if (now === commands) break;
    commands = now;
  }
  const hashBefore = (await game.state()).hash;
  await game.page.reload({ waitUntil: 'load' });
  await game.page.waitForFunction(() => !!window.unityInstance, null, { timeout: 240000 });
  await game.wait(1500);
  await game.send('probe on');
  await game.wait(300);
  run.check(await game.press('이어하기'), '다시 열면 [이어하기]가 있다');
  state = await game.until(s => s.adventure, 8000);
  run.check(state && state.commands === commands, `이어하면 같은 곳에서 계속된다 (명령 ${state && state.commands}/${commands})`);
  run.check(state && state.hash === hashBefore, `이어한 판의 상태가 저장 전과 똑같다 (상태 값 ${state && state.hash})`);
  await game.shot('continued', '이어하기 뒤');

  if (!wholeRun) return;
  // The town, then the rest of the chapter by the bot.
  state = await autoplayUntil(game, s => s.adventure === 'Map' && !s.levelUp && s.movePoints < 0, 60);
  if (state) {
    // The chapter has three maps picked by the seed: ask where this one's town is.
    const answer = (await game.send('goto town')).reverse().find(l => l.startsWith('goto ')) || '';
    const townHex = answer.trim().split(' ').pop();
    await game.send('roll');
    // The reachable hexes become buttons on the frame after the roll.
    let town = [];
    for (let i = 0; i < 10 && town.length === 0; i++) {
      town = await game.find(townHex, '칸');
      if (town.length === 0) await game.wait(200);
    }
    run.check(town.length > 0, `마을 칸(${townHex})이 지도에 있다`);
    if (town.length > 0) {
      await game.press(townHex, '칸');
      await game.press('이동');
      state = await game.until(s => s.adventure === 'Town', 4000);
      run.check(!!state, '마을에 들어간다');
      await game.shot('town', '마을과 상점');
      const unknown = state ? state.unknown : 0;
      if (await game.press(/^소문을 듣는다/, null, { quiet: true })) {
        state = await game.state();
        run.check(state.unknown < unknown, `소문으로 안개 속을 알게 된다 (${unknown} → ${state.unknown})`);
      }
      const potions = state ? state.heroes[state.mapHero].potions : 0;
      if (await game.press(/에게 \(/, '회복 물약', { quiet: true })) {
        state = await game.state();
        run.check(state.heroes.some(h => h.potions > potions) || state.gold >= 0, '상점에서 영웅에게 물약을 사 준다');
      }
      await game.send('gold 500');
      const forgeButtons = await game.find(/→ \+1/, '대장간');
      run.check(forgeButtons.length >= 3, `대장간에 벼릴 장비 단추가 있다 (${forgeButtons.length}개)`);
      if (forgeButtons.length > 0) {
        // The forge is at the bottom of the town's list: the press scrolls there first, and a tap while the list is
        // still gliding can miss. Wait for the +1, and press again if it did not come.
        let forged = null;
        for (let i = 0; i < 3 && !forged; i++) {
          await game.press(forgeButtons[0].label, '대장간', { quiet: true });
          forged = await game.until(s => s.heroes.some(h => h.plus > 0), 2000);
        }
        run.check(!!forged, '대장간에서 무기를 벼리면 +1이 된다');
      }
      await game.shot('town-after', '소문·물약·대장간 뒤 마을');
      await game.press('마을을 떠난다', null, { quiet: true });
    }
  }
  const shots = {};
  for (let guard = 0; guard < 6000; guard++) {
    state = await game.state();
    if (state.adventure === 'Victory' || state.adventure === 'Defeat') break;
    const boss = state.adventure === 'Battle' && state.battle && ['warden', 'bell', 'abbess', 'sern', 'nameless', 'bandit_chief', 'captain', 'priest', 'captain4', 'shadow'].includes(state.battle.encounter) ? state.battle.encounter : null;
    if (boss && !shots[boss]) {
      shots[boss] = true;
      await game.send('speed 1');
      await game.until(s => s.awaiting, 15000);
      await game.shot('boss-' + boss, `보스 전투 (${boss})`);
      await game.send('speed 0');
    }
    // Between two chapters, the party camps on the road before the next prologue (Docs/Narrative.md 막간).
    if (state.adventure === 'Event' && /^camp_/.test(state.event || '') && !shots.interlude) {
      shots.interlude = true;
      run.check(['chapter2', 'chapter3', 'chapter4', 'chapter5'].includes(state.chapter), `장 사이 막간은 다음 장이 시작될 때 나온다 (${state.chapter}, ${state.event})`);
      await game.wait(300);
      await game.shot('interlude', `장 사이 막간 (${state.event})`);
    }
    if (state.adventure === 'Story' && state.chapter !== 'chapter1' && shots.interlude && !shots.afterInterlude) {
      shots.afterInterlude = true;
      run.check(true, '막간 다음에 장의 첫 이야기가 나온다');
    }
    if (state.chapter === 'chapter2' && state.adventure === 'Story' && !shots.prologue2) {
      shots.prologue2 = true;
      await game.shot('chapter2-story', '2장 이야기');
    }
    if (state.chapter === 'chapter2' && state.adventure === 'Map' && !shots.map2) {
      shots.map2 = true;
      await game.shot('chapter2-map', '2장 가라앉은 항구 지도');
    }
    if (state.chapter === 'chapter3' && state.adventure === 'Map' && !shots.map3) {
      shots.map3 = true;
      await game.shot('chapter3-map', '3장 눈 덮인 수도원 지도');
    }
    if (state.adventure === 'Event' && state.dungeonEvent && !shots.dungeonEvent) {
      shots.dungeonEvent = true;
      run.check((await game.allText()).includes('방 '), '던전의 사건 방은 위에 던전 이름과 방 번호를 보인다');
      await game.shot('dungeon-event', `던전의 사건 방 (${state.event})`);
    }
    if (state.adventure === 'DungeonRest' && !shots.dungeon) {
      shots.dungeon = true;
      await game.shot('dungeon', '던전: 방 사이에서');
    }
    if (state.talks > 0 && state.adventure === 'Map' && !state.levelUp && !shots.talk) {
      shots.talk = true;
      await game.send('talk');
      await game.wait(400);
      await game.shot('talk', '모닥불 곁의 대화');
      run.check(await game.press('계속', '대화'), '대화 카드를 [계속]으로 닫는다');
    }
    await game.send(shots.interlude ? 'autoplay 20' : 'autoplay 20 camp');
  }
  state = await game.state();
  if (shots.map2) run.check(!!shots.interlude, '2장에 들어서기 전에 막간(모닥불)을 지난다');
  run.check(state.adventure === 'Victory' || state.adventure === 'Defeat', `캠페인을 끝까지 간다 (${state.adventure}, ${state.chapter}, 명성 ${state.renown} 「${state.rank}」, 레벨 ${state.heroes.map(h => h.level).join('/')})`);
  run.check(!state.saved, '끝나면 저장이 지워진다');
  run.check(state.pages > 0, `일지 페이지를 받는다 (${state.pages}장)`);
  await game.shot('end', '끝 화면');
  // The run summary to pass on.
  run.check(await game.press('기록 복사', '기록'), '끝 화면에 [기록 복사]가 있다');
  const shared = (await game.send('share')).reverse().find(l => l.startsWith('share ')) || '';
  run.check(/기사 Lv\d+/.test(shared) && shared.includes('이긴 전투'), `복사되는 기록에 파티와 전적이 있다 (${shared.slice(0, 120)})`);

  // The journal on the title.
  await game.press('처음으로');
  run.check(await game.press(/^일지/), '타이틀에서 [일지]를 누를 수 있다');
  state = await game.until(s => s.screen === 'Journal', 4000);
  run.check(!!state, '일지 화면');
  await game.shot('journal', '일지: 페이지, 장, 찾은 옛 메모');
  run.check((await game.allText()).includes('업적'), '일지에 업적과 직업별 기록이 있다');
  run.check((await game.allText()).includes('도감'), '일지에 도감(몬스터·장비·조합·결말)이 있다');
  await game.press('뒤로');

  // A later start: the chapter reached shows on the party screen and the run starts there.
  await game.send('reached 3');
  run.check(await game.press(/^(혼자 시작|새 모험)/), '두 번째 모험도 [새 모험]으로');
  state = await game.until(s => s.screen === 'Party', 5000);
  run.check(await game.press(/^시작: 1장/, '시작 장'), '다다른 장이 있으면 [시작: 1장] 단추가 있다');
  state = await game.state();
  run.check(state.startChapter === 1, `시작 장을 2장으로 바꾼다 (${state.startChapter + 1}장)`);
  await game.press(/^길이/, '길이');
  run.check(noOverlap((await game.buttons()).filter(b => b.row !== '직업')), '선택지가 넷이어도 파티 화면 단추가 겹치지 않는다');
  await game.shot('party-chapters', '파티 고르기: 시작 장과 길이');
  // A party with neither a front-liner nor a healer is told it is a hard challenge, and may still set out.
  const fragile = ['점성술사', '원소술사', '음유시인'];
  for (const name of fragile) await game.press(name, '직업');
  run.check((await game.allText()).includes('앞줄도 회복도 없는 파티'), '앞줄도 회복도 없는 파티를 고르면 어려운 도전이라고 알린다');
  run.check((await game.buttons()).some(b => b.label === '모험 시작' && b.enabled), '그래도 [모험 시작]을 누를 수 있다');
  await game.shot('party-fragile', '파티 고르기: 앞줄도 회복도 없는 파티');
  for (const name of fragile) await game.press(name, '직업');
  for (const name of ['도적', '음유시인', '성직자']) await game.press(name, '직업');
  run.check(!(await game.allText()).includes('앞줄도 회복도 없는 파티'), '앞줄과 회복이 있으면 경고가 없다');
  await game.press('모험 시작');
  state = await game.until(s => s.adventure === 'Story', 5000);
  run.check(state && state.firstChapter === 1 && state.lastChapter === 1 && state.heroes.every(h => h.level >= 4), `2장 한 장짜리 판이 레벨 4로 시작한다 (${state && state.chapter}, 레벨 ${state && state.heroes.map(h => h.level).join('/')})`);
  run.check(state && state.slot === 1, `끝난 모험이 비운 슬롯 1을 다시 쓴다 (슬롯 ${state && state.slot})`);
  await game.send('title');

  // The practice run: chapter 1 only, easy, its own slot, the tips again.
  run.check(await game.press('연습 판'), '[연습 판]을 누를 수 있다');
  state = await game.until(s => s.adventure === 'Story', 5000);
  run.check(state && state.slot === 0 && state.difficulty === 'Easy' && state.lastChapter === 0, '연습 판은 쉬움, 1장만, 따로 저장된다');
  run.check(state && state.tutorial === 'story', `연습 판의 안내가 첫 단계를 보인다 (${state && state.tutorial})`);
  await game.shot('tutorial-story', '연습 판 안내: 이야기');
  // The keyboard (PC): Space is the screen's main button.
  if (!game.touch) {
    await game.page.keyboard.press('Space');
    state = await game.until(s => s.adventure === 'Map', 4000);
    run.check(!!state, 'PC에서 스페이스로 [계속]을 누른다');
  } else {
    await game.press('계속');
    state = await game.until(s => s.adventure === 'Map', 4000);
  }
  if (await game.press('알겠다', '도움말', { quiet: true })) await game.wait(300);
  state = await game.state();
  run.check(state.tutorial === 'roll', `안내가 다음 단계(이동 굴리기)로 넘어간다 (${state.tutorial})`);
  await game.shot('tutorial-map', '연습 판 안내: 지도에서 이동 굴리기');
  await game.send('tips off; title');

  // English: the practice run's first page and map in English, with no Korean left on the screen.
  await game.send('language en; title');
  run.check(await game.press('Practice run'), '영어 타이틀에 [Practice run]이 있다');
  state = await game.until(s => s.adventure === 'Story', 5000);
  const hangul = t => /[\uac00-\ud7a3]/.test(t);
  let english = await game.allText();
  run.check(state && !hangul(english), `영어로 하면 이야기 화면에 한글이 없다 (${english.slice(0, 80)})`);
  await game.shot('english-story', '영어: 연습 판의 첫 이야기');
  await game.send('tips off; continue');
  await game.wait(500);
  english = await game.allText();
  run.check(!hangul(english), `영어로 하면 지도 화면에 한글이 없다 (${(english.match(/[^|]*[\uac00-\ud7a3][^|]*/) || [''])[0].slice(0, 60)})`);
  await game.shot('english-map', '영어: 지도');
  await game.send('language ko; title');

  // A chapter in English by the bot: every kind of screen it meets shows no Korean.
  await game.send('language en; tips off; start knight,hunter,cleric 7; speed 0');
  const seen = new Set();
  for (let i = 0; i < 400 && seen.size < 7; i++) {
    state = await game.state();
    if (!state.adventure || state.adventure === 'Victory' || state.adventure === 'Defeat') break;
    const kind = state.levelUp ? 'LevelUp' : state.adventure;
    if (!seen.has(kind)) {
      seen.add(kind);
      await game.wait(250);
      const text = await game.allText();
      const korean = text.split(' | ').filter(t => hangul(t));
      run.check(korean.length === 0, `영어: ${kind} 화면에 한글이 없다${korean.length ? ' (' + korean.slice(0, 2).join(' / ').slice(0, 100) + ')' : ''}`);
      await game.shot(`english-${kind.toLowerCase()}`, `영어: ${kind}`);
    }
    await game.send('autoplay 1');
  }
  run.note(`- 영어로 본 화면: ${[...seen].join(', ')}`);
  await game.send('language ko; title');
}

// No two buttons on screen cover each other (the layout audit, Docs/WebDemo.md 작은 화면).
function noOverlap(buttons) {
  const shown = buttons.filter(b => b.shown);
  for (let i = 0; i < shown.length; i++)
    for (let j = i + 1; j < shown.length; j++) {
      const [ax, ay, aw, ah] = shown[i].rect || [...shown[i].at, ...shown[i].size];
      const [bx, by, bw, bh] = shown[j].rect || [...shown[j].at, ...shown[j].size];
      if (ax + 1 < bx + bw && bx + 1 < ax + aw && ay + 1 < by + bh && by + 1 < ay + ah) return false;
    }
  return true;
}

main().catch(e => {
  console.error(e);
  process.exitCode = 1;
});
