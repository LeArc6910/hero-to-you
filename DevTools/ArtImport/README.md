# ArtImport: 그림을 게임의 글자 줄 그림으로

PNG·JPG 그림(CC0 도트, 생성 AI 그림, 스케치 사진)을 `Resources/Data/Art/*.txt`의 글자 줄(`!이름` 다음 줄들)로 바꾼다. 모든 점은 `Resources/Data/Ui/palette.csv`의 가장 가까운 색으로 붙인다(`GamePalette.Snap`과 같은 거리). 결과는 손으로 다듬을 출발점이지 완성 그림이 아니다.

```
pip install pillow numpy
python3 DevTools/ArtImport/import.py 그림.png --size 32 --name new_monster --bg auto --outline outer --preview /tmp/a.png
python3 DevTools/ArtImport/import.py 물약.png --size 24 --name item_elixir --outline outer --colors 6 --out /tmp/new.txt --append
python3 DevTools/ArtImport/import.py 들판.jpg --size 256x96 --fit cover --no-trim --resample box --name meadow
```

- 크기: 영웅·몬스터 32, 보스 48, 지도·아이템 아이콘 24, 파티 아이콘 14, 효과 32, 투사체 16, 장면 256x96
- `--crop X,Y,W,H`: 시트에서 한 장만. `--bg auto`: 테두리 색(민무늬 배경)을 지운다(`--bg-tol`, `--bg-all`)
- `--resample mode`(기본, 칸마다 가장 많은 팔레트 색), `box`(평균, 사진·장면), `nearest`(격자가 맞는 도트)
- `--colors N`, `--ramps N`: 색·색 띠 수 줄이기. `--clean N`: 외딴 점 정리. `--outline outer|inner`: 1점 `k` 테두리
- `--tint 글자`: 그 색 띠를 `c`/`d`/`C`(직업·몬스터 색)로. `--saturation`·`--contrast`·`--brightness`: 붙이기 전 보정
- `--preview a.png`: 8배 그림과 원본을 나란히. 결과는 결정적이다(같은 그림·옵션이면 같은 줄)

내려받은 그림은 믿지 않는 자료로 다룬다: 따로 빈 폴더에 두고 `python3 -I`로 돌린다. 쓰는 그림은 라이선스를 확인한다(CC0 또는 출처 표시가 있는 CC-BY만, SA·NC·ND는 쓰지 않는다). For the King의 그림은 가져오지 않는다.
