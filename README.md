# hero-to-you (히어로 투 유)

For the King 느낌의 탑뷰 2D 턴제 협동(1~3인) 모험 RPG. Unity 6000.6.3f1. PC와 모바일 출시를 목표로 하고, 개발 중에는 웹 데모로 휴대폰에서 해 본다.

- 개발 가이드라인(기획과 기술 원칙): [`Docs/Design.md`](Docs/Design.md)
- 주사위 규칙 [`Docs/Dice.md`](Docs/Dice.md) · 전투 [`Docs/Combat.md`](Docs/Combat.md) · 직업 12종 [`Docs/Classes.md`](Docs/Classes.md) · 이야기와 사건 [`Docs/Narrative.md`](Docs/Narrative.md)
- 작업 방식과 명령: [`CLAUDE.md`](CLAUDE.md) · 사람이 할 일: [`LOCAL_TASKS.md`](LOCAL_TASKS.md)
- 웹 데모: https://learc6910.github.io/hero-to-you/ (처음 한 번 설정: [`Docs/WebDemoSetup.md`](Docs/WebDemoSetup.md), 빌드 방식: [`Docs/WebDemo.md`](Docs/WebDemo.md))

Unity 없이 게임 규칙을 테스트하려면 .NET 8 SDK를 설치한 뒤:

```
dotnet test DevTools/Game.Core.Tests
dotnet run --project DevTools/CoreSandbox -- --adventure 500 --party knight,hunter,cleric
```
