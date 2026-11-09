# 웹 데모 설정 가이드 (처음 한 번)

GitHub에 푸시할 때마다 게임이 자동으로 웹으로 빌드되어 https://learc6910.github.io/hero-to-you/ 에 올라가게 하려면, 사람이 GitHub 저장소 설정을 몇 가지 해 줘야 한다. 클라우드 세션에는 저장소 설정을 바꿀 권한이 없다. 이 문서는 그 과정을 처음부터 끝까지 적는다. 빌드가 어떻게 돌아가는지는 `Docs/WebDemo.md`, 클라우드의 자동 플레이 시험은 `Docs/Playtest.md`.

## 한눈에

| 단계 | 어디서 | 하는 일 | 걸리는 시간 |
|---|---|---|---|
| 0 | https://id.unity.com | Unity 계정이 이메일·비밀번호로 로그인되는지 확인 | 2분 |
| 1 | 저장소 Settings > Secrets and variables > Actions | 시크릿 `UNITY_EMAIL`, `UNITY_PASSWORD` 넣기 | 3분 |
| 2 | 저장소 Settings > Pages | Source를 GitHub Actions로 | 1분 |
| 3 | 저장소 Settings > Environments > github-pages | 배포할 수 있는 브랜치 확인 | 1분 |
| 4 | 저장소 Actions 탭 (또는 클라우드에 부탁) | 첫 빌드 돌리기 | 20분 안팎(기다리기만) |
| 5 | 휴대폰 브라우저 | 웹 데모 열어 보기 | |

설정은 처음 한 번만 하면 된다. 그 뒤로는 푸시마다 저절로 빌드·배포된다.

## 지금 상태 (2026-11-10 클라우드가 확인)

- 저장소 `LeArc6910/hero-to-you`는 public이다. GitHub Pages는 public 저장소에서 무료다
- 기본 브랜치는 `claude/coop-3player-2d-game-qosx9s`다. 빈 저장소에 처음 올린 브랜치라 저절로 기본이 됐다
- Pages는 아직 꺼져 있다
- 첫 푸시 때 Web demo 워크플로가 돌았지만 시크릿이 없어 빌드를 건너뛰었다. 실패가 아니라 설계대로다. Actions 탭에 "Web demo skipped" 안내가 남는다

## 0단계: Unity 계정 확인

웹 빌드는 GitHub의 서버 안에서 Unity 에디터를 켜고, 그 Unity를 사용자의 Unity 계정(Unity Personal, 무료)으로 활성화한다. 그래서 그 계정의 이메일과 비밀번호가 필요하다.

1. https://id.unity.com 에서 로그아웃한 뒤 **이메일과 비밀번호로** 다시 로그인해 본다
2. 로그인되면 그 이메일과 비밀번호를 다음 단계에서 쓴다. pray-to의 웹 빌드에 쓰는 계정과 같으면 된다
3. 안 될 때:
   - Google·Apple 같은 **소셜 로그인**으로 만든 계정은 비밀번호가 없다. 로그인 화면의 비밀번호 찾기(Forgot password)로 비밀번호를 만든다
   - **2단계 인증**이 켜져 있으면 서버가 로그인하지 못한다. 2단계 인증이 없는 계정을 쓴다

## 1단계: 시크릿 넣기

시크릿은 GitHub가 암호처럼 보관하는 값이다. 워크플로만 읽을 수 있고, public 저장소여도 아무에게도 보이지 않는다.

1. https://github.com/LeArc6910/hero-to-you/settings/secrets/actions 를 연다
   - 메뉴로 가려면: 저장소 위쪽 **Settings** 탭 → 왼쪽 **Secrets and variables** → **Actions**
   - Settings 탭은 저장소 주인 계정(LeArc6910)으로 로그인해야 보인다
2. **Repository secrets** 칸의 **New repository secret**을 누른다
3. 첫 번째 시크릿
   - Name: `UNITY_EMAIL` (대문자, 밑줄까지 정확히)
   - Secret: Unity 계정 이메일
   - **Add secret**
4. 다시 **New repository secret**을 눌러 두 번째 시크릿
   - Name: `UNITY_PASSWORD`
   - Secret: Unity 계정 비밀번호
   - **Add secret**
5. 목록에 `UNITY_EMAIL`, `UNITY_PASSWORD` 두 줄이 보이면 끝이다

알아 둘 것:

- GitHub는 저장한 시크릿의 **값을 다시 보여 주지 않는다**. 이름과 고친 날짜만 보인다. 값이 틀렸으면 그 줄의 연필 아이콘(Update)으로 새로 넣는다
- 그래서 pray-to에 넣은 값을 복사해 올 수 없다. 시크릿은 저장소마다 따로라서 여기에 다시 입력해야 한다
- 값 앞뒤에 공백이나 줄바꿈이 들어가지 않게 한다. 복사·붙여넣기 할 때 특히 조심한다
- **값은 채팅, 커밋, 문서, 이슈 어디에도 적지 않는다**
- 남이 이 저장소를 fork해서 보낸 pull request의 워크플로는 시크릿을 받지 못한다(GitHub 기본 동작). 그래서 public이어도 안전하다

## 2단계: GitHub Pages 켜기

1. https://github.com/LeArc6910/hero-to-you/settings/pages 를 연다 (Settings → 왼쪽 **Pages**)
2. **Build and deployment** 아래 **Source**를 `Deploy from a branch`에서 **GitHub Actions**로 바꾼다
3. 따로 저장 단추는 없다. 바꾸면 바로 적용된다
4. 아직 아무것도 배포되지 않았으므로 주소는 첫 빌드가 끝난 뒤에 보인다

## 3단계: 배포 브랜치 확인

Pages를 GitHub Actions로 켜면 GitHub가 `github-pages`라는 배포 환경(environment)을 만든다. 이 환경에는 "어느 브랜치에서 배포할 수 있는가" 규칙이 붙고, 보통 **기본 브랜치만** 허용한다.

1. https://github.com/LeArc6910/hero-to-you/settings/environments 를 연다 (Settings → 왼쪽 **Environments**)
2. `github-pages`를 누른다. 2단계를 하기 전이면 아직 없을 수 있다
3. **Deployment branches and tags**를 본다
   - 지금 기본 브랜치가 작업 브랜치(`claude/coop-3player-2d-game-qosx9s`)라서 그대로 두어도 배포된다
   - 나중에 `main`을 만들어 기본 브랜치로 바꾸면 `main`에서 배포된다. 그때도 작업 브랜치에서 배포하고 싶으면 **Add deployment branch or tag rule**로 `claude/*`를 더한다
4. 규칙에 걸리면 배포 단계가 "Branch … is not allowed to deploy to github-pages due to environment protection rules"로 실패한다. 이 단계에서 브랜치를 더하면 된다

## 4단계: 첫 빌드 돌리기

둘 중 하나:

- **클라우드에 부탁**: 채팅에 "웹 빌드 돌려줘"라고 하면 클라우드가 워크플로를 돌리거나 커밋을 올려 빌드를 일으키고 끝까지 지켜본다. 실패하면 로그를 보고 고친다
- **직접**:
  1. https://github.com/LeArc6910/hero-to-you/actions/workflows/web-demo.yml 을 연다 (Actions 탭 → 왼쪽 **Web demo**)
  2. 오른쪽 **Run workflow** → Branch는 `claude/coop-3player-2d-game-qosx9s` → 초록 **Run workflow**
  3. 목록에 새 실행이 생긴다. 눌러 들어가면 job 셋이 차례로 돈다

| job | 하는 일 | 시간 |
|---|---|---|
| `check` | 시크릿이 있는지 본다. 없으면 여기서 멈추고 안내만 남긴다 | 수 초 |
| `build` | Unity 이미지 받기 → Unity 활성화 → 프로젝트 설정·씬 만들기 → WebGL 빌드 → 라이선스 반환 | 첫 번째 20분 안팎, 그다음부터 12분쯤(`Library` 캐시) |
| `deploy` | GitHub Pages에 올린다 | 1분 |

그 뒤로는 따로 할 것이 없다. `main`이나 `claude/…` 브랜치에 코드가 푸시될 때마다 저절로 돈다. 문서(`*.md`, `Docs/`)와 `DevTools/`만 바뀐 푸시는 건너뛴다.

## 5단계: 휴대폰으로 열어 보기

1. 휴대폰 브라우저로 https://learc6910.github.io/hero-to-you/ 를 연다. 처음은 내려받느라 몇 초 걸리고, 두 번째부터는 빨리 뜬다(브라우저 캐시)
2. 가로로 드는 쪽이 편하다(세로도 된다)
3. 타이틀 오른쪽 아래의 7자리 글자가 빌드한 커밋이다. 클라우드가 알려 준 커밋과 같으면 최신이다
4. 조작은 모두 누르기다: [혼자 시작] → 영웅 셋 → 전투에서 기술 → 적을 눌러 대상 → [−]/[+] 결의 → [굴리기]. 자세한 건 `Docs/WebDemo.md`
5. 해 본 느낌은 `LOCAL_TASKS.md`의 "1. 첫 웹 데모 확인"에 적거나 채팅으로 알려 준다

같은 시각에 클라우드는 같은 주소를 헤드리스 브라우저로 열어 PC(1280×720)와 휴대폰(세로 390×844, 가로 844×390) 화면에서 실제 클릭·탭으로 플레이하고 사진을 확인한다(`Docs/Playtest.md`).

## 문제가 생기면

| 보이는 것 | 뜻 | 할 일 |
|---|---|---|
| Actions에 "Web demo skipped" 안내, `build`·`deploy`가 회색(skipped) | 시크릿이 없거나 이름이 다르다 | 1단계. 이름 철자 `UNITY_EMAIL`, `UNITY_PASSWORD` 확인 |
| `build` 로그에 `Failed to login`, `HTTP error code 401` | Unity 로그인 실패 | 0단계 확인 후 1단계에서 값 다시 넣기(공백 주의) |
| `build` 로그에 `serial invalid` | 옛 활성화 방식의 문제. 이 저장소는 pray-to에서 고친 방식을 쓰므로 나오지 않아야 한다 | 클라우드에 알린다 |
| `build`가 `Killed`(종료 코드 137)로 끝남 | 에디터가 시작 직후 꺼지는 GitHub 서버 쪽 일. 스크립트가 세 번까지 다시 띄운다 | 세 번 다 실패하면 실행 화면의 **Re-run failed jobs** |
| `deploy`가 `not allowed to deploy to github-pages` | 배포 브랜치 규칙 | 3단계 |
| 주소가 404 | Pages가 꺼져 있거나 아직 첫 배포 전 | 2단계, 그리고 `deploy` 초록 확인 |
| 페이지에 "불러오지 못했습니다: Unknown data format" | 배포 직후 잠깐 GitHub Pages가 파일 대신 오류 페이지를 준 것(pray-to에서 본 일) | 새로고침. 계속되면 클라우드에 알린다 |
| 한글이 네모나 빈칸으로 나온다 | 글꼴에 없는 글자 | 클라우드에 알린다(글꼴 부분집합을 다시 만든다) |
| 라이선스 오류가 빌드마다 되풀이된다 | 중간에 취소된 빌드가 라이선스를 돌려주지 못했을 수 있다 | Unity 계정 페이지에서 활성화된 기기·좌석을 정리하고 다시 돌린다 |

실패한 실행의 주소(https://github.com/LeArc6910/hero-to-you/actions/runs/…)를 채팅에 붙여 주면 클라우드가 로그를 읽고 고친다.

## 보안 요약

- Unity 비밀번호는 저장소 시크릿에만 있다. 워크플로는 Unity 활성화에만 쓰고 로그에는 `***`로 가려져 찍힌다
- 빌드가 끝나면(성공이든 실패든) 라이선스를 돌려준다(`.github/scripts/build-web.sh`)
- 시크릿 값을 바꾸고 싶으면 1단계 화면의 Update로 덮어쓴다. 지우면 다음 빌드부터 건너뛴다
