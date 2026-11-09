using System.Collections.Generic;
using UnityEngine;

namespace Game.Client
{
    // First-time tips (Docs/Design.md 튜토리얼): the first time a screen comes up, a short card says what to do on it.
    // Each is shown once on this device (PlayerPrefs); the title's [도움말 다시 보기] brings them back, and the help
    // spots on the screens (the time, the rift, renown) open theirs again. Words for how to play, not the story:
    // the story lives in the sheets.
    static class Tips
    {
        const string SeenKey = "tips_seen";

        public static readonly Dictionary<string, (string title, string text)> All = new Dictionary<string, (string, string)>
        {
            ["map"] = ("지도", "영웅마다 차례가 온다. [이동 굴리기]로 이동력을 정하고 갈 칸을 눌러 [이동]. 금색 테두리가 갈 수 있는 칸이다. 어두운 칸은 아직 아무도 보지 못한 안개다. 그 안에 무엇이 있는지는 가 보거나 마을에서 소문을 들어야 안다."),
            ["join"] = ("전투가 났다", "전투가 난 칸에서 2칸 안에 있는 영웅만 함께 싸울 수 있다. 싸운 영웅만 경험치를 받는다. 들판에서 만난 무리는 한 번 피해 갈 수 있다(광전사는 겁줘서 쫓을 수도 있다)."),
            ["battle"] = ("전투", "기술을 고르고, 필요하면 대상을 누른 뒤 [굴리기]. 굴리기 전에 성공 개수별 확률과 피해가 보인다. 결의를 쓰면 주사위 하나가 확정 성공이 된다. 실패한 주사위는 끈기로 쌓여 결의가 된다. 버튼을 길게 누르면(마우스는 올리면) 설명이 나온다."),
            ["event"] = ("사건", "선택지를 고르고, 판정이 있으면 누가 할지 고른다. 확률은 미리 보인다. 직업만의 선택지도 있다. 사람들을 도우면 명성이 오르고, 이야기의 결말이 달라진다."),
            ["town"] = ("마을", "여관에서 쉬면 마을에 있는 영웅이 모두 회복한다. 상점의 소모품은 원하는 영웅에게 사 줄 수 있다. 소문은 안개 속을 밝혀 주고, 마을을 도우면 명성이 오른다."),
            ["levelup"] = ("레벨업", "강화 둘 가운데 하나를 고른다. 판마다 다른 강화가 나오니 같은 직업도 다르게 자란다."),
            ["time"] = ("하루와 밤", "세 영웅이 한 번씩 움직이면 시간이 흐른다(아침, 낮, 저녁, 밤). 밤에는 이동력이 1 줄고 시야가 좁다. 새 날이 올 때마다 균열이 하나 자란다."),
            ["rift"] = ("균열", "날마다 하나씩 자라 10이 되면 모험이 끝난다. 6 이상이면 보스가 강해진다. 본 줄거리 장소와 일부 사건이 균열을 줄여 준다."),
            ["renown"] = ("명성", "사람들을 돕고 보스를 쓰러뜨리면 오른다. 명성이 쌓이면 사람들이 너희를 부르는 이름이 바뀐다. 이름 없는 길손에서 영웅까지."),
            ["journal"] = ("일지", "모험이 끝날 때마다 일지 페이지를 받는다. 페이지로 장을 펼치면 다음 모험을 조금 다르게 시작할 수 있다. 길에서 찾은 옛 영웅의 메모도 여기에 모인다."),
        };

        static HashSet<string> _seen;

        static HashSet<string> Seen
        {
            get
            {
                if (_seen != null) return _seen;
                _seen = new HashSet<string>(PlayerPrefs.GetString(SeenKey, "").Split(','));
                return _seen;
            }
        }

        // Whether the tip still has to be shown once; marks it seen.
        public static bool FirstTime(string id)
        {
            if (!All.ContainsKey(id) || Seen.Contains(id)) return false;
            Seen.Add(id);
            PlayerPrefs.SetString(SeenKey, string.Join(",", Seen));
            PlayerPrefs.Save();
            return true;
        }

        public static void Reset()
        {
            Seen.Clear();
            PlayerPrefs.DeleteKey(SeenKey);
            PlayerPrefs.Save();
        }

        // Playtests start with every tip seen, so the cards do not cover what they press.
        public static void MarkAllSeen()
        {
            foreach (string id in All.Keys) Seen.Add(id);
            PlayerPrefs.SetString(SeenKey, string.Join(",", Seen));
            PlayerPrefs.Save();
        }
    }
}
