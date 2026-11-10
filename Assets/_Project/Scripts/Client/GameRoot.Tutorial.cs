using System.Linq;
using Game.Core;
using UnityEngine;

namespace Game.Client
{
    // The guided practice run (Docs/WebDemo.md 연습 판): a line at the bottom says what to do next and the button to
    // press glows. The step is read from where the adventure is, so it never gets out of step with the game; the
    // rules know nothing of it. Only in the practice slot, until switched off.
    public sealed partial class GameRoot
    {
        const string TutorialKey = "tutorial_off";

        bool TutorialOn => _screen == ClientScreen.Adventure && _adventure != null && SaveStore.Slot == SaveStore.PracticeSlot
            && PlayerPrefs.GetInt(TutorialKey, 0) == 0;

        // The step now (for the playtests too): its id and what should glow.
        internal string TutorialStep { get; private set; } = "";

        // Works out the step and tells Ui what to light up. Called before the screen is drawn.
        void PrepareTutorial()
        {
            Ui.GlowLabel = null;
            Ui.GlowRow = null;
            Ui.GlowRows = null;
            TutorialStep = "";
            if (!TutorialOn || _tipOpen != null) return;
            var a = _adventure;
            string step;
            if (a.LevelUp != null)
            {
                step = "levelup";
                Ui.GlowRow = "강화";
            }
            else if (a.Battle != null && (a.Screen == AdventureScreen.Battle || BattlePlaying))
            {
                if (!AwaitingHero) step = "wait";
                else if (_skill == null)
                {
                    step = "skill";
                    Ui.GlowRow = "기술";
                }
                else if (NeedsTarget(a.Battle) && _target < 0)
                {
                    step = "target";
                    Ui.GlowRow = "대상";
                }
                else
                {
                    step = "roll_battle";
                    Ui.GlowLabel = Strings.Get("battle.roll");
                }
            }
            else
            {
                switch (a.Screen)
                {
                    case AdventureScreen.Story:
                    case AdventureScreen.EventResult:
                    case AdventureScreen.BattleResult:
                        step = a.Screen == AdventureScreen.Story ? "story" : "result";
                        Ui.GlowLabel = Strings.Get("common.continue");
                        break;
                    case AdventureScreen.Map when a.MovePoints < 0:
                        step = "roll";
                        Ui.GlowLabel = Strings.Get("map.roll");
                        break;
                    case AdventureScreen.Map when !_picked.HasValue:
                        step = "pick";
                        Ui.GlowRow = "칸";
                        break;
                    case AdventureScreen.Map:
                        step = "move";
                        Ui.GlowLabel = Strings.Get("map.move");
                        break;
                    case AdventureScreen.Join:
                        step = "join";
                        Ui.GlowLabel = Strings.Get("join.fight");
                        break;
                    case AdventureScreen.Event when _choice < 0:
                        step = "event";
                        Ui.GlowRow = "선택지";
                        break;
                    case AdventureScreen.Event when _chooser < 0:
                        step = "event_hero";
                        Ui.GlowRows = a.Heroes.Select(h => h.Name).ToArray();
                        break;
                    case AdventureScreen.Event:
                        step = "event_roll";
                        Ui.GlowLabel = "굴려 본다";
                        break;
                    case AdventureScreen.Town:
                        step = "town";
                        Ui.GlowLabel = Strings.Get("town.leave");
                        break;
                    default:
                        step = "";
                        break;
                }
            }
            TutorialStep = step;
        }

        // The line at the bottom with [안내 끄기]. Drawn over the screen but under the modal cards.
        void DrawTutorial()
        {
            if (!TutorialOn || TutorialStep.Length == 0 || TutorialStep == "wait" || _tipOpen != null) return;
            string text = Strings.Get("tutorial." + TutorialStep);
            float w = Mathf.Min(560, Ui.Width - 16);
            float textW = w - 104;
            float h = Mathf.Max(48, Ui.TextHeight(text, 12.5f, textW) + 14);
            // Above the footer buttons of the screens (they sit in the last 54 points).
            var r = new Rect((Ui.Width - w) / 2, Ui.Height - 58 - h, w, h);
            if (_adventure.Battle != null && (_adventure.Screen == AdventureScreen.Battle || BattlePlaying)) r.y = 50;
            Ui.Panel(r, new Color(0.10f, 0.08f, 0.16f, 0.94f), Palette.Gold);
            Ui.Text(new Rect(r.x + 10, r.y + 6, textW, h - 12), text, 12.5f, Palette.Ink, TextAnchor.MiddleLeft, false, true);
            if (Ui.Button(new Rect(r.xMax - 92, r.y + (h - 44) / 2, 86, 44), Strings.Get("tutorial.off"), 12, true, Look.Quiet, "안내"))
            {
                PlayerPrefs.SetInt(TutorialKey, 1);
                PlayerPrefs.Save();
            }
        }

        // A new practice run brings the guide back.
        static void TutorialAgain()
        {
            PlayerPrefs.SetInt(TutorialKey, 0);
            PlayerPrefs.Save();
        }
    }
}
