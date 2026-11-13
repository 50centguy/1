using System.Collections.Generic;
using BorderRepair.Data;
using UnityEngine;

namespace BorderRepair.RepairProps
{
    // The visor's pictograms and its few accent colours. Icons are 12 x 12 pixel glyphs drawn here (no art files), white
    // so each use tints them. Colour carries meaning, never decoration:
    //   amber  the frame of the HUD and the step in hand
    //   cyan   the eye looking at something: hover, inspection, analysis of a part
    //   green  done / passed        red  fault / refused
    //   and one colour per tool, the same wherever that tool is named (header, tray mark, cursor, panel button).
    public static class VisorIcons
    {
        public static readonly Color Scan = new Color(.42f, .95f, .92f, 1f);
        public static readonly Color ScanDim = new Color(.42f, .95f, .92f, .45f);
        static readonly Color Driver = new Color(1f, .84f, .22f, 1f), Opener = new Color(1f, .47f, .32f, 1f),
            Tester = new Color(.50f, .88f, 1f, 1f), Hand = new Color(1f, .93f, .82f, 1f);

        static readonly Dictionary<string, string[]> Art = new Dictionary<string, string[]>
        {
            { "screwdriver", new[] { ".........##.", "........###.", ".......###..", "......###...", ".....##.....", "....##......", "...##.......", "..###.......", ".###........", "###.........", "##.........." } },
            { "pry", new[] { "..####......", ".##..##.....", ".#....#.....", "......##....", ".......##...", "........##..", ".........##.", "..........##", "..........##" } },
            { "tester", new[] { ".##########.", ".#........#.", ".#.######.#.", ".#.#....#.#.", ".#.######.#.", ".#........#.", ".#..##....#.", ".#.#..#.#.#.", ".#.#..#...#.", ".#..##..#.#.", ".#........#.", ".##########." } },
            { "loupe", new[] { "...####.....", "..#....#....", ".#......#...", ".#......#...", ".#......#...", ".#......#...", "..#....##...", "...####.##..", ".........##.", "..........##", "...........#" } },
            { "brush", new[] { ".........##.", "........###.", ".......###..", "......###...", ".....###....", "....##......", "..####......", ".#####......", ".####.......", "#.#.#.......", ".#.#........" } },
            { "spray", new[] { "....##......", "...####..#..", "....##..#.#.", "...####..#..", "..######....", "..#....#....", "..#.##.#....", "..#.##.#....", "..#....#....", "..#....#....", "..######...." } },
            { "power", new[] { ".....##.....", ".....##.....", "..#..##..#..", ".##..##..##.", ".#...##...#.", "#....##....#", "#..........#", "#..........#", ".#........#.", ".##......##.", "..########..", "....####...." } },
            { "wave", new[] { "............", "...#........", "..#.#.......", "..#.#.......", ".#...#.....#", ".#...#.....#", "#.....#...#.", "......#...#.", ".......#.#..", ".......#.#..", "........#..." } },
            { "screw", new[] { "..########..", ".#........#.", ".#.######.#.", "..########..", "....####....", ".....##.#...", "....#.##....", ".....##.#...", "....#.##....", ".....##.....", ".....##....." } },
            { "chip", new[] { "..#.#.#.#...", ".#########..", "##.......##.", ".#.......#..", "##..###..##.", ".#..###..#..", "##..###..##.", ".#.......#..", "##.......##.", ".#########..", "..#.#.#.#..." } },
            { "plug", new[] { "...#....#...", "...#....#...", "...#....#...", ".##########.", ".#........#.", ".#........#.", ".#........#.", "..#......#..", "...######...", ".....##.....", ".....##.....", ".....##....." } },
            { "cover", new[] { "............", ".##########.", ".#........#.", ".#.######.#.", ".#........#.", ".#.######.#.", ".#........#.", ".#.######.#.", ".#........#.", ".##########." } },
            { "check", new[] { "............", "..........##", ".........##.", "........##..", ".......##...", "##....##....", ".##..##.....", "..####......", "...##......." } },
            { "cross", new[] { "##........##", ".##......##.", "..##....##..", "...##..##...", "....####....", ".....##.....", "....####....", "...##..##...", "..##....##..", ".##......##.", "##........##" } },
            { "mouse", new[] { "...######...", "..###..#.#..", "..###..#.#..", "..###..#.#..", "..########..", "..#......#..", "..#......#..", "..#......#..", "..#......#..", "..#......#..", "...######..." } },
        };

        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => sprites.Clear();

        public static IEnumerable<string> Names => Art.Keys;

        public static Sprite Get(string name)
        {
            if (name == null || !Art.TryGetValue(name, out var rows)) return null;
            if (sprites.TryGetValue(name, out var cached) && cached != null) return cached;
            const int n = 12;
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "VisorIcon_" + name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[n * n];
            int pad = (n - rows.Length) / 2;   // centre short glyphs vertically
            for (int r = 0; r < rows.Length; r++)
                for (int x = 0; x < n && x < rows[r].Length; x++)
                    if (rows[r][x] == '#') pixels[(n - 1 - (r + pad)) * n + x] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels); texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100);
            sprite.name = "VisorIcon_" + name; sprite.hideFlags = HideFlags.HideAndDontSave;
            sprites[name] = sprite;
            return sprite;
        }

        public static string ToolName(RepairActionType? tool) => !tool.HasValue ? "loupe"
            : tool == RepairActionType.RemoveFastener ? "screwdriver" : tool == RepairActionType.OpenHousing ? "pry" : "tester";

        public static Sprite Tool(RepairActionType? tool) => Get(ToolName(tool));

        public static Color ToolColor(RepairActionType? tool) => !tool.HasValue ? Hand
            : tool == RepairActionType.RemoveFastener ? Driver : tool == RepairActionType.OpenHousing ? Opener : Tester;

        /// <summary>What the command does, as a picture: by its kind first, then by what its id says it touches.</summary>
        public static string CommandName(PropCommand command)
        {
            if (command == null) return null;
            switch (command.kind)
            {
                case PropCommandKind.Power: return "power";
                case PropCommandKind.Fixture: return "plug";
                case PropCommandKind.Test: return "wave";
                case PropCommandKind.Measure: return "tester";
                case PropCommandKind.Inspect: return "loupe";
                case PropCommandKind.Complete: return "check";
            }
            string id = command.id ?? "";
            if (id.Contains("Screw")) return "screw";
            if (id.StartsWith("cover.") || id.StartsWith("strip.")) return "cover";
            if (id.StartsWith("module.")) return "chip";
            if (id.StartsWith("harness.") || id.StartsWith("supply.") || id.StartsWith("fix.") || id == "isolate") return "plug";
            return ToolName(command.tool);
        }

        public static Sprite Command(PropCommand command) => Get(CommandName(command));

        public static Sprite Prop(RepairBenchProp prop) => prop == null ? null
            : prop.IsCleaner ? Get("brush") : prop.IsPainter ? Get("spray") : Tool(prop.Tool);

        public static Color PropColor(RepairBenchProp prop) => prop == null ? Hand
            : prop.IsCleaner ? new Color(1f, .62f, .30f, 1f) : prop.IsPainter ? new Color(1f, .55f, .75f, 1f) : ToolColor(prop.Tool);
    }
}
