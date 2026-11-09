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

async function playSlice(game, wholeRun) {
  const { run } = game;
  let state = await game.state();
  run.check(state.screen === 'Title', `첫 화면은 타이틀 (${state.screen})`);
  run.check((await game.allText()).includes('히어로 투 유'), '타이틀에 게임 이름이 한글로 보인다');
  run.check(state.touch === game.touch, `터치 화면 인식 (${state.touch})`);
  await game.shot('title', '타이틀');

  // Party: three cards by real presses.
  run.check(await game.press('혼자 시작 (영웅 셋을 모두 조종)'), '[혼자 시작]을 누를 수 있다');
  state = await game.until(s => s.screen === 'Party', 5000);
  run.check(!!state, '파티 고르기 화면');
  for (const name of ['기사', '사냥꾼', '성직자']) await game.press(name, '직업');
  await game.shot('party', '파티 고르기(기사·사냥꾼·성직자)');
  const start = (await game.find('모험 시작', null))[0];
  run.check(start && start.enabled, '셋을 고르면 [모험 시작]이 켜진다');
  await game.press('모험 시작');
  state = await game.until(s => s.adventure === 'Story', 5000);
  run.check(!!state, '프롤로그 이야기 화면');
  await game.shot('story', '프롤로그');

  // First battle: one real command, then the rest by the bot.
  await game.press('계속');
  state = await game.until(s => s.adventure === 'Battle' && s.awaiting, 30000);
  run.check(!!state, '첫 전투에서 영웅의 차례가 온다');
  if (!state) return;
  run.check(state.battle.monsters.length === 3 && state.battle.monsters.every(m => m.intent), '적 셋이 다음 행동(의도)을 보인다');
  await game.shot('battle', '첫 전투, 영웅의 차례');
  const skills = await game.find(/.+/, '기술');
  run.check(skills.length >= 3, `기술 단추 ${skills.length}개`);
  run.check(skills.every(b => b.size[1] >= 38), '기술 단추가 손가락으로 누를 만큼 크다(높이 38점 이상)');
  if (skills[1]) await game.press(skills[1].label, '기술');
  const targets = await game.find(/.+/, '대상');
  run.check(targets.length >= 1, `대상 고르기 칸 ${targets.length}개`);
  if (targets.length > 1) await game.press(targets[targets.length - 1].label, '대상');
  await game.shot('battle-skill', '두 번째 기술과 대상, 확률 미리 보기');
  const before = (await game.state()).battle.events;
  const pressed = await game.press(/^(굴리기|사용)/);
  run.check(pressed, '[굴리기]를 누를 수 있다');
  await game.wait(700);
  await game.shot('battle-dice', '주사위가 굴러가는 중');
  state = await game.until(s => s.battle && s.battle.events > before, 8000);
  run.check(!!state, '명령이 전투를 진행시킨다');

  await game.send('speed 0');
  await game.send('autobattle');
  await game.send('skip');
  state = await game.until(s => s.adventure !== 'Battle' && !s.playing, 10000);
  run.check(state && ['BattleResult', 'Defeat'].includes(state.adventure), `전투가 끝난다 (${state && state.adventure})`);
  await game.shot('battle-result', '전투 결과');
  if (!state || state.adventure !== 'BattleResult') return;

  // The event after it.
  await game.press('계속');
  state = await game.until(s => s.adventure === 'Event', 5000);
  run.check(!!state, `사건 화면 (${state && state.event})`);
  await game.shot('event', '사건');
  const choices = (await game.find(/.+/, '선택지')).filter(b => b.enabled);
  run.check(choices.length >= 2, `선택지 ${choices.length}개`);
  if (choices[0]) await game.press(choices[0].label, '선택지');
  if ((await game.state()).adventure === 'Event') {
    await game.shot('event-pick', '판정할 영웅 고르기');
    await game.press('굴려 본다');
  }
  state = await game.until(s => s.adventure === 'EventResult', 5000);
  run.check(!!state, '사건 결과 화면');
  await game.shot('event-result', '사건 결과');

  if (!wholeRun) return;
  // The rest of the slice, quickly: every battle by the bot, every event by its first choice.
  let bossShot = false;
  for (let guard = 0; guard < 200; guard++) {
    state = await game.state();
    if (state.adventure === 'Victory' || state.adventure === 'Defeat') break;
    if (state.adventure === 'Battle') {
      if (!bossShot && state.battle && state.battle.encounter === 'warden') {
        bossShot = true;
        await game.send('speed 1');
        await game.until(s => s.awaiting, 15000);
        await game.shot('boss', '보스 잿빛 파수꾼');
        await game.send('speed 0');
      }
      await game.send('autobattle; skip');
    } else if (state.adventure === 'Event') {
      await game.send('autochoose');
    } else {
      await game.send('continue');
    }
    await game.wait(150);
  }
  state = await game.state();
  run.check(state.adventure === 'Victory' || state.adventure === 'Defeat', `세로 조각을 끝까지 간다 (${state.adventure}, 레벨 ${state.heroes.map(h => h.level).join('/')})`);
  await game.shot('end', '끝 화면');
}

main().catch(e => {
  console.error(e);
  process.exitCode = 1;
});
