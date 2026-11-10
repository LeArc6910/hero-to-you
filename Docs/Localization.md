# 현지화 (영어판)

출시 목표(Steam·모바일)에 영어판이 있다. 2026-11-10에 세 겹을 모두 영어로 옮겼다: 화면의 낱말(`strings.csv`), 데이터 시트의 이름과 글(`*_en` 열), Core가 쓰는 문장(`Data/Text/*.csv`). 설정의 [언어]가 한국어와 영어를 바꾸고, 바꾸면 시트를 그 언어로 다시 읽는다.

## 화면의 낱말 (`Ui/strings.csv`)

- `id, ko, en`. 클라이언트의 `Strings.Get(id)`, `Strings.Format(id, 값…)`이 읽는다(`Client/Strings.cs`)
- 영어 칸이 비면 한국어가 나오고, 시트에 없는 id는 id 그대로 보여서 빠진 곳이 눈에 띈다
- 단추의 `row`(기술, 대상, 선택지, 칸 …)는 플레이 시험이 단추를 찾는 이름이라 옮기지 않는다

## Core의 문장 (`Data/Text/*.csv`, 2026-11-10)

- 전투 기록과 거절(`battle.csv`), 모험의 보고·거절·목표·시간·불러오기 알림·일지(`adventure.csv`), 마을(`town.csv`), 능력치·땅·상태 이름과 시트를 읽으며 만드는 글(`common.csv`). 열은 `id, ko, en`
- Core는 `Data.Line("battle.turn", 이름)`으로 문장을 만든다(`Common/Lines.cs`). 값 자리는 `{0}`, 한국어 조사는 `{0:이}`(이/가) `{0:을}`(을/를) `{0:은}`(은/는) `{0:과}`(과/와) `{0:으로}`(으로/로) 표시로 값에 맞춰 붙고, 영어에서는 표시가 빠진다. 칸 안의 줄바꿈은 `\n`
- 문장 시트는 시트 지문(`Fingerprint`)에 들지 않는다. 문장을 고쳐도 저장본이 "다른 시트로 저장됐다"고 알리지 않는다
- 시험(`LinesTests`)이 Core 코드가 부르는 id(`.Line("id"` 꼴, 글자 그대로)가 모두 시트에 있는지, 영어와 한국어의 값 자리가 같은지, 영어 칸에 한글이 없는지 본다. 그래서 새 문장은 id를 글자 그대로 쓴다
- 옮기면서 한국어 출력은 바이트까지 같게 했고(봇 모험·전투 수백 판의 모든 글을 비교), 그 뒤 예전 코드가 조사를 고정해서 틀리던 줄 다섯("부활 기도을", "지팡이을", "사냥꾼와" 등)을 조사 표시로 고쳤다
- 시트 작성자를 위한 시트 오류 글(`GameData`의 `시트 오류`)과 지도 읽기 오류는 한국어로 둔다

## 데이터의 영어 (`*_en` 열, 2026-11-10)

- 시트의 아무 열 `X` 옆에 `X_en` 열을 두면, 언어가 영어일 때 그 칸을 읽는다. 칸이 비면 한국어 칸을 쓴다(`CsvRow`의 열 읽기). `GameData.Load(read, "en")`이 그 언어로 읽고, 클라이언트는 설정의 [언어]를 바꾸면 시트를 다시 읽는다(`GameRoot.ReloadData`)
- 시트의 지문(`Fingerprint`)은 파일 글 전체로 만들어서 언어와 상관없다. 한국어로 저장한 판을 영어로 이어도 똑같이 돈다(`RunShapeTests`가 확인한다). 규칙은 id로만 움직이고 이름을 보지 않는다
- 영어가 있는 시트: `classes.csv`(name, role, mechanic, blurb), `skills.csv`, `upgrades.csv`, `combos.csv`(name, text), `monsters.csv`(name, text, phase2_line), `monster_skills.csv`(name, text), `items.csv`(name, attack, text), `chapters.csv`(제목, 마을, 장소·던전·보스 이름, 목표, 약해짐·소환 막힘 글), `stories.csv`(title, text), `renown.csv`(title, text), `perks.csv`, `achievements.csv`(name, text), `events.csv`(title, text, journal), `event_choices.csv`(label, success_text, fail_text), `talks.csv`(lines: 말하는 직업과 `|`는 그대로)
- 새 줄을 더할 때 `_en` 칸은 비워 둬도 된다(한국어가 나온다). 영어 글을 쓰면 글꼴을 다시 만들 필요는 없다(ASCII와 “ ” — … 은 들어 있다)
- 옮긴 말: 리엔 Rien(대명사를 쓰지 않는다), 세른 Sern, 칼라 Kalla(기사단장 Commander Kalla), 마렌 Maren, 마을: Woodsedge Village, Tiderock Village, Foothill Village, Oasis Village, The Last Village, 명성 단계: Nameless Wayfarers, Bearers of the Journal, Sealkeepers, Names in the Songs, Heroes, 확정 성공 sure success, 바닥 floor, 대기 cooldown, 일지 the journal, 균열 the Rift, 봉인석 seal stone, 희망 hope, 결의 resolve, 끈기 grit, 명성 renown. 직업: Knight, Berserker, Hunter, Elementalist, Cleric, Gambler, Rogue, Bard, Alchemist, Necromancer, Seer, Beastmaster, Monk, Engineer, Mnemonist

## 아직 남은 것

| 무엇 | 어디 | 옮기는 법 |
|---|---|---|
| 시트 작성자용 오류 글 | `GameData`, `WorldMap` | 플레이어가 보지 않으므로 한국어로 둔다 |
| 영어 글의 손질 | 모든 `*_en` | 사람이 영어판을 해 보며 어색한 곳을 고친다(번역은 2026-11-10에 한 번에 채웠다) |
| 글꼴 | `Resources/Fonts` | 지금 글꼴에 ASCII와 “ ” — … × − 가 있어 영어는 그대로 나온다 |

## 규칙

- 새 화면의 낱말은 처음부터 `strings.csv`에 넣는다(`ko`는 꼭, `en`은 할 수 있으면)
- id는 `화면.무엇` 꼴(`settings.music`, `tip.map.text`)
- 큰따옴표는 “ ”, 영어의 작은따옴표는 CSV에서 그대로 쓴다
