using BorderRepair.Inspection;
using UnityEngine;

namespace BorderRepair.EditorTools
{
    /// <summary>用 Unity 基本几何体拼出的原创占位物品。尺寸单位：米；物品正面朝向 -Z（镜头方向）。</summary>
    internal static class PlaceholderItemFactory
    {
        internal delegate Material MaterialLookup(string key);

        public static GameObject BuildCommunicator(MaterialLookup m)
        {
            var root = new GameObject("Item_Communicator");

            Part(root.transform, PrimitiveType.Cube, "Body", new Vector3(0, 0, 0), new Vector3(0.16f, 0.26f, 0.06f), m("plastic_dark"));
            Part(root.transform, PrimitiveType.Cube, "Grip", new Vector3(0, -0.1f, 0.005f), new Vector3(0.17f, 0.07f, 0.065f), m("rubber"));

            var screen = Point(root.transform, "screen", new Vector3(0, 0.06f, -0.031f));
            Part(screen, PrimitiveType.Cube, "Screen", Vector3.zero, new Vector3(0.12f, 0.08f, 0.004f), m("screen"));

            var keypad = new GameObject("Keypad").transform;
            keypad.SetParent(root.transform, false);
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                    Part(keypad, PrimitiveType.Cube, $"Key_{row}{col}", new Vector3(-0.035f + col * 0.035f, -0.01f - row * 0.028f, -0.032f), new Vector3(0.025f, 0.018f, 0.006f), m("metal_light"));

            var battery = Point(root.transform, "battery", new Vector3(0, -0.02f, 0.032f));
            Part(battery, PrimitiveType.Cube, "BatteryCover", Vector3.zero, new Vector3(0.12f, 0.14f, 0.006f), m("plastic_grey"));

            // 天线模块：损坏外观（歪斜、短、发红）与维修后外观（直、完整、带顶帽）。
            var antenna = Point(root.transform, "antenna", new Vector3(0.05f, 0.13f, 0));
            Part(antenna, PrimitiveType.Cylinder, "AntennaBase", new Vector3(0, 0.012f, 0), new Vector3(0.03f, 0.012f, 0.03f), m("metal_dark"));
            var broken = new GameObject("Broken").transform;
            broken.SetParent(antenna, false);
            Part(broken, PrimitiveType.Cylinder, "BrokenRod", new Vector3(0.012f, 0.045f, 0), new Vector3(0.012f, 0.03f, 0.012f), m("damaged"), new Vector3(0, 0, -28f));
            Part(broken, PrimitiveType.Cube, "ExposedWire", new Vector3(0.028f, 0.075f, 0), new Vector3(0.004f, 0.02f, 0.004f), m("copper"), new Vector3(0, 0, -50f));
            var repaired = new GameObject("Repaired").transform;
            repaired.SetParent(antenna, false);
            Part(repaired, PrimitiveType.Cylinder, "NewRod", new Vector3(0, 0.06f, 0), new Vector3(0.012f, 0.04f, 0.012f), m("metal_light"));
            Part(repaired, PrimitiveType.Sphere, "NewTip", new Vector3(0, 0.105f, 0), new Vector3(0.02f, 0.02f, 0.02f), m("rubber"));
            repaired.gameObject.SetActive(false);
            antenna.GetComponent<InspectionPoint>().Configure("antenna", broken.gameObject, repaired.gameObject);

            return root;
        }

        public static GameObject BuildNavBeacon(MaterialLookup m)
        {
            var root = new GameObject("Item_NavBeacon");

            Part(root.transform, PrimitiveType.Cylinder, "Base", new Vector3(0, -0.13f, 0), new Vector3(0.2f, 0.025f, 0.2f), m("metal_dark"));
            Part(root.transform, PrimitiveType.Cylinder, "Body", new Vector3(0, -0.02f, 0), new Vector3(0.14f, 0.09f, 0.14f), m("hazard_yellow"));
            Part(root.transform, PrimitiveType.Cylinder, "Stripe", new Vector3(0, 0.02f, 0), new Vector3(0.145f, 0.012f, 0.145f), m("plastic_dark"));

            var lamp = Point(root.transform, "lamp", new Vector3(0, 0.1f, 0));
            Part(lamp, PrimitiveType.Cylinder, "LampCollar", new Vector3(0, -0.02f, 0), new Vector3(0.1f, 0.01f, 0.1f), m("metal_dark"));
            Part(lamp, PrimitiveType.Sphere, "LampDome", new Vector3(0, 0.02f, 0), new Vector3(0.09f, 0.08f, 0.09f), m("lamp_glass"));

            var mast = Point(root.transform, "mast", new Vector3(0.045f, 0.12f, 0.03f));
            Part(mast, PrimitiveType.Cylinder, "Mast", new Vector3(0, 0.05f, 0), new Vector3(0.008f, 0.06f, 0.008f), m("metal_light"));

            // 被人为改动的痕迹：重贴的检修封签、带划痕的螺丝。
            var seal = Point(root.transform, "seal", new Vector3(0, -0.01f, -0.071f));
            Part(seal, PrimitiveType.Cube, "SealPlate", Vector3.zero, new Vector3(0.05f, 0.06f, 0.004f), m("metal_light"));
            Part(seal, PrimitiveType.Cube, "SealSticker", new Vector3(0.004f, 0, -0.003f), new Vector3(0.042f, 0.014f, 0.002f), m("seal_red"), new Vector3(0, 0, 7f));
            Part(seal, PrimitiveType.Cylinder, "ScratchedScrew", new Vector3(0.018f, 0.022f, -0.004f), new Vector3(0.008f, 0.002f, 0.008f), m("scratched_metal"), new Vector3(90f, 0, 0));
            Part(seal, PrimitiveType.Cylinder, "Screw", new Vector3(-0.018f, -0.022f, -0.004f), new Vector3(0.008f, 0.002f, 0.008f), m("metal_dark"), new Vector3(90f, 0, 0));

            var port = Point(root.transform, "port", new Vector3(0.06f, -0.11f, -0.055f));
            Part(port, PrimitiveType.Cube, "PortHatch", Vector3.zero, new Vector3(0.04f, 0.025f, 0.012f), m("plastic_dark"), new Vector3(0, -45f, 0));
            Part(port, PrimitiveType.Cube, "ExtraChip", new Vector3(-0.004f, 0.004f, -0.008f), new Vector3(0.014f, 0.01f, 0.004f), m("pcb_green"), new Vector3(0, -45f, 0));
            Part(port, PrimitiveType.Cube, "JumperWire", new Vector3(0.01f, -0.006f, -0.008f), new Vector3(0.02f, 0.002f, 0.002f), m("copper"), new Vector3(0, -45f, 20f));

            return root;
        }

        public static GameObject BuildSalvageDrone(MaterialLookup m)
        {
            var root = new GameObject("Item_SalvageDrone");

            Part(root.transform, PrimitiveType.Cube, "Body", Vector3.zero, new Vector3(0.14f, 0.05f, 0.18f), m("plastic_grey"));

            var mainboard = Point(root.transform, "mainboard", new Vector3(0, 0.028f, 0));
            Part(mainboard, PrimitiveType.Cube, "Hatch", Vector3.zero, new Vector3(0.1f, 0.006f, 0.12f), m("corroded"));
            Part(mainboard, PrimitiveType.Cube, "CorrosionStain", new Vector3(0.02f, 0.004f, -0.02f), new Vector3(0.05f, 0.002f, 0.04f), m("rust"));

            string[] names = { "fl", "fr", "bl", "br" };
            Vector3[] dirs = { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 dir = dirs[i].normalized;
                Vector3 tip = dir * 0.19f;
                float yawAngle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                Part(root.transform, PrimitiveType.Cube, $"Arm_{names[i]}", dir * 0.1f, new Vector3(0.018f, 0.014f, 0.14f), m("plastic_dark"), new Vector3(0, yawAngle, 0));

                Transform motorParent = root.transform;
                string motorMat = "metal_dark";
                if (names[i] == "fl")
                {
                    motorParent = Point(root.transform, "motor_fl", tip);
                    tip = Vector3.zero;
                    motorMat = "burnt";
                }
                Part(motorParent, PrimitiveType.Cylinder, $"Motor_{names[i]}", tip + new Vector3(0, 0.012f, 0), new Vector3(0.035f, 0.018f, 0.035f), m(motorMat));

                Transform rotorParent = root.transform;
                Vector3 rotorPos = dir * 0.19f + new Vector3(0, 0.034f, 0);
                if (names[i] == "br")
                {
                    rotorParent = Point(root.transform, "rotor", rotorPos);
                    rotorPos = Vector3.zero;
                }
                Part(rotorParent, PrimitiveType.Cube, $"Rotor_{names[i]}", rotorPos, new Vector3(0.12f, 0.003f, 0.016f), m("rotor"), new Vector3(0, 30f + i * 40f, 0));
            }

            var cam = Point(root.transform, "camera", new Vector3(0, -0.04f, -0.07f));
            Part(cam, PrimitiveType.Cube, "Gimbal", new Vector3(0, 0.012f, 0), new Vector3(0.03f, 0.015f, 0.03f), m("metal_dark"));
            Part(cam, PrimitiveType.Sphere, "CameraBall", new Vector3(0, -0.008f, 0), new Vector3(0.04f, 0.04f, 0.04f), m("plastic_dark"));
            Part(cam, PrimitiveType.Sphere, "Lens", new Vector3(0, -0.01f, -0.018f), new Vector3(0.018f, 0.018f, 0.01f), m("screen"));

            Part(root.transform, PrimitiveType.Cube, "SkidL", new Vector3(-0.05f, -0.05f, 0), new Vector3(0.01f, 0.01f, 0.16f), m("plastic_dark"));
            Part(root.transform, PrimitiveType.Cube, "SkidR", new Vector3(0.05f, -0.05f, 0), new Vector3(0.01f, 0.01f, 0.16f), m("plastic_dark"));

            return root;
        }

        // ---------- helpers ----------

        static Transform Point(Transform parent, string pointId, Vector3 localPosition)
        {
            var go = new GameObject("Point_" + pointId);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.AddComponent<InspectionPoint>().Configure(pointId, null, null);
            return go.transform;
        }

        internal static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 localPosition, Vector3 localScale, Material material, Vector3 localEuler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localEulerAngles = localEuler;
            go.transform.localScale = localScale;
            if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;

            // 圆柱默认胶囊碰撞体与扁平外形不符，改为网格碰撞体，扫描点击更准确。
            if (type == PrimitiveType.Cylinder)
            {
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            }
            return go;
        }
    }
}
