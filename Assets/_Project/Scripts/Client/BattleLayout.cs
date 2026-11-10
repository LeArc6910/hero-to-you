using System.Collections.Generic;
using UnityEngine;

namespace Game.Client
{
    // Where each unit stands in a battle (user request 2026-11-10: a battlefield seen at an angle, the two sides across
    // a diagonal instead of two columns). The party is near, at the lower left; the foes are farther off at the upper
    // right and smaller with distance. Each side stands in a rank that runs across the line between them, a hero of the
    // front row a step nearer the foes. A unit's label (and a foe's intent) sits above its head, and the columns from
    // label to feet are kept apart so nothing covers a name or a bar; each column is the unit's tap target. Layout only:
    // GameRoot.Battle draws it, and the depth (0 at the horizon, 1 at the field's bottom) sizes the units and the ground.
    static class BattleLayout
    {
        public struct Spot
        {
            public int Id;
            public bool Party;
            public bool Front;
            public bool Big;
            public float LabelW;
            public float LabelH;

            // Set by the layout: the biggest the unit is drawn, so a crowded row keeps its place.
            public float MaxSize;
        }

        public struct Place
        {
            public int Id;
            public bool Party;
            public Vector2 Foot;
            public float Size;
            public Rect Sprite;
            public Rect Label;
            public Rect Column;
        }

        const float Gap = 3;

        // The empty side columns of a 32-point picture: its column is this much narrower each side than the picture.
        const float SpriteInset = 0.12f;

        // The narrowest a label gets in a crowded row (its words shrink to fit, GameRoot.Battle).
        public const float MinLabelW = 56;

        public static float Horizon(Rect field, bool portrait) => field.y + field.height * (portrait ? 0.2f : 0.3f);

        // Where the ground's lines meet on the horizon: behind the foes.
        public static float VanishingX(Rect field, bool portrait) => field.x + field.width * (portrait ? 0.62f : 0.7f);

        public static float Depth(Rect field, bool portrait, float y)
        {
            float horizon = Horizon(field, portrait);
            return Mathf.Clamp01((y - horizon) / Mathf.Max(1, field.yMax - horizon));
        }

        // Nearer is bigger, but the farthest foe keeps most of its size so it stays readable.
        public static float Scale(float depth) => 0.6f + 0.4f * Mathf.Clamp01(depth);

        public static float BaseSize(Rect field, bool portrait)
        {
            float ground = field.yMax - Horizon(field, portrait);
            float size = Mathf.Min(ground * (portrait ? 0.34f : 0.36f), field.width * (portrait ? 0.24f : field.width < 600 ? 0.2f : 0.15f));
            return Mathf.Clamp(size, 36, 120);
        }

        // Each side's part of the field: across (u, 0 left .. 1 right), in depth (t), and how far its rows slope down
        // to the right (a rank facing the other side across the diagonal).
        static (float u0, float u1, float t0, float t1, float slope) Area(bool party, bool portrait) => portrait
            ? party ? (0.02f, 0.98f, 0.7f, 1f, 0.22f) : (0.02f, 0.98f, 0.02f, 0.32f, 0.2f)
            : party ? (0.03f, 0.47f, 0.4f, 1f, 0.45f) : (0.5f, 0.97f, 0.02f, 0.62f, 0.4f);

        public static List<Place> Lay(Rect field, bool portrait, IReadOnlyList<Spot> party, IReadOnlyList<Spot> foes)
        {
            var spots = new List<Spot>();
            var feet = new List<Vector2>();
            Seed(field, portrait, party, true, spots, feet);
            Seed(field, portrait, foes, false, spots, feet);
            float baseSize = BaseSize(field, portrait);

            // Push overlapping columns apart, mostly up and down (the ranks keep their slant), sideways when that is
            // the shorter way; the nearer of two moves down and right, the farther up and left.
            var places = new List<Place>();
            for (int round = 0; round < 60; round++)
            {
                places.Clear();
                for (int i = 0; i < spots.Count; i++) places.Add(Make(field, portrait, baseSize, spots[i], feet[i]));
                bool moved = false;
                for (int i = 0; i < places.Count; i++)
                    for (int j = i + 1; j < places.Count; j++)
                    {
                        var a = places[i].Column;
                        var b = places[j].Column;
                        float ox = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.x, b.x) + Gap;
                        float oy = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.y, b.y) + Gap;
                        if (ox <= 0 || oy <= 0) continue;
                        moved = true;
                        bool jLower = feet[j].y > feet[i].y || (feet[j].y == feet[i].y && j > i);
                        int low = jLower ? j : i, high = jLower ? i : j;
                        if (oy <= ox * 1.4f)
                        {
                            feet[low] += new Vector2(0, oy * 0.55f);
                            feet[high] -= new Vector2(0, oy * 0.55f);
                        }
                        else
                        {
                            int right = feet[j].x >= feet[i].x ? j : i, left = right == j ? i : j;
                            feet[right] += new Vector2(ox * 0.55f, 0);
                            feet[left] -= new Vector2(ox * 0.55f, 0);
                        }
                    }
                // Inside the field: feet above its bottom, labels below its top, columns within its sides.
                for (int i = 0; i < feet.Count; i++) feet[i] = Keep(field, portrait, baseSize, spots[i], feet[i]);
                if (!moved) break;
            }
            places.Clear();
            for (int i = 0; i < spots.Count; i++) places.Add(Make(field, portrait, baseSize, spots[i], feet[i]));
            return places;
        }

        // As many to a row as fit across the side's part (one row when they all fit, the back row first), each row
        // sloping down to the right, a second row set half a place along so the two interleave. A phone held upright
        // has no height for two rows: one row each side, the labels narrower when it is crowded.
        static void Seed(Rect field, bool portrait, IReadOnlyList<Spot> side, bool party, List<Spot> spots, List<Vector2> feet)
        {
            int n = side.Count;
            if (n == 0) return;
            var (u0, u1, t0, t1, slope) = Area(party, portrait);
            float horizon = Horizon(field, portrait);
            float ground = field.yMax - horizon;
            float widest = 0;
            foreach (var spot in side) widest = Mathf.Max(widest, spot.LabelW + Gap);
            float areaW = (u1 - u0) * field.width;
            // At most two rows (there is no height for a third); the labels narrow to fit.
            int perRow = portrait ? n : Mathf.Clamp(Mathf.Max(Mathf.FloorToInt(areaW / Mathf.Max(1, widest)), (n + 1) / 2), 1, n);
            int rows = (n + perRow - 1) / perRow;
            perRow = (n + rows - 1) / rows;
            float labelW = Mathf.Max(MinLabelW, areaW / perRow - Gap);
            // A big one (a chapter's boss) stands in the middle of the front row, where nearness makes it biggest.
            var order = new List<int>();
            for (int i = 0; i < n; i++) if (!side[i].Big) order.Add(i);
            int middle = (rows - 1) * perRow + (n - (rows - 1) * perRow) / 2;
            for (int i = 0; i < n; i++) if (side[i].Big) order.Insert(Mathf.Min(middle, order.Count), i);
            for (int slot = 0; slot < n; slot++)
            {
                int i = order[slot];
                int row = slot / perRow, column = slot % perRow;
                int inRow = Mathf.Min(perRow, n - row * perRow);
                float across = (column + 0.5f + (row % 2 == 1 ? 0.25f : 0) - (rows > 1 && row % 2 == 0 ? 0.25f : 0)) / inRow;
                float rowT = rows == 1 ? (t0 + t1) / 2 : t0 + slope / 2 + (t1 - t0 - slope) * row / (rows - 1);
                var at = new Vector2(u0 + (u1 - u0) * Mathf.Clamp01(across), rowT + slope * (across - 0.5f));
                // The front row a step toward the foes, the back row a step away (Docs/Combat.md 줄).
                if (party) at += side[i].Front ? new Vector2(0.03f, -0.05f) : new Vector2(-0.02f, 0.03f);
                var spot = side[i];
                spot.LabelW = Mathf.Min(spot.LabelW, labelW);
                spot.MaxSize = (areaW / perRow - Gap) / (1 - 2 * SpriteInset) * (spot.Big ? 1.5f : 1);
                spots.Add(spot);
                feet.Add(new Vector2(field.x + field.width * at.x, horizon + ground * Mathf.Clamp01(at.y)));
            }
        }

        static Place Make(Rect field, bool portrait, float baseSize, Spot spot, Vector2 foot)
        {
            float size = baseSize * Scale(Depth(field, portrait, foot.y)) * (spot.Big ? 1.5f : 1);
            if (spot.MaxSize > 0) size = Mathf.Min(size, spot.MaxSize);
            size = Mathf.Max(24, Mathf.Floor(size / 4) * 4);
            var sprite = new Rect(foot.x - size / 2, foot.y - size, size, size);
            // The label rides just above the head (a little into the sprite's empty top rows).
            var label = new Rect(foot.x - spot.LabelW / 2, sprite.y - spot.LabelH + size * 0.06f, spot.LabelW, spot.LabelH);
            float left = Mathf.Min(label.x, sprite.x + size * SpriteInset), right = Mathf.Max(label.xMax, sprite.xMax - size * SpriteInset);
            return new Place
            {
                Id = spot.Id,
                Party = spot.Party,
                Foot = foot,
                Size = size,
                Sprite = sprite,
                Label = label,
                Column = new Rect(left, label.y, right - left, foot.y - label.y),
            };
        }

        static Vector2 Keep(Rect field, bool portrait, float baseSize, Spot spot, Vector2 foot)
        {
            var place = Make(field, portrait, baseSize, spot, foot);
            float top = field.y + 2, bottom = field.yMax - 2;
            if (place.Column.y < top) foot.y += top - place.Column.y;
            if (foot.y > bottom) foot.y = bottom;
            if (place.Column.x < field.x + 2) foot.x += field.x + 2 - place.Column.x;
            if (place.Column.xMax > field.xMax - 2) foot.x -= place.Column.xMax - (field.xMax - 2);
            return foot;
        }
    }
}
