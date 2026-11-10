# 일지 (판을 넘는 성장, M7)

한 판이 끝나도 남는 것. 코드는 `Scripts/Core/Adventure/Journal.cs`(페이지, 장, 찾은 메모, 글로 쓰고 읽기), `Adventure.cs`(`PagesEarned`, 장의 효과), 클라이언트는 `GameRoot.Menu.cs`(일지 화면), `SaveStore`(기기의 `journal.txt`). 데이터는 `Data/Journal/perks.csv`.

## 페이지

모험이 끝나면(이기든 지든) 페이지를 받는다.

| 무엇 | 페이지 |
|---|---|
| 끝까지 간 것 | 1 |
| 지난 장마다 | 3 |
| 쓰러뜨린 중간 보스마다 | 1 |
| 다정한 결말마다 | 1 |
| 가시밭길로 캠페인을 이기면 | 모두 두 배 |

## 장 (`perks.csv`: `id, name, pages, effect, text, requires`)

페이지로 한 번 펼치면 계속 쓸 수 있다. 펼친 장 가운데 다음 모험에 가져갈 것을 고른다(여럿 가능). 가져간 장은 저장본의 `perks` 줄에 남는다(`Docs/Saving.md`).

| 장 | 페이지 | 효과(`effect`) |
|---|---|---|
| 두둑한 지갑 | 2 | `gold 20`: 금화 +20 |
| 넉넉한 물약 | 2 | `potion 1`: 영웅마다 물약 +1 |
| 리엔의 결의 | 3 | `tonic 1`: 영웅마다 결의 물약 1 |
| 희망의 장 | 4 | `hope 1`: 희망 +1 |
| 서두른 출발 | 4 | `doom -1`: 첫 장 균열 −1 |
| 선배의 가르침 | 5 | `xp 10`: 영웅마다 경험치 10(레벨 2) |
| 가시밭길 | 6 | `hard`: 몬스터 생명력 +25%, 이기면 페이지 두 배 |
| 손으로 그린 지도 | 3 | `reveal 6`: 시작할 때 출발점에서 가까운 모르는 칸 6개가 밝혀져 있다 |
| 새벽 기도 | 4 | `bless 1`: 영웅마다 축복 하나 |
| 먼저 난 소문 | 5 | `renown 2`: 명성 +2 |
| 잘 벼린 날 | 6 | `forge 1`: 영웅마다 무기가 한 번 벼려져 있다(대장간 +1과 같다) |
| 은행가의 편지 | 8 | `gold 40`, 업적 「의뢰꾼」이 있어야 펼친다 |
| 던전꾼의 짐 | 7 | `tonic 1`, 업적 「깊은 곳까지」 |
| 모닥불 곁의 이야기 | 7 | `hope 1`, 업적 「모닥불 이야기꾼」 |
| 봉인의 기억 | 9 | `doom -1`, 업적 「첫 봉인」 |

`effect`는 `gold, potion, tonic, hope, doom, xp, hard, reveal, bless, forge, renown` 가운데 하나다(`GameData.PerkVerbs`, 직업 조합 `combos.csv`도 같은 말을 쓴다). `requires`에 업적 id가 있으면 그 업적을 이룬 일지만 그 장을 펼칠 수 있다(2026-11-10, 지금 15장). 장은 데이터라 이름과 숫자를 시트에서 바꾼다. 새 효과가 필요하면 `Adventure.ApplyGift`와 `GameData.PerkVerbs`에 더한다.

## 도감 (2026-11-10)

판이 끝나면 그 판에서 싸운 몬스터, 얻거나 든 장비, 가진 조합, 읽은 이야기 장이 일지의 도감에 모인다(`Journal.Codex`, `journal.txt`의 `codex_monsters`·`codex_items`·`codex_combos`·`codex_stories` 줄). 결말은 장의 끝 이야기(보통·다정·쓸쓸) 가운데 읽은 것을 센다(`EndingsSeen`). 일지 화면의 도감 칸에 종류마다 모은 수와 이름이 보이고, 아직 못 본 것은 ???로 남는다.

## 새 업적과 그 장 (2026-11-10)

| 업적 | 규칙 | 열리는 장 |
|---|---|---|
| 짧은 길 | `short_win`: 짧은 판을 끝까지 이긴다 | 샛길 지도(5장, `reveal 10`) |
| 세 갈래 인연 | `combos 3`: 조합 셋을 가진 파티로 판을 마친다 | — |
| 다시 만난 얼굴들 | `old_friends 3`: 한 판에서 앞 장에 구한 사람 셋을 만난다 | 옛 친구들의 편지(7장, `renown 3`) |
| 짐승의 이름 | `monsters 30`: 도감의 몬스터 30종 | 사냥꾼의 수첩(6장, `xp 8`) |
| 장비 수집가 | `items 40`: 도감의 장비 40종 | 단골 대장장이(8장, `forge 1`) |
| 여러 개의 끝 | `endings 8`: 도감의 결말 8개 | — |

업적은 24개, 장은 19개가 됐다.

## 다다른 장 (`reached`, 2026-11-10)

일지는 어느 판이든 가장 멀리 간 장을 기억한다. 파티 고르기 화면에서 그 장까지는 어디서든 시작할 수 있다(`Docs/Map.md` 짧은 판). 짧은 판은 지난 장·페이지·업적(승리가 필요 없는 것)은 남지만, **캠페인 승리로 치지 않는다**: 「일지의 끝」 같은 승리 업적, 끝까지 간 판 수, 직업의 끝까지 기록에 들지 않는다(`Adventure.FullRun`).

## 찾은 옛 메모

사건의 메모(`events.csv`의 `journal`)를 처음 본 판이 끝나면 일지에 모인다. 일지 화면에 찾은 메모와 남은 개수가 보인다. 영웅의 발자취를 판을 넘어 모으는 것이 이 게임의 수집이다(`Docs/Narrative.md` 서사의 방향).

## 업적 (`achievements.csv`: `id, name, rule, text`)

판이 끝날 때 규칙을 보고 처음 이룬 업적을 일지에 남긴다(끝 화면에 보인다). 규칙: `win`, `chapters N`, `kind_ending`, `grim_ending`, `kind_all`, `renown N`, `dungeons N`, `contracts N`, `talks N`, `notes N`(여러 판에 걸쳐), `hard_win`, `hardcore_win`, `easy_win`, `classes N`(이긴 적이 있는 직업 수), `runs N`, `no_fall`, 그리고 2026-11-10에 더한 `short_win`, `combos N`, `old_friends N`, `monsters N`, `items N`, `endings N`(도감, 아래). 지금 24개.

## 직업별 기록

직업마다 모험한 판과 끝까지 간 판을 센다(일지 화면 아래).

## 기록 (`journal.txt`)

```
hero-to-you journal 1
pages 7
earned 12
runs 3
victories 1
chapters 3
renown 16
unlocked coin_purse,hope_page
notes elder,hero_statue,pilgrim
achieved first_seal,first_steps
classruns cleric:2,hunter:2,knight:2
classwins knight:1
```

깨졌거나 없으면 빈 일지로 시작한다. 가져갈 장의 선택은 기기의 PlayerPrefs(`perks_chosen`)에 있다.
