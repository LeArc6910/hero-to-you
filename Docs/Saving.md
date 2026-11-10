# 저장과 이어하기

코드는 `Scripts/Core/Adventure/Adventure.Save.cs`(저장 글, 불러오기), `Scripts/Client/SaveStore.cs`(기기의 파일), `GameRoot.Act`(명령마다 저장).

## 방식: 명령을 다시 돌린다

Core의 모든 결과는 시드와 명령으로 정해진다(`Docs/Design.md` 7.2). 그래서 저장본은 상태가 아니라 **시드, 파티, 받아들여진 명령의 목록**이다.

```
hero-to-you save 3
seed 638123456789
party knight,hunter,cleric
perks coin_purse,hope_page
level normal
sheets 9f2c41a0
continue
roll
move 4 3
join 0,1
fight 0 attack 3 1
...
```

`perks`는 그 판이 들고 시작한 일지의 장(`Docs/Journal.md`), `level`은 난이도(`easy`, `normal`), `hardcore`가 있으면 하드코어다. `Adventure`의 공개 메서드(Continue, RollMove, Reroll, Move, Camp, Stay, Join, Avoid, Scare, Fight, Choose, Rest, Buy, Sell, Rumor, Donate, TakeContract, Elder, LeaveTown, Equip, Unequip, UseItem, SetRow, Delve, LeaveDungeon, Forge, Recruit, ChooseUpgrade)는 성공할 때마다 한 줄을 `Log`에 쓴다. 불러오기(`Adventure.Load`)는 같은 시드와 파티로 새 모험을 만들고 그 줄들을 차례로 다시 준다. 같은 상태가 된다(`SaveTests`가 확인한다).

- 좋은 점: 저장 형식이 작고 단순하다. 협동의 명령 동기화(M4)와 같은 길이다. 버그를 저장본 하나로 그대로 재현한다
- `sheets`: 데이터 시트의 지문. 저장한 뒤 시트가 바뀌었으면 불러올 때 "결과가 조금 다를 수 있다"고 알린다

## 데이터가 바뀌어도 버티게 (2026-11-10)

개발 중에는 시트를 자주 고친다. 그래도 진행 중인 판을 잃지 않게 두 가지를 한다.

- **난수를 쓰임새마다 따로 뽑는다**: 지도 채우기(장마다), 사건 고르기, 전리품, 레벨업 강화, 상점, 모닥불 대화, 이동 판정, 사건 판정, 피하기 판정이 모두 다른 흐름이고, 전투의 시드는 "몇 번째 전투인가"로만 정한다. 사건을 하나 더하면 사건 고르기만 달라지고, 지도와 전투는 그대로라 저장본의 명령이 계속 맞는다
- **맞는 데까지 이어한다**: 다시 돌리다 어떤 명령이 안 맞으면(그 칸에 갈 수 없다 등) 거절하지 않고 그 앞까지의 상태로 이어하며 "명령 N개 가운데 M개까지만 이어졌다"고 알린다. 첫 명령부터 안 맞을 때와 저장 형식(`SaveVersion`)이 다를 때만 거절한다
- 명령의 글 형식을 바꾸면 `Adventure.SaveVersion`을 올린다(옛 저장본은 거절된다). 2(2026-11-09): `perks` 줄, `buy`에 영웅 번호, 새 명령(reroll, avoid, scare, rumor, donate, use). 3(2026-11-10): 난수 흐름을 나눴다, `level` 줄, 새 명령(row, delve, leavedungeon, contract)
- 일지(`journal.txt`)는 저장본과 따로 있고 판이 끝나도 남는다
- **이 장 처음부터**: `Adventure.ChapterStart`는 장이 시작할 때의 명령 수다. 장에서 지면 저장본을 그 수까지만 잘라(`ChapterStartSave`) 다시 불러온다. 하드코어와 가시밭길에는 없다
- **버그 제보**: 설정의 [버그 제보]가 저장본에 빌드·화면 정보 한 줄을 붙여 복사하거나 파일로 받게 한다. 클라우드는 `dotnet run --project DevTools/CoreSandbox -- --replay report.txt`로 같은 상태를 다시 만든다

## 클라이언트

- 모험을 시작할 때와 명령이 받아들여질 때마다 `persistentDataPath/adventure.txt`에 쓴다. 웹에서는 그 폴더가 사이트의 IndexedDB이고 페이지가 쓸 때마다 옮긴다(`autoSyncPersistentDataPath`, 웹 템플릿)
- 타이틀에 저장본이 있으면 [이어하기]와 파티·진행 줄이 보인다. 이어하면 전투 중이었어도 그 전투의 지금 모습으로 바로 보인다(지난 연출은 건너뛴다)
- 모험이 끝나면(승리·패배) 저장본을 지운다. [새 모험]을 고르면 이어하던 것은 시작할 때 덮어쓴다
- 브라우저의 사이트 데이터를 지우면 저장본도 지워진다
