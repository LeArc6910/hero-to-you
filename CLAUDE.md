# hero-to-you

For the King 느낌의 탑뷰 2D 턴제 협동(1~3인) 모험 RPG다. Unity 6000.6.3f1. 기획과 기술 원칙은 `Docs/Design.md`(개발 가이드라인)에 있고, 방향이 바뀌면 그 문서를 먼저 고친다. 세부는 `Docs/Dice.md`(주사위), `Docs/Combat.md`(전투), `Docs/Classes.md`(직업 15종), `Docs/Narrative.md`(이야기·사건), `Docs/Map.md`(지도·장), `Docs/Saving.md`(저장), `Docs/Journal.md`(판을 넘는 일지), `Docs/Localization.md`(영어판 준비), `Docs/WebDemo.md`(웹 데모), `Docs/WebDemoSetup.md`(사람이 처음 한 번 하는 웹 데모 설정), `Docs/Playtest.md`(클라우드 플레이 시험).

## 작업 분담

- **클라우드 세션**: 코드(`.cs`, `.asmdef`), 데이터(CSV), 테스트, 문서. Unity 에디터 없이 `dotnet`으로 컴파일·테스트한다. 푸시할 때마다 `CLOUD_LOG.md` 맨 위에 항목을 같은 커밋으로 추가한다(무엇을 왜 바꿨는지, 로컬에서 할 일, 비어 있는 로컬 확인 칸).
- **로컬 세션**(Unity 에디터가 있는 컴퓨터): 씬, `.asset`, `.meta`, `Packages/packages-lock.json`, `ProjectSettings/`, 아트·사운드. 시작할 때 `git pull` 후 `CLOUD_LOG.md`에서 로컬 확인이 비어 있는 항목을 읽고 `LOCAL_TASKS.md`를 위에서부터 처리한다.
- **클라우드가 대신 해 보기**: 사람이 "테스트해 줘"라고 하면, 웹 데모 배포를 기다려 `node DevTools/WebPlaytest/playtest.js --wait`로 헤드리스 Chromium에서 PC(마우스)와 휴대폰(터치) 화면으로 플레이하고, 사진과 `report.md`를 열어 본 뒤 결과를 알린다(`Docs/Playtest.md`). 새 화면이나 기능을 만들면 `Client/Playtest.cs`의 명령과 `playtest.js`의 확인도 더한다. 단추는 `Ui.Button`으로 그려야 시험이 진짜 클릭·탭으로 누를 수 있다. 사람에게는 사진으로 알 수 없는 것(손맛, 소리, 실제 휴대폰 감각, 재미)만 묻는다.
- 로컬은 `.cs` 파일을 고치지 않는다. 컴파일 에러나 버그는 결과 보고에 Console 로그를 붙이고, 클라우드가 고친다.
- 클라우드는 에디터 작업이 필요하면 가능한 한 에디터 스크립트(`Client/Editor/ProjectSetup.cs`)로 만들어 웹 빌드와 로컬이 같은 코드를 돌게 한다. 손 작업이 남으면 `LOCAL_TASKS.md`에 적는다.
- 클라우드는 `.meta`를 만들지 않는다. 새 파일의 `.meta`는 로컬이 Unity를 열 때 생기고 로컬이 커밋한다(웹 빌드는 `.meta` 없이도 된다). 단, 클라우드가 파일을 지우거나 옮길 때는 짝이 되는 `.meta`도 같이 지우거나 옮긴다.

## 코드 구조

- `Assets/_Project/Scripts/Core` (`Game.Core`): 게임 규칙과 상태 전부. UnityEngine을 참조하지 않는다(`noEngineReferences`). netstandard2.1, C# 9.
  - `Common/GameRandom.cs`: PCG32. `Sheets/`: CSV 읽기와 시트(`GameData`가 모든 시트를 묶는다. `GameData.Load(read, "en")`은 시트의 `*_en` 칸을 읽는다)
  - `Dice/`: 판정(`Check`), 굴리기 전 확률(`CheckSpec.Odds`), 운명의 흐름(`Fate`)
  - `Heroes/`: 영웅, 능력치, 직업, 레벨
  - `Combat/`: 턴제 전투(`Battle`). 명령(`BattleCommand`)을 받아 상태를 바꾸고 일어난 일(`BattleEvent`) 목록을 쌓는다. 직업 기믹은 `Battle.Classes.cs`
  - `Map/`: 육각 칸(`Hex`), 지도 글자 읽기(`MapDef`), 지금 지도(`WorldMap`: 칸의 내용, 이동 범위)
  - `Adventure/`: 한 판(장을 잇는 캠페인)의 진행(`Adventure`): 장과 이야기·결말·목표(`Adventure.cs`), 지도 채우기·안개·지도 차례·직업 지도 특기·하루와 균열(`.Map.cs`), 합류·피하기·보스·레벨업·상처(`.Battle.cs`), 사건·명성·발자취(`.Events.cs`), 마을·상점·소모품·장비(`.Town.cs`), 던전·의뢰·동료 대화·줄(`.Quests.cs`), 저장과 불러오기(`.Save.cs`, 명령 기록을 다시 돌린다), 판을 넘는 일지(`Journal`), 모험 봇(`AdventureBot`)
- `Assets/_Project/Resources/Data`: 데이터 시트(CSV)와 지도(`Maps/*.txt`). 형식은 `Docs/Classes.md`, `Docs/Combat.md`, `Docs/Narrative.md`, `Docs/Map.md`. 클라이언트는 `Resources.Load`로 바로 읽는다. 시트를 새로 추가하면 `GameData.Files`에 넣는다.
- `Assets/Tests/EditMode` (`Game.Core.Tests`): Core 테스트(NUnit). Unity Test Runner와 `dotnet test`가 같은 파일을 쓴다.
- `Assets/_Project/Scripts/Client` (`Game.Client`): 화면과 입력만. 게임 규칙을 두지 않는다. UI는 IMGUI이고 크기는 `Ui`의 점 단위(`Ui.Width`, `Ui.Height`)로 잡는다. 줄이 바뀔 수 있는 한글은 `Ui.Wrap`. 설명은 `Ui.Tip`(마우스는 가리키기, 휴대폰은 길게 누르기), 처음 보는 화면의 도움말 카드는 `Tips`. 화면의 낱말은 `Strings`(`Resources/Data/Ui/strings.csv`, 한국어·영어)로 쓰고, 설정은 `Settings`(글자 크기, 진동, 언어). 이야기 장면 그림은 `Art/SceneArt.cs`. 그림은 `Art/`에 코드로 그린 도트(`PixelArt`), 효과음과 음악도 코드로 합성한다(`Art/SoundArt.cs`·`Sounds`, `Art/MusicArt.cs`·`Music`). 글꼴은 `Resources/Fonts`의 Noto Sans KR 부분집합. 화면은 `GameRoot`의 부분 파일(`.Menu`, `.Map`, `.Battle`, `.Story`, `.Town`)이고, 모험 명령은 `GameRoot.Act`로 줘서 받아들여지면 기기의 지금 슬롯에 저장한다(`SaveStore`: 슬롯 셋과 연습 판). 입력은 IMGUI 이벤트만 쓴다(마우스·탭 모두 클릭으로 들어온다). `Editor/`에는 프로젝트 설정과 씬을 코드로 만드는 메뉴(`Game > Set Up Project`)와 웹 빌드(`Game > Build Web Demo`).
- `DevTools/`: Unity 없이 Core를 빌드·테스트·시뮬레이션하는 .NET 프로젝트. 소스는 `Assets/`에서 가져다 쓰므로 여기에 게임 코드를 두지 않는다.
  - `Game.Core`: Core만 컴파일한다. 경고도 에러로 친다
  - `Game.Core.Tests`: 테스트
  - `CoreSandbox`: 밸런스 시뮬레이션 콘솔(판정 분포, 봇 파티 전투·모험)
  - `UnityCompileCheck`: Unity 쪽 코드를 Unity 실제 엔진 DLL로 컴파일만 해 본다. asmdef마다 `Assemblies/`에 프로젝트가 하나씩 있다. 패키지 API를 쓰게 되면 `Stubs/<어셈블리 이름>/`에 같은 시그니처로 적는다. 웹 빌드용 확인은 웹 플레이어 DLL(`.unity/WebGL`)로 한다
  - `WebPlaytest`: 웹 데모를 헤드리스 Chromium으로 플레이하는 시험(`Docs/Playtest.md`)
  - `ArtPreview/preview.py`: `Client/Art/PixelArt.cs`의 도트 그림을 PNG 한 장으로 그린다(Unity 없이 그림 확인)
  - `SoundPreview`: `Client/Art/SoundArt.cs`의 효과음과 `MusicArt.cs`의 음악을 WAV로 쓰고 무음·클리핑을 검사한다
  - `Fonts`: 게임 글꼴 부분집합 만들기. `.cs`나 시트에 새 한글을 쓰면 다시 돌린다(없는 글자는 웹에서 빈칸으로 나온다)
- 웹 데모: `.github/workflows/web-demo.yml`이 GameCI로 빌드해 GitHub Pages에 올린다. `Docs/WebDemo.md`.

## 명령

```
# .NET 8 SDK가 없으면(클라우드 세션마다): curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0
dotnet test DevTools/Game.Core.Tests
dotnet run --project DevTools/CoreSandbox -- --dice
dotnet run --project DevTools/CoreSandbox -- --battle 2000 --party knight,hunter,cleric --encounter goblins
dotnet run --project DevTools/CoreSandbox -- --classes 1000
dotnet run --project DevTools/CoreSandbox -- --classes 60 --encounter warden --level 4
dotnet run --project DevTools/CoreSandbox -- --adventure 200 --party berserker,elementalist,gambler   # 캠페인 전체를 봇이
dotnet run --project DevTools/CoreSandbox -- --adventure 60 --party knight,hunter,cleric --human [--easy] [--retries 3] [--chapters 4-5]  # 사람처럼 실수하는 봇(밸런스는 이것으로 잰다). --chapters는 짧은 판
dotnet run --project DevTools/CoreSandbox -- --replay report.txt   # 사람이 보낸 버그 제보(저장본)를 그대로 다시 돌린다
dotnet run --project DevTools/SoundPreview -- /tmp/sounds

# 코드로 그린 도트 그림을 PNG로 보기 (Pillow 필요)
python3 DevTools/ArtPreview/preview.py /tmp/art.png

# 새 글자를 쓰면 글꼴 부분집합을 다시 만든다 (fonttools 필요, 원본 주소는 스크립트 머리말)
python3 DevTools/Fonts/make-korean-font.py NotoSansKR.ttf Assets/_Project/Resources/Fonts/NotoSansKR-Game.ttf

# Unity 쪽 코드 컴파일 확인. fetch는 세션마다 한 번(몇 분, 엔진 DLL만 .unity/에 받는다)
DevTools/UnityCompileCheck/fetch-unity.sh
dotnet build DevTools/UnityCompileCheck
dotnet build DevTools/UnityCompileCheck/Assemblies/Game.Client -p:DefineConstants=UNITY_WEBGL -p:UnityAssemblies=$PWD/DevTools/UnityCompileCheck/.unity/WebGL -o /tmp/webgl-check

# 웹 데모를 헤드리스 Chromium으로 플레이해 보기(사진과 report.md가 DevTools/WebPlaytest/out/에). --wait면 배포를 기다린다
node DevTools/WebPlaytest/playtest.js --wait
```

GitHub Actions(`.github/workflows/core-tests.yml`)가 푸시마다 테스트, 샌드박스 빌드, Unity 쪽 컴파일 확인을 돌린다.

## 규칙

- Core에서는 `System.Random` 대신 `GameRandom`을 쓴다. 같은 시드와 같은 명령이면 어디서나 같은 결과가 나와야 한다(협동의 명령 동기화, 버그 재현). 시각(`DateTime.Now` 등)도 Core 안에서 읽지 않는다. 부동소수점 대신 정수 계산을 쓴다.
- 전투의 모든 주사위는 그 전투의 `Fate`에서 꺼낸다(점성술사의 예지, `Docs/Dice.md`).
- `Adventure`의 공개 명령은 성공하면 `Record`로 한 줄을 남긴다. 저장은 그 기록을 다시 돌리는 것이라(`Docs/Saving.md`), 새 명령을 만들면 `Record`와 `Adventure.Run`에 같이 넣고, 명령 글 형식을 바꾸면 `SaveVersion`을 올린다.
- 게임은 **턴제**다(사용자 결정, 2026-11-09). 지도도 전투도 차례가 있고, Core는 프레임을 모른다.
- 확률은 "선택을 존중하는" 쪽으로 만든다(사용자 요청, 2026-11-09). 굴리기 전에 확률을 보이고, 결의·끈기·바닥으로 운의 몫을 묶는다. 새 판정을 만들 때 `Docs/Dice.md`의 기준을 지키고 샌드박스로 확인한다.
- 직업은 수치만 다르게 만들지 않는다. 고유 기믹, 사건 선택지, 지도 특기, 약점을 같이 만든다(`Docs/Classes.md`).
- 서사는 "옛 영웅의 발자취를 따라 걷다 스스로 영웅이 되어 가는 서사시"다(사용자 결정, 2026-11-09). 사용자가 나중에 이야기 전체를 바꿀 수 있으니, 인물·장소 이름과 이야기 글은 데이터(`Data/Story/`, `chapters.csv`, `renown.csv`)에만 두고 코드(`Core`, `Client`)에 쓰지 않는다(`Docs/Narrative.md` 이야기 바꾸기).
- PC와 모바일 크로스 플랫폼(사용자 목표). 새 기능은 마우스와 터치 둘 다로 쓸 수 있어야 하고, UI는 휴대폰 가로·세로와 작은 PC 창(800×600)에 들어가야 한다. 누르는 단추는 44점 이상.
- 에디터에서 손으로 만드는 콘텐츠를 늘리지 않는다. 직업·몬스터·사건은 데이터, 씬과 설정은 에디터 스크립트로 만든다.
- For the King은 느낌의 기준일 뿐이다. 그 게임의 이름, 인물, 그림, 사건 문장, 수치를 가져오지 않는다.
- 사용자에게는 한국어로 말한다(사용자의 다른 프로젝트에서의 결정을 따른다). 작업 보고, 중간 안내, 마지막 요약까지 모두 한국어로 쓴다. 문서도 한국어로 쓴다.
- 커밋 메시지는 영어로 쓴다. 첫 줄은 명령형 요약, 본문에는 무엇을 왜 바꿨는지 적는다.
- 커밋 작성자는 저장소 주인으로 한다: `git config user.name LeArc6910`, `git config user.email 270705266+LeArc6910@users.noreply.github.com`. 메시지에 `Co-Authored-By`나 세션 링크 같은 도구 표시 줄을 붙이지 않는다(사용자의 다른 프로젝트에서의 결정을 따른다).
- 작업 브랜치: 지금은 `claude/coop-3player-2d-game-qosx9s`(이 저장소의 첫 브랜치라 기본 브랜치이기도 하다). 사람이 `main`을 기본으로 바꾸면 `main`에서 일한다. 웹 데모는 두 브랜치 모두에서 빌드한다.
