using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>
    /// 程序化高质量素材层。使用 Unity 内置网格生成更完整的家具结构，保留原有 assetId，
    /// 不依赖第三方模型包，适合 Android 沙盒和 PICO 真机运行。后续可用同名 prefab 覆盖。
    /// </summary>
    internal static class SpaceCanvasPremiumAssets
    {
        public static bool TryCreate(string id, Transform parent, Material material, out GameObject result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(id)) return false;
            switch (id)
            {
                case "furniture.sofa": result = Sofa(id, parent, material, 2.25f); return true;
                case "furniture.armchair": result = Sofa(id, parent, material, .95f); return true;
                case "furniture.table": result = Table(id, parent, material, 1.35f, .72f); return true;
                case "furniture.desk": result = Table(id, parent, material, 1.45f, .78f); return true;
                case "furniture.chair": result = Chair(id, parent, material, false); return true;
                case "office.office_chair": result = Chair(id, parent, material, true); return true;
                case "exhibit.stool": result = Stool(id, parent, material); return true;
                case "furniture.tv": result = Screen(id, parent, material, 1.25f); return true;
                case "office.monitor": result = Screen(id, parent, material, .95f); return true;
                case "furniture.lamp": result = Lamp(id, parent, material, false); return true;
                case "exhibit.spotlight": result = Lamp(id, parent, material, true); return true;
                case "decor.plant":
                case "office.plant": result = Plant(id, parent, material); return true;
                case "furniture.shelf":
                case "decor.cabinet": result = Cabinet(id, parent, material); return true;
                case "furniture.bed": result = Bed(id, parent, material); return true;
                case "office.keyboard": result = Keyboard(id, parent, material); return true;
                case "office.mouse": result = Mouse(id, parent, material); return true;
                case "decor.vase": result = Vase(id, parent, material); return true;
                case "decor.rug": result = Rug(id, parent, material); return true;
                case "decor.picture": result = Picture(id, parent, material); return true;
                case "decor.book": result = Book(id, parent, material); return true;
                case "decor.clock": result = Clock(id, parent, material); return true;
                case "exhibit.counter":
                case "kitchen.counter": result = Counter(id, parent, material); return true;
                case "exhibit.backwall":
                case "office.partition": result = Backwall(id, parent, material); return true;
                case "exhibit.banner": result = Banner(id, parent, material); return true;
                case "kitchen.fridge": result = Fridge(id, parent, material); return true;
                case "kitchen.microwave": result = Microwave(id, parent, material); return true;
                case "kitchen.sink": result = Sink(id, parent, material); return true;
                default: return false;
            }
        }

        private static GameObject Root(string id, Transform parent)
        {
            var root = new GameObject(SpaceCanvasBuiltinAssets.DisplayName(id));
            root.transform.SetParent(parent, false);
            root.AddComponent<SpaceCanvasPremiumAssetTag>().revision = SpaceCanvasPremiumAssetTag.CurrentRevision;
            return root;
        }

        private static GameObject Sofa(string id, Transform parent, Material mat, float width)
        {
            var root = Root(id, parent);
            mat = Finish("Fabric", mat);
            Box(root, new Vector3(0, .02f, 0), new Vector3(width, .28f, .82f), mat);
            Box(root, new Vector3(0, .34f, .30f), new Vector3(width - .08f, .58f, .18f), mat);
            Box(root, new Vector3(-width * .5f + .10f, .13f, 0), new Vector3(.18f, .45f, .82f), mat);
            Box(root, new Vector3(width * .5f - .10f, .13f, 0), new Vector3(.18f, .45f, .82f), mat);
            Box(root, new Vector3(-width * .27f, .16f, -.25f), new Vector3(.48f, .18f, .42f), mat);
            Box(root, new Vector3(width * .27f, .16f, -.25f), new Vector3(.48f, .18f, .42f), mat);
            return root;
        }

        private static GameObject Table(string id, Transform parent, Material mat, float width, float depth)
        {
            var root = Root(id, parent);
            var metal = Finish("Graphite", mat);
            mat = Finish("Timber", mat);
            Box(root, new Vector3(0, .58f, 0), new Vector3(width, .10f, depth), mat);
            Box(root, new Vector3(0, .52f, 0), new Vector3(width - .08f, .035f, depth - .08f), metal);
            for (var x = -1; x <= 1; x += 2)
                for (var z = -1; z <= 1; z += 2)
                    Box(root, new Vector3(x * (width * .42f), .26f, z * (depth * .38f)), new Vector3(.075f, .52f, .075f), metal);
            return root;
        }

        private static GameObject Chair(string id, Transform parent, Material mat, bool office)
        {
            var root = Root(id, parent);
            var frame = Finish(office ? "Graphite" : "Timber", mat);
            mat = Finish("Fabric", mat);
            Box(root, new Vector3(0, .46f, 0), new Vector3(.58f, .10f, .56f), mat);
            Box(root, new Vector3(0, .78f, .22f), new Vector3(.58f, .62f, .11f), mat);
            if (office)
            {
                Cylinder(root, new Vector3(0, .22f, 0), .16f, .42f, frame);
                for (var i = 0; i < 5; i++)
                {
                    var angle = i * Mathf.PI * 2f / 5f;
                    Box(root, new Vector3(Mathf.Cos(angle) * .28f, .08f, Mathf.Sin(angle) * .28f), new Vector3(.34f, .045f, .07f), frame);
                }
            }
            else
            {
                for (var x = -1; x <= 1; x += 2)
                    for (var z = -1; z <= 1; z += 2)
                        Box(root, new Vector3(x * .22f, .22f, z * .20f), new Vector3(.06f, .44f, .06f), frame);
            }
            return root;
        }

        private static GameObject Stool(string id, Transform parent, Material mat)
        {
            var root = Root(id, parent);
            Cylinder(root, new Vector3(0, .65f, 0), .34f, .10f, Finish("Timber", mat));
            Cylinder(root, new Vector3(0, .31f, 0), .11f, .58f, Finish("Graphite", mat));
            return root;
        }

        private static GameObject Screen(string id, Transform parent, Material mat, float width)
        {
            var root = Root(id, parent);
            var screen = Finish("Screen", mat);
            mat = Finish("Graphite", mat);
            Box(root, new Vector3(0, .55f, 0), new Vector3(width, .58f, .08f), mat);
            Box(root, new Vector3(0, .20f, 0), new Vector3(.08f, .28f, .08f), mat);
            Box(root, new Vector3(0, .05f, 0), new Vector3(.48f, .05f, .24f), mat);
            Box(root, new Vector3(0, .57f, -.045f), new Vector3(width * .86f, .42f, .015f), screen);
            return root;
        }

        private static GameObject Lamp(string id, Transform parent, Material mat, bool spot)
        {
            var root = Root(id, parent);
            var shade = Finish("Linen", mat);
            mat = Finish("Brass", mat);
            Cylinder(root, new Vector3(0, .07f, 0), .32f, .07f, mat);
            Cylinder(root, new Vector3(0, .60f, 0), .045f, 1.05f, mat);
            if (spot) Cylinder(root, new Vector3(0, 1.12f, 0), .16f, .18f, mat);
            else
            {
                Cylinder(root, new Vector3(0, 1.08f, 0), .27f, .22f, shade);
                Sphere(root, new Vector3(0, 1.07f, 0), new Vector3(.34f, .18f, .34f), shade);
            }
            return root;
        }

        private static GameObject Plant(string id, Transform parent, Material mat)
        {
            var root = Root(id, parent);
            Cylinder(root, new Vector3(0, .12f, 0), .28f, .24f, Finish("Ceramic", mat));
            mat = Finish("Leaf", mat);
            for (var i = 0; i < 7; i++)
            {
                var angle = i * Mathf.PI * 2f / 7f;
                Sphere(root, new Vector3(Mathf.Cos(angle) * .17f, .50f + (i % 2) * .08f, Mathf.Sin(angle) * .17f), new Vector3(.22f, .48f, .11f), mat);
            }
            Sphere(root, new Vector3(0, .76f, 0), new Vector3(.22f, .36f, .22f), mat);
            return root;
        }

        private static GameObject Cabinet(string id, Transform parent, Material mat)
        {
            var root = Root(id, parent);
            var fittings = Finish("Brass", mat);
            mat = Finish("Timber", mat);
            Box(root, new Vector3(0, .82f, 0), new Vector3(1.05f, 1.65f, .42f), mat);
            for (var i = 0; i < 3; i++)
            {
                Box(root, new Vector3(0, .28f + i * .50f, -.225f), new Vector3(.92f, .035f, .02f), fittings);
                for (var side = -1; side <= 1; side += 2)
                    Sphere(root, new Vector3(side * .18f, .28f + i * .50f, -.25f), new Vector3(.035f, .035f, .035f), fittings);
            }
            return root;
        }

        private static GameObject Bed(string id, Transform parent, Material mat)
        {
            var root = Root(id, parent);
            var linen = Finish("Linen", mat);
            mat = Finish("Fabric", mat);
            Box(root, new Vector3(0, .26f, 0), new Vector3(1.65f, .38f, 2.05f), mat);
            Box(root, new Vector3(0, .52f, .88f), new Vector3(1.65f, .75f, .12f), mat);
            Box(root, new Vector3(-.42f, .50f, -.32f), new Vector3(.62f, .12f, .48f), linen);
            Box(root, new Vector3(.42f, .50f, -.32f), new Vector3(.62f, .12f, .48f), linen);
            return root;
        }

        private static GameObject Keyboard(string id, Transform parent, Material mat)
        {
            var root = Root(id, parent);
            Box(root, new Vector3(0, .03f, 0), new Vector3(.76f, .06f, .28f), Finish("Graphite", mat));
            mat = Finish("Linen", mat);
            for (var row = 0; row < 3; row++)
                for (var col = 0; col < 10; col++)
                    Box(root, new Vector3(-.31f + col * .07f, .07f, -.08f + row * .07f), new Vector3(.045f, .025f, .045f), mat);
            return root;
        }

        private static GameObject Mouse(string id, Transform parent, Material mat)
        {
            var root = Root(id, parent);
            Sphere(root, new Vector3(0, .07f, 0), new Vector3(.17f, .10f, .24f), Finish("Graphite", mat));
            Box(root, new Vector3(0, .13f, .03f), new Vector3(.025f, .025f, .06f), Finish("Steel", mat));
            return root;
        }

        private static GameObject Vase(string id, Transform parent, Material mat)
        {
            var root = Root(id, parent);
            mat = Finish("Ceramic", mat);
            Cylinder(root, new Vector3(0, .20f, 0), .24f, .40f, mat);
            Cylinder(root, new Vector3(0, .43f, 0), .16f, .08f, mat);
            return root;
        }

        private static GameObject Rug(string id, Transform parent, Material mat)
        {
            var root = Root(id, parent);
            Box(root, Vector3.zero, new Vector3(2.2f, .035f, 1.45f), Finish("Fabric", mat));
            Box(root, new Vector3(0, .022f, 0), new Vector3(1.95f, .012f, 1.20f), Finish("Linen", mat));
            return root;
        }

        private static GameObject Picture(string id, Transform parent, Material mat)
        {
            var root = Root(id, parent);
            Box(root, Vector3.zero, new Vector3(1.25f, .85f, .08f), Finish("Timber", mat));
            Box(root, new Vector3(0, 0, -.045f), new Vector3(1.08f, .68f, .012f), Finish("Paper", mat));
            return root;
        }

        private static GameObject Counter(string id, Transform parent, Material mat)
        {
            var root = Root(id, parent);
            Box(root, new Vector3(0, .72f, 0), new Vector3(1.45f, .12f, .72f), Finish("Ceramic", mat));
            mat = Finish("Timber", mat);
            Box(root, new Vector3(-.58f, .35f, 0), new Vector3(.12f, .70f, .64f), mat);
            Box(root, new Vector3(.58f, .35f, 0), new Vector3(.12f, .70f, .64f), mat);
            Box(root, new Vector3(0, .22f, 0), new Vector3(1.10f, .07f, .60f), mat);
            return root;
        }

        private static GameObject Book(string id, Transform parent, Material mat)
        {
            var root = Root(id, parent);
            Box(root, Vector3.zero, new Vector3(.43f, .045f, .25f), Finish("Paper", mat));
            var cover = Finish("Fabric", mat);
            Box(root, new Vector3(0, .028f, 0), new Vector3(.46f, .012f, .27f), cover);
            Box(root, new Vector3(0, -.028f, 0), new Vector3(.46f, .012f, .27f), cover);
            return root;
        }

        private static GameObject Clock(string id, Transform parent, Material mat)
        {
            var root = Root(id, parent);
            var face = Part(root, PrimitiveType.Cylinder, Vector3.zero, new Vector3(.4f, .025f, .4f), Finish("Ceramic", mat));
            face.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var hands = Finish("Graphite", mat);
            Box(root, new Vector3(.045f, 0, -.03f), new Vector3(.10f, .012f, .01f), hands);
            Box(root, new Vector3(0, .065f, -.03f), new Vector3(.012f, .14f, .01f), hands);
            return root;
        }

        private static GameObject Backwall(string id, Transform parent, Material mat)
        {
            var root = Root(id, parent);
            Box(root, new Vector3(0, 1.1f, 0), new Vector3(2.2f, 2.2f, .10f), Finish("Linen", mat));
            mat = Finish("Graphite", mat);
            for (var i = -2; i <= 2; i++) Box(root, new Vector3(i * .38f, 1.1f, -.06f), new Vector3(.018f, 2.0f, .012f), mat);
            return root;
        }

        private static GameObject Banner(string id, Transform parent, Material mat)
        {
            var root = Root(id, parent);
            Box(root, new Vector3(0, 1.12f, 0), new Vector3(1.10f, 1.65f, .07f), Finish("Fabric", mat));
            mat = Finish("Steel", mat);
            Cylinder(root, new Vector3(-.47f, .10f, 0), .025f, 2.1f, mat);
            Cylinder(root, new Vector3(.47f, .10f, 0), .025f, 2.1f, mat);
            return root;
        }

        private static GameObject Fridge(string id, Transform parent, Material mat)
        {
            var root = Root(id, parent);
            Box(root, new Vector3(0, .95f, 0), new Vector3(.85f, 1.90f, .72f), Finish("Steel", mat));
            Box(root, new Vector3(0, .96f, -.37f), new Vector3(.72f, .015f, .02f), Finish("Graphite", mat));
            Box(root, new Vector3(.25f, 1.02f, -.40f), new Vector3(.035f, .55f, .025f), Finish("Graphite", mat));
            return root;
        }

        private static GameObject Microwave(string id, Transform parent, Material mat)
        {
            var root = Root(id, parent);
            Box(root, new Vector3(0, .40f, 0), new Vector3(.72f, .48f, .50f), Finish("Steel", mat));
            Box(root, new Vector3(0, .42f, -.26f), new Vector3(.50f, .30f, .015f), Finish("Screen", mat));
            mat = Finish("Graphite", mat);
            for (var i = -1; i <= 1; i++) Sphere(root, new Vector3(.25f, .50f + i * .08f, -.27f), new Vector3(.025f, .025f, .025f), mat);
            return root;
        }

        private static GameObject Sink(string id, Transform parent, Material mat)
        {
            var root = Root(id, parent);
            mat = Finish("Steel", mat);
            Box(root, new Vector3(0, .65f, 0), new Vector3(1.0f, .12f, .65f), mat);
            Box(root, new Vector3(0, .72f, -.02f), new Vector3(.55f, .04f, .35f), mat);
            Cylinder(root, new Vector3(0, .92f, .14f), .035f, .38f, mat);
            return root;
        }

        private static Material Finish(string name, Material fallback)
        {
            // These Resources assets keep the URP shader and authored PBR properties in
            // Android builds. Callers clone them; the shared resources are never modified.
            return Resources.Load<Material>("SpaceCanvasMaterials/Finishes/" + name) ?? fallback;
        }

        private static GameObject Part(GameObject root, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
        {
            var part = GameObject.CreatePrimitive(type);
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = pos;
            part.transform.localScale = scale;
            if (mat != null && part.TryGetComponent<Renderer>(out var renderer)) renderer.sharedMaterial = mat;
            return part;
        }

        private static void Box(GameObject root, Vector3 pos, Vector3 scale, Material mat) => Part(root, PrimitiveType.Cube, pos, scale, mat);
        private static void Sphere(GameObject root, Vector3 pos, Vector3 scale, Material mat) => Part(root, PrimitiveType.Sphere, pos, scale, mat);
        private static void Cylinder(GameObject root, Vector3 pos, float radius, float height, Material mat) => Part(root, PrimitiveType.Cylinder, pos, new Vector3(radius, height * .5f, radius), mat);
    }
}
