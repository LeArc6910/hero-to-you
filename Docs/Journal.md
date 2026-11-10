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

## 장 (`perks.csv`: `id, name, pages, effect, text`)

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

`effect`는 `gold, potion, tonic, hope, doom, xp, hard` 가운데 하나다. 장은 데이터라 이름과 숫자를 시트에서 바꾼다. 새 효과가 필요하면 `Adventure.ApplyPerks`와 `GameData.ReadPerks`에 더한다.

## 찾은 옛 메모

사건의 메모(`events.csv`의 `journal`)를 처음 본 판이 끝나면 일지에 모인다. 일지 화면에 찾은 메모와 남은 개수가 보인다. 영웅의 발자취를 판을 넘어 모으는 것이 이 게임의 수집이다(`Docs/Narrative.md` 서사의 방향).

## 업적 (`achievements.csv`: `id, name, rule, text`)

판이 끝날 때 규칙을 보고 처음 이룬 업적을 일지에 남긴다(끝 화면에 보인다). 규칙: `win`, `chapters N`, `kind_ending`, `grim_ending`, `kind_all`, `renown N`, `dungeons N`, `contracts N`, `talks N`, `notes N`(여러 판에 걸쳐), `hard_win`, `hardcore_win`, `easy_win`, `classes N`(이긴 적이 있는 직업 수), `runs N`, `no_fall`. 지금 18개.

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
