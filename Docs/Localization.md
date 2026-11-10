# 현지화 준비 (영어판을 위한 바탕)

출시 목표(Steam·모바일)에 영어판이 있다. 화면의 낱말을 코드에서 시트로 뺐고(2026-11-10), 같은 날 데이터 시트의 이름과 글에 영어 열을 더했다.

## 지금 있는 것

- `Resources/Data/Ui/strings.csv`: `id, ko, en`. 클라이언트의 `Strings.Get(id)`, `Strings.Format(id, 값…)`이 읽는다(`Client/Strings.cs`)
- 설정의 [언어]가 한국어와 영어를 바꾼다(`Settings.Language`, 기기에 저장). 영어 칸이 비면 한국어가 나오고, 시트에 없는 id는 id 그대로 보여서 빠진 곳이 눈에 띈다
- 시트로 옮긴 것: 타이틀, 설정(색약·움직임·버그 제보 포함), 파티 고르기(난이도·하드코어), 도움말 카드 13개, 이야기 화면의 안내, 던전 화면, 동료 대화, 줄, 지난 일 창, 버그 제보 창, 자동 전투, 대장간·새 동료 머리말, 일지의 업적·직업 기록, 끝 화면의 다시 하기, 지도의 주요 단추(이동 굴리기, 야영, 머물기, 이동, 다시 굴리기, 장비·소모품), 싸운다, 마을을 떠난다, 굴리기·사용·도망

## 데이터의 영어 (`*_en` 열, 2026-11-10)

- 시트의 아무 열 `X` 옆에 `X_en` 열을 두면, 언어가 영어일 때 그 칸을 읽는다. 칸이 비면 한국어 칸을 쓴다(`CsvRow`의 열 읽기). `GameData.Load(read, "en")`이 그 언어로 읽고, 클라이언트는 설정의 [언어]를 바꾸면 시트를 다시 읽는다(`GameRoot.ReloadData`)
- 시트의 지문(`Fingerprint`)은 파일 글 전체로 만들어서 언어와 상관없다. 한국어로 저장한 판을 영어로 이어도 똑같이 돈다(`RunShapeTests`가 확인한다). 규칙은 id로만 움직이고 이름을 보지 않는다
- 영어가 있는 시트: `classes.csv`(name, role, mechanic, blurb), `skills.csv`, `upgrades.csv`, `combos.csv`(name, text), `monsters.csv`(name, text, phase2_line), `monster_skills.csv`(name, text), `items.csv`(name, attack, text), `chapters.csv`(제목과 장소·보스·목표 이름, 약해짐·소환 막힘 글), `stories.csv`(title, text), `renown.csv`(title, text), `perks.csv`, `achievements.csv`(name, text)
- 새 줄을 더할 때 `_en` 칸은 비워 둬도 된다(한국어가 나온다). 영어 글을 쓰면 글꼴을 다시 만들 필요는 없다(ASCII와 “ ” — … 은 들어 있다)
- 옮긴 말: 리엔 Rien, 세른 Sern, 일지 the journal, 균열 the Rift, 봉인석 seal stone, 희망 hope, 결의 resolve, 끈기 grit, 명성 renown. 직업: Knight, Berserker, Hunter, Elementalist, Cleric, Gambler, Rogue, Bard, Alchemist, Necromancer, Seer, Beastmaster, Monk, Engineer, Mnemonist

## 아직 한국어로만 있는 것

| 무엇 | 어디 | 옮기는 법 |
|---|---|---|
| 지도·전투·마을·사건 화면의 나머지 글 | `Client/GameRoot.*.cs` | 한 화면씩 `Strings.Get`으로 바꾸고 시트에 줄을 더한다 |
| 규칙의 안내와 오류(“금화가 모자랍니다”, 전투 기록) | `Core`의 `Adventure`, `Battle` | Core는 문장 대신 사유 코드와 값을 내고, 클라이언트가 시트로 문장을 만든다. 저장 형식과는 상관없다 |
| 사건과 선택지(`events.csv` 182개, `event_choices.csv` 431개), 동료 대화(`talks.csv`), 마을·장소 글 | `Data/Story/*.csv` | 같은 `*_en` 열을 더한다. 양이 가장 많다(약 90KB) |
| 조사(을/를, 이/가) | `Core/Common/Korean.cs` | 영어에서는 쓰지 않는다. 문장 틀을 언어마다 시트에 둔다 |
| 글꼴 | `Resources/Fonts` | 지금 글꼴에 ASCII가 있어 영어는 그대로 나온다 |

## 규칙

- 새 화면의 낱말은 처음부터 `strings.csv`에 넣는다(`ko`는 꼭, `en`은 할 수 있으면)
- id는 `화면.무엇` 꼴(`settings.music`, `tip.map.text`)
- 큰따옴표는 “ ”, 영어의 작은따옴표는 CSV에서 그대로 쓴다
