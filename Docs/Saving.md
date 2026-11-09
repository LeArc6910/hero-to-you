# 저장과 이어하기

코드는 `Scripts/Core/Adventure/Adventure.Save.cs`(저장 글, 불러오기), `Scripts/Client/SaveStore.cs`(기기의 파일), `GameRoot.Act`(명령마다 저장).

## 방식: 명령을 다시 돌린다

Core의 모든 결과는 시드와 명령으로 정해진다(`Docs/Design.md` 7.2). 그래서 저장본은 상태가 아니라 **시드, 파티, 받아들여진 명령의 목록**이다.

```
hero-to-you save 1
seed 638123456789
party knight,hunter,cleric
sheets 9f2c41a0
continue
roll
move 4 3
join 0,1
fight 0 attack 3 1
...
```

`Adventure`의 공개 메서드(Continue, RollMove, Move, Camp, Stay, Join, Fight, Choose, Rest, Buy, Sell, Elder, LeaveTown, Equip, Unequip, ChooseUpgrade)는 성공할 때마다 한 줄을 `Log`에 쓴다. 불러오기(`Adventure.Load`)는 같은 시드와 파티로 새 모험을 만들고 그 줄들을 차례로 다시 준다. 같은 상태가 된다(`SaveTests`가 확인한다).

- 좋은 점: 저장 형식이 작고 단순하다. 협동의 명령 동기화(M4)와 같은 길이다. 버그를 저장본 하나로 그대로 재현한다
- `sheets`: 데이터 시트의 지문. 저장한 뒤 시트가 바뀌었으면 불러올 때 "결과가 조금 다를 수 있다"고 알린다. 다시 돌리다 명령이 안 맞으면(그 칸에 갈 수 없다 등) 저장본을 거절하고 지운다
- 명령의 글 형식을 바꾸면 `Adventure.SaveVersion`을 올린다(옛 저장본은 거절된다)

## 클라이언트

- 모험을 시작할 때와 명령이 받아들여질 때마다 `persistentDataPath/adventure.txt`에 쓴다. 웹에서는 그 폴더가 사이트의 IndexedDB이고 페이지가 쓸 때마다 옮긴다(`autoSyncPersistentDataPath`, 웹 템플릿)
- 타이틀에 저장본이 있으면 [이어하기]와 파티·진행 줄이 보인다. 이어하면 전투 중이었어도 그 전투의 지금 모습으로 바로 보인다(지난 연출은 건너뛴다)
- 모험이 끝나면(승리·패배) 저장본을 지운다. [새 모험]을 고르면 이어하던 것은 시작할 때 덮어쓴다
- 브라우저의 사이트 데이터를 지우면 저장본도 지워진다
