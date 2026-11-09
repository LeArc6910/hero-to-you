# 웹 데모

작업 중인 최신 상태를 브라우저(휴대폰 포함)에서 바로 해 볼 수 있게 하는 빌드와 배포다. 주소는 https://learc6910.github.io/hero-to-you/ 이다(사람 설정이 끝나고 첫 배포 뒤). 사람이 처음 한 번 하는 설정(시크릿, Pages, 배포 브랜치)은 `Docs/WebDemoSetup.md`에 단계별로 있다.

## 흐름

1. `main`이나 `claude/…` 브랜치에 푸시하면 `.github/workflows/web-demo.yml`이 돈다. 문서(`*.md`, `Docs/`)와 `DevTools/`만 바뀐 푸시는 건너뛴다. Actions 탭에서 손으로 돌릴 수도 있다(Run workflow)
2. 시크릿 `UNITY_EMAIL`, `UNITY_PASSWORD`가 없으면 빌드를 건너뛰고 안내만 남긴다(실패로 표시하지 않는다)
3. GameCI의 Unity 이미지(`unityci/editor:ubuntu-<ProjectVersion의 버전>-webgl-3`) 안에서 `.github/scripts/build-web.sh`가 돈다
   - **라이선스**: 에디터에 들어 있는 Licensing Client로 Unity Personal을 활성화하고(`--activate-ulf --include-personal`, 시리얼 없음) 빌드가 어떻게 끝나든 돌려준다. 계정은 소셜 로그인이나 2단계 인증 없이 이메일·비밀번호로 로그인할 수 있어야 한다. 사용자의 다른 프로젝트(pray-to)에서 2026-10-28에 맞춘 방식을 그대로 가져왔다
   - **빌드**: `Game.Client.Editor.WebBuild.RunBatch`. 먼저 `Game > Set Up Project`(`ProjectSetup`)를 해서 설정과 `Main` 씬을 코드로 만들고, 커밋 해시를 `Resources/BuildInfo.txt`에 써서(타이틀 오른쪽 아래에 보인다) WebGL로 `build/web`에 빌드한다. 저장소에는 `ProjectSettings/ProjectVersion.txt`와 `Packages/manifest.json`, 코드와 데이터만 있어도 빌드가 된다(에디터에서 손으로 만든 것이 없다)
4. `version.txt`(커밋)를 넣어 GitHub Pages에 올린다. 클라우드의 플레이 시험이 이것을 보고 배포를 기다린다(`Docs/Playtest.md`)

## 휴대폰에서 해 보기

- 위 주소를 휴대폰 브라우저로 연다. **가로로 들고** 하는 쪽이 편하다(세로도 된다)
- 모든 조작은 누르기다
  - **지도**: [이동 굴리기] → 금색 테두리 칸 하나를 누르면 오른쪽(세로면 아래) 패널에 그 칸의 내용과 비용 → [이동]. 굴리기 전에 [야영], 굴린 뒤 [여기 머물기]. 지도가 화면보다 크면 끌어서 옮긴다. [장비·가방]으로 장비를 바꾼다
  - **전투**: 기술 단추 → 적이나 아군을 눌러 대상 → [−]/[+]로 결의 → [굴리기]
  - **합류**: 함께 싸울 영웅을 켜고 끈 뒤 [싸운다]
  - **레벨업**: 강화 카드 둘 가운데 하나를 누른다
- 확률 막대: 성공 수(0개~전부)마다 나올 확률과 그때의 피해·회복. 금색이 완벽, 회색이 0개
- 주사위 칸의 숫자가 확률보다 작으면 성공(초록), 금색 ★은 확정 성공(결의·표식·협공·축복·공명·기습)
- 점성술사가 있으면 전투 패널에 "다음 주사위: 12 · 87 · 45"가 보인다. 다음에 굴릴 주사위의 수다
- 명령마다 기기에 저장된다. 다시 열면 타이틀의 [이어하기](`Docs/Saving.md`)
- 타이틀의 [전투 연출: 보통/빠르게], [소리: 켬/끔]

## 웹 빌드 설정 (`Editor/ProjectSetup.cs`)

- gzip 압축 + decompression fallback(GitHub Pages가 `Content-Encoding`을 붙이지 않으므로 Unity 로더가 푼다), 데이터 캐싱, 시작 화면 끔, IL2CPP 크기 우선
- 페이지는 `Assets/WebGLTemplates/Fit`: PC에서도 창을 꽉 채운다. 마지막 입력이 손가락인지(`window.heroLastPointer`) 적어 둬 게임이 터치 배치를 고른다(`Client/WebBridge.cs`)
- 화면 밀도: 브라우저는 기기의 실제 픽셀로 그린다(휴대폰은 점 하나에 3픽셀쯤). `Ui`가 `devicePixelRatio`를 읽어 점 단위로 배치하고, 글자는 실제 픽셀 크기로 그려 흐리지 않다. 큰 모니터에서는 최대 2배까지 키운다
- 글꼴: 웹은 시스템 글꼴을 못 쓰므로 Noto Sans KR(SIL OFL 1.1, `Docs/Licenses/NotoSansKR-OFL.txt`) Medium의 부분집합(약 520 KB, KS X 1001 한글 2,350자 + 코드와 시트에 쓰인 모든 글자)을 넣었다. 새 글자를 쓰면 `DevTools/Fonts/make-korean-font.py`로 다시 만든다(원본은 스크립트 머리말의 주소)
- 렌더 파이프라인은 기본(Built-in). 입력은 IMGUI 이벤트만 쓴다(마우스 클릭과 탭이 같은 길로 들어온다)

## 아직 확인하지 못한 것

클라우드에서는 실제 기기로 돌려 볼 수 없다. 첫 배포 뒤 휴대폰에서 본다.

- 탭이 단추에 바로 반응하는지, 두 번 눌리지 않는지
- 글자 크기와 화면 배치(세로·가로)
- 주사위 연출의 속도와 손맛
