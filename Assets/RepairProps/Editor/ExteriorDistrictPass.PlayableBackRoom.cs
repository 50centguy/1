using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.EditorTools
{
    // A back room behind the B1 (Docs/Art/INTERIOR_B1_PASS.md section 25.12): a steel door in the back wall right of
    // UNIT 07's dock (x 1.45..2.35, where the wall was bare), standing open into a small store room the night clerk also
    // sleeps in: steel shelving of stock and parts along the back, a folding cot, a crate for a bedside table, a spare
    // chassis under a tarp, a coat on a hook, one bare bulb. The walls, floor and ceiling are the B1's own pieces again,
    // so they match and collide. The runtime room (ClinicLayout.room) takes the back room in, so the join treats it as
    // indoors (lighting, culling) and the snow stays out.
    public static partial class ExteriorDistrictPass
    {
        const float DoorX0 = 1.45f, DoorX1 = 2.35f, DoorTop = 2.05f;
        static readonly Bounds BackRoomInside = new Bounds(new Vector3(1.45f, 1.22f, 4.0f), new Vector3(4.1f, 2.45f, 2.4f));   // x -.6..3.5, z 2.8..5.2
        static Bounds PlayableRoom { get { var b = B1Room; var r = BackRoomInside; r.Expand(.4f); b.Encapsulate(r); return b; } }

        static void BackRoom(Transform holder)
        {
            var all = holder.GetComponentsInChildren<Transform>(true);
            var shell = all.FirstOrDefault(t => t.name.StartsWith("01 Basement shell", StringComparison.Ordinal));
            Transform Piece(string name) => shell == null ? null : shell.Cast<Transform>().FirstOrDefault(t => t.name == name);
            var back = Piece("Back wall"); var leftWall = Piece("Left wall"); var slab = Piece("Basement concrete slab"); var ceiling = Piece("Ceiling");
            if (back == null || leftWall == null || slab == null || ceiling == null) { log.Append("back room: shell pieces missing\n"); return; }
            var wb = back.GetComponent<Renderer>().bounds;
            var g = new GameObject("Back room - store and night shift (generated)").transform;
            g.SetParent(shell, true);
            g.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // The shell's pieces again, as boxes from min to max.
            GameObject Again(Transform src, string name, Vector3 min, Vector3 max)
            {
                var go = Object.Instantiate(src.gameObject, g);
                go.name = name;
                go.transform.SetPositionAndRotation((min + max) * .5f, Quaternion.identity);
                var ps = g.lossyScale;
                var size = max - min;
                go.transform.localScale = new Vector3(size.x / ps.x, size.y / ps.y, size.z / ps.z);
                return go;
            }
            // The back wall with a door in it.
            Again(back, "Back wall - left of the door", new Vector3(wb.min.x, wb.min.y, wb.min.z), new Vector3(DoorX0, wb.max.y, wb.max.z));
            Again(back, "Back wall - right of the door", new Vector3(DoorX1, wb.min.y, wb.min.z), new Vector3(wb.max.x, wb.max.y, wb.max.z));
            Again(back, "Back wall - over the door", new Vector3(DoorX0, DoorTop, wb.min.z), new Vector3(DoorX1, wb.max.y, wb.max.z));
            back.gameObject.SetActive(false);
            // The room: floor, ceiling, three walls.
            var ins = BackRoomInside; const float t = .18f;
            float z0 = wb.max.z, z1 = ins.max.z, x0 = ins.min.x, x1 = ins.max.x, top = ins.max.y;
            Again(slab, "Back room - floor", new Vector3(x0 - t, -.2f, z0 - .02f), new Vector3(x1 + t, 0, z1 + t));
            Again(ceiling, "Back room - ceiling", new Vector3(x0 - t, top, z0 - .02f), new Vector3(x1 + t, top + .17f, z1 + t));
            Again(leftWall, "Back room - west wall", new Vector3(x0 - t, 0, z0), new Vector3(x0, top, z1 + t));
            Again(leftWall, "Back room - east wall", new Vector3(x1, 0, z0), new Vector3(x1 + t, top, z1 + t));
            Again(leftWall, "Back room - back wall", new Vector3(x0, 0, z1), new Vector3(x1, top, z1 + t));

            var steel = Worn("Back room - dark steel", Hex("#2E3438"), .4f, .6f);
            var doorPaint = Worn("Back room - door, old green-grey paint", Hex("#4F5B55"), .35f, .3f);
            var shelfGrey = Worn("Back room - shelving, grey steel", Hex("#5D6468"), .35f, .5f);
            var card = Lit("Back room - cardboard", Hex("#6B5640"), .2f);
            var cardDark = Lit("Back room - cardboard, older", Hex("#544535"), .2f);
            var bin = Lit("Back room - parts bin, blue-grey plastic", Hex("#3A474F") * 1.3f, .35f);
            var shell07 = Lit("Back room - robot shell, cream", Hex("#C9C1AC"), .45f);
            var mattress = Lit("Back room - mattress, khaki", Hex("#5D5A4C"), .15f);
            var blanket = Lit("Back room - blanket, grey-blue wool", Hex("#4A5560"), .1f);
            var pillow = Lit("Back room - pillow, off-white", Hex("#B7B2A3"), .15f);
            var tarp = Lit("Street - tarp, old blue-grey", Hex("#3B4650"), .45f);
            var coat = Lit("Back room - work coat, olive", Hex("#3F4234"), .15f);
            var wood = Lit("Street - pallet wood, grey", Hex("#5B5348"), .2f);
            var bulbGlass = Lit("Back room - bulb", Hex("#3A3020"), .6f, 0, null, new Color(1f, .72f, .42f) * 1.6f);
            var solid = new List<GameObject>();

            // The door: frame, and the leaf pushed open past square into the room, the way clear.
            var d = Group(g, "Door");
            Box("Door jamb", d, new Vector3(DoorX0 + .03f, DoorTop * .5f, wb.center.z), new Vector3(.06f, DoorTop, wb.size.z + .04f), steel);
            Box("Door jamb", d, new Vector3(DoorX1 - .03f, DoorTop * .5f, wb.center.z), new Vector3(.06f, DoorTop, wb.size.z + .04f), steel);
            Box("Door head", d, new Vector3((DoorX0 + DoorX1) * .5f, DoorTop - .03f, wb.center.z), new Vector3(DoorX1 - DoorX0, .06f, wb.size.z + .04f), steel);
            var hinge = new Vector3(DoorX1 - .07f, 0, wb.max.z + .03f);
            var open = new Vector3(-Mathf.Cos(115 * Mathf.Deg2Rad), 0, Mathf.Sin(115 * Mathf.Deg2Rad));   // past square, the way clear
            var leafRot = Quaternion.LookRotation(Vector3.Cross(open, Vector3.up));
            float leafW = DoorX1 - DoorX0 - .14f;
            var leaf = Box("Door leaf, open", d, hinge + open * (leafW * .5f) + Vector3.up * (DoorTop - .06f) * .5f, new Vector3(leafW, DoorTop - .08f, .045f), doorPaint, leafRot);
            solid.Add(leaf);
            Box("Door handle", d, hinge + open * (leafW - .08f) + Vector3.up * 1.0f + leafRot * new Vector3(0, 0, -.05f), new Vector3(.12f, .025f, .03f), steel, leafRot);
            Box("Door kick plate", d, hinge + open * (leafW * .5f) + Vector3.up * .12f + leafRot * new Vector3(0, 0, -.026f), new Vector3(leafW - .04f, .2f, .006f), steel, leafRot);

            // Shelving along the back wall, west half.
            var sh = Group(g, "Shelving - stock and parts");
            float sz = z1 - .25f, sx0 = x0 + .15f, sx1 = x0 + 2.15f;
            foreach (var x in new[] { sx0, sx1 }) foreach (var z in new[] { sz - .22f, sz + .2f })
                Box("Upright", sh, new Vector3(x, .95f, z), new Vector3(.035f, 1.9f, .035f), shelfGrey);
            float[] levels = { .12f, .6f, 1.08f, 1.56f };
            foreach (var y in levels) Box("Shelf", sh, new Vector3((sx0 + sx1) * .5f, y, sz), new Vector3(sx1 - sx0 + .05f, .025f, .46f), shelfGrey);
            var rnd = new System.Random(11);
            for (int l = 0; l < levels.Length; l++)
            {
                float x = sx0 + .12f;
                while (x < sx1 - .3f)
                {
                    float w = .28f + (float)rnd.NextDouble() * .25f, h = .18f + (float)rnd.NextDouble() * .2f;
                    if (x + w > sx1 - .05f) break;
                    int kind = rnd.Next(5);
                    var c = new Vector3(x + w * .5f, levels[l] + .013f + h * .5f, sz + (float)(rnd.NextDouble() - .5) * .06f);
                    if (kind == 0 && l == 2) { Box("Robot head shell, spare", sh, c + Vector3.up * .04f, new Vector3(.32f, .26f, .28f), shell07); w = .34f; }
                    else if (kind == 1) Box("Parts bin", sh, c, new Vector3(w, Mathf.Min(h, .16f), .36f), bin);
                    else if (kind == 2 && l < 2) Cyl("Cable coil", sh, c, .14f, .1f, steel);
                    else Box("Carton", sh, c, new Vector3(w, h, .38f), rnd.Next(2) == 0 ? card : cardDark, Quaternion.Euler(0, (float)(rnd.NextDouble() - .5) * 8, 0));
                    x += w + .04f;
                }
            }
            var shelfBlock = new GameObject("Shelving - collider") { layer = 0 };
            shelfBlock.transform.SetParent(sh, false);
            shelfBlock.transform.position = new Vector3((sx0 + sx1) * .5f, .95f, sz);
            shelfBlock.AddComponent<BoxCollider>().size = new Vector3(sx1 - sx0 + .1f, 1.9f, .5f);

            // The cot along the east wall, the crate beside it, the bulb over the middle.
            var cot = Group(g, "Cot - the night clerk's");
            var cc = new Vector3(x1 - .48f, 0, z1 - 1.08f);
            Box("Cot frame", cot, cc + Vector3.up * .36f, new Vector3(.74f, .05f, 1.9f), steel);
            foreach (var (dx, dz) in new[] { (-.33f, -.88f), (.33f, -.88f), (-.33f, .88f), (.33f, .88f) })
                Box("Cot leg", cot, cc + new Vector3(dx, .18f, dz), new Vector3(.035f, .36f, .035f), steel);
            Box("Mattress", cot, cc + Vector3.up * .44f, new Vector3(.7f, .1f, 1.85f), mattress);
            Box("Blanket, folded back", cot, cc + new Vector3(0, .51f, -.42f), new Vector3(.72f, .05f, .95f), blanket, Quaternion.Euler(0, 0, -1.5f));
            Box("Blanket, fold", cot, cc + new Vector3(0, .55f, .08f), new Vector3(.72f, .07f, .14f), blanket);
            Box("Pillow", cot, cc + new Vector3(0, .54f, .74f), new Vector3(.48f, .1f, .3f), pillow, Quaternion.Euler(0, 6f, 0));
            var cotBlock = Box("Cot - collider", cot, cc + Vector3.up * .3f, new Vector3(.76f, .6f, 1.92f), steel);
            cotBlock.GetComponent<Renderer>().enabled = false; cotBlock.AddComponent<BoxCollider>();
            var crate = Box("Crate, bedside", cot, new Vector3(x1 - .4f, .22f, z1 - 2.25f + .1f), new Vector3(.44f, .44f, .38f), wood);
            solid.Add(crate);
            Cyl("Thermos", cot, new Vector3(x1 - .48f, .56f, z1 - 2.12f), .045f, .24f, steel);
            Box("Paperback", cot, new Vector3(x1 - .3f, .45f, z1 - 2.2f), new Vector3(.12f, .025f, .18f), cardDark, Quaternion.Euler(0, 20f, 0));
            var bulbAt = new Vector3(ins.center.x - .1f, top - .38f, ins.center.z);
            Rod("Bulb flex", g, new Vector3(bulbAt.x, top, bulbAt.z), bulbAt + Vector3.up * .05f, .006f, steel);
            Sphere("Bulb", g, bulbAt, .045f, bulbGlass);
            var light = new GameObject("Back room bulb (warm)").AddComponent<Light>();
            light.transform.SetParent(g, false);
            light.transform.position = bulbAt + Vector3.down * .08f;
            light.type = LightType.Point; light.color = new Color(1f, .72f, .42f); light.intensity = 1.5f; light.range = 4.2f;
            light.shadows = LightShadows.None;

            // A spare chassis under a tarp in the west corner by the door; a coat on a hook, boots under it.
            var tarpC = new Vector3(x0 + .5f, 0, z0 + .55f);
            solid.Add(Box("Spare chassis under a tarp", g, tarpC + Vector3.up * .48f, new Vector3(.66f, .96f, .58f), tarp, Quaternion.Euler(0, 9f, 0)));
            Box("Tarp, over the top", g, tarpC + Vector3.up * 1.0f, new Vector3(.5f, .14f, .46f), tarp, Quaternion.Euler(4f, 9f, 3f));
            Box("Tarp hem", g, tarpC + Vector3.up * .06f, new Vector3(.76f, .12f, .68f), tarp, Quaternion.Euler(0, 9f, 0));
            Box("Coat hook", g, new Vector3(x0 + .02f, 1.62f, z0 + 1.45f), new Vector3(.06f, .03f, .03f), steel);
            Box("Work coat on the hook", g, new Vector3(x0 + .08f, 1.2f, z0 + 1.45f), new Vector3(.12f, .82f, .42f), coat, Quaternion.Euler(0, 0, -3f));
            Box("Boot", g, new Vector3(x0 + .14f, .07f, z0 + 1.35f), new Vector3(.12f, .14f, .28f), tyreLike());
            Box("Boot", g, new Vector3(x0 + .16f, .07f, z0 + 1.6f), new Vector3(.12f, .14f, .28f), tyreLike(), Quaternion.Euler(0, 12f, 0));
            foreach (var go in solid) go.AddComponent<BoxCollider>();
            log.Append("back room: door x ").Append(DoorX0).Append("..").Append(DoorX1).Append(" to ").Append(DoorTop).Append(" m, room x ")
                .Append(x0).Append("..").Append(x1).Append(" z ").Append(z0.ToString("F2")).Append("..").Append(z1).Append(", ")
                .Append(g.GetComponentsInChildren<Renderer>().Length).Append(" parts, bulb amber 1.5\n");
        }

        static Material tyreLike() => Lit("Street - tyre", Hex("#161819"), .2f);
    }
}
