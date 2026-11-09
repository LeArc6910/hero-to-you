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
  const profiles = option('--profiles', 'pc,phone,phone-wide').split(',').map(p => Profiles[p]).filter(Boolean);
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
  let state = await game.state();
  run.check(state.screen === 'Title', `첫 화면은 타이틀 (${state.screen})`);
  run.check((await game.allText()).includes('히어로 투 유'), '타이틀에 게임 이름이 한글로 보인다');
  run.check(state.touch === game.touch, `터치 화면 인식 (${state.touch})`);
  await game.shot('title', '타이틀');

  // Party: three cards by real presses.
  run.check(await game.press(/^(혼자 시작|새 모험)/), '[혼자 시작]을 누를 수 있다');
  state = await game.until(s => s.screen === 'Party', 5000);
  run.check(!!state, '파티 고르기 화면');
  for (const name of ['기사', '사냥꾼', '성직자']) await game.press(name, '직업');
  await game.shot('party', '파티 고르기(기사·사냥꾼·성직자)');
  await game.press('모험 시작');
  state = await game.until(s => s.adventure === 'Story', 5000);
  run.check(!!state, '프롤로그 이야기 화면');
  await game.shot('story', '프롤로그');

  // The map: roll, pick a hex, move.
  await game.press('계속');
  state = await game.until(s => s.adventure === 'Map', 5000);
  run.check(!!state, '지도 화면');
  run.check(state && state.saved, '모험을 시작하면 기기에 저장된다');
  run.check(state && state.unknown > 0 && state.explored > 0, `안개: 아직 모르는 칸 ${state && state.unknown}, 본 칸 ${state && state.explored}`);
  await game.shot('map', '지도, 첫 영웅의 차례(안개)');

  // A first-time tip card, then closing it.
  await game.send('tips reset; tip map');
  await game.wait(400);
  await game.shot('tip', '처음 보는 화면의 도움말 카드');
  run.check(await game.press('알겠다', '도움말'), '도움말 카드를 [알겠다]로 닫는다');
  run.check(!(await game.state()).tip, '도움말 카드가 닫혔다');
  await game.send('tips off');
  run.check(await game.press(/^이동 굴리기/), '[이동 굴리기]를 누를 수 있다');
  state = await game.until(s => s.movePoints >= 1 && s.reachable > 0, 5000);
  run.check(!!state, `이동력이 나오고 갈 칸이 생긴다 (${state && state.movePoints})`);
  const hexes = await game.find(/^\d+,\d+$/, '칸');
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
    const skills = await game.find(/.+/, '기술');
    run.check(skills.length >= 2, `기술 단추 ${skills.length}개`);
    run.check(skills.every(b => b.size[1] >= 38), '기술 단추가 손가락으로 누를 만큼 크다(높이 38점 이상)');
    const targets = await game.find(/.+/, '대상');
    if (targets.length > 1) await game.press(targets[targets.length - 1].label, '대상');
    await game.shot('battle-skill', '기술과 대상, 확률 미리 보기');
    run.check((await game.find(/^회복 물약/, '소모품')).length === 1, '전투에서 물약(소모품) 단추가 있다');
    run.check((await game.find(/^도망/)).length === 1, '보스가 아니면 [도망] 단추가 있다');
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
  }
  await game.send('speed 0');
  await game.send('autobattle; skip');
  state = await game.until(s => s.adventure !== 'Battle' && !s.playing, 10000);
  run.check(state && ['BattleResult', 'Defeat', 'Story'].includes(state.adventure), `전투가 끝난다 (${state && state.adventure})`);
  await game.shot('battle-result', '전투 결과');

  // A level-up choice, when one comes.
  state = await autoplayUntil(game, s => s.levelUp, 300);
  if (state) {
    await game.shot('levelup', '레벨업 강화 고르기');
    const options = await game.find(/.+/, '강화');
    run.check(options.length >= 1, `강화 선택지 ${options.length}개`);
    if (options[0]) await game.press(options[0].label, '강화');
    run.check(!(await game.state()).levelUp, '강화를 누르면 레벨업이 끝난다');
  }

  // Save and continue: reload the page, then [이어하기].
  const commands = (await game.state()).commands;
  await game.page.reload({ waitUntil: 'load' });
  await game.page.waitForFunction(() => !!window.unityInstance, null, { timeout: 240000 });
  await game.wait(1500);
  await game.send('probe on');
  await game.wait(300);
  run.check(await game.press('이어하기'), '다시 열면 [이어하기]가 있다');
  state = await game.until(s => s.adventure, 8000);
  run.check(state && state.commands === commands, `이어하면 같은 곳에서 계속된다 (명령 ${state && state.commands}/${commands})`);
  await game.shot('continued', '이어하기 뒤');

  if (!wholeRun) return;
  // The town, then the rest of the chapter by the bot.
  state = await autoplayUntil(game, s => s.adventure === 'Map' && !s.levelUp && s.movePoints < 0, 60);
  if (state) {
    await game.send('goto town; roll');
    const town = await game.find('2,2', '칸');
    if (town.length > 0) {
      await game.press('2,2', '칸');
      await game.press('이동');
      state = await game.until(s => s.adventure === 'Town', 4000);
      run.check(!!state, '마을에 들어간다');
      await game.shot('town', '마을과 상점');
      const unknown = state ? state.unknown : 0;
      if (await game.press(/^소문을 듣는다/, null, { quiet: true })) {
        state = await game.state();
        run.check(state.unknown < unknown, `소문으로 안개 속을 알게 된다 (${unknown} → ${state.unknown})`);
      }
      const potions = state.heroes[state.mapHero].potions;
      if (await game.press(/에게 \(/, '회복 물약', { quiet: true })) {
        state = await game.state();
        run.check(state.heroes.some(h => h.potions > potions) || state.gold >= 0, '상점에서 영웅에게 물약을 사 준다');
      }
      await game.shot('town-after', '소문과 물약 뒤 마을');
      await game.press('마을을 떠난다', null, { quiet: true });
    }
  }
  const shots = {};
  for (let guard = 0; guard < 6000; guard++) {
    state = await game.state();
    if (state.adventure === 'Victory' || state.adventure === 'Defeat') break;
    const boss = state.adventure === 'Battle' && state.battle && ['warden', 'bell', 'bandit_chief', 'captain'].includes(state.battle.encounter) ? state.battle.encounter : null;
    if (boss && !shots[boss]) {
      shots[boss] = true;
      await game.send('speed 1');
      await game.until(s => s.awaiting, 15000);
      await game.shot('boss-' + boss, `보스 전투 (${boss})`);
      await game.send('speed 0');
    }
    if (state.chapter === 'chapter2' && state.adventure === 'Story' && !shots.prologue2) {
      shots.prologue2 = true;
      await game.shot('chapter2-story', '2장 이야기');
    }
    if (state.chapter === 'chapter2' && state.adventure === 'Map' && !shots.map2) {
      shots.map2 = true;
      await game.shot('chapter2-map', '2장 가라앉은 항구 지도');
    }
    await game.send('autoplay 20');
  }
  state = await game.state();
  run.check(state.adventure === 'Victory' || state.adventure === 'Defeat', `캠페인을 끝까지 간다 (${state.adventure}, ${state.chapter}, 명성 ${state.renown} 「${state.rank}」, 레벨 ${state.heroes.map(h => h.level).join('/')})`);
  run.check(!state.saved, '끝나면 저장이 지워진다');
  run.check(state.pages > 0, `일지 페이지를 받는다 (${state.pages}장)`);
  await game.shot('end', '끝 화면');

  // The journal on the title.
  await game.press('처음으로');
  run.check(await game.press(/^일지/), '타이틀에서 [일지]를 누를 수 있다');
  state = await game.until(s => s.screen === 'Journal', 4000);
  run.check(!!state, '일지 화면');
  await game.shot('journal', '일지: 페이지, 장, 찾은 옛 메모');
  await game.press('뒤로');
}

main().catch(e => {
  console.error(e);
  process.exitCode = 1;
});
