using System.Collections.Generic;
using UnityEngine;

namespace SmashGame
{
    public enum Theme { Grass, Winter, Desert }

    public class LevelInfo
    {
        public int level;
        public Theme theme;
        public int startBalls;
        public bool hard;
        public float pedestalTop;
        public List<Block> blocks = new();
        public string structureName;
    }

    /// <summary>
    /// 레벨을 절차적으로 생성한다. 레벨 번호가 시드이므로 같은 레벨은 항상 같은 구조물이다.
    /// 소재 9종·받침대 1~3개·장애물 2종·테마 3종 조합(역기획서 3장)을 코드로 옮겼다.
    /// </summary>
    public static class LevelBuilder
    {
        public const float PedestalTop = 2.0f;

        /// <summary>공이 받침대에 걸리지 않도록 무시할 콜라이더 목록 (레퍼런스처럼 공은 블록만 맞힌다)</summary>
        public static readonly List<Collider> PedestalColliders = new();

        struct Palette
        {
            public Color sky, ground, pedestal, a, b, c, d;
        }

        static Palette GetPalette(Theme t) => t switch
        {
            Theme.Winter => new Palette
            {
                sky = new Color(0.62f, 0.82f, 0.98f), ground = new Color(0.72f, 0.88f, 0.98f), pedestal = new Color(0.5f, 0.25f, 0.7f),
                a = new Color(0.9f, 0.2f, 0.3f), b = new Color(0.25f, 0.5f, 0.95f), c = new Color(0.95f, 0.35f, 0.75f), d = new Color(0.98f, 0.98f, 1f)
            },
            Theme.Desert => new Palette
            {
                sky = new Color(0.45f, 0.75f, 0.98f), ground = new Color(0.93f, 0.72f, 0.42f), pedestal = new Color(0.5f, 0.25f, 0.7f),
                a = new Color(0.98f, 0.55f, 0.15f), b = new Color(0.3f, 0.7f, 0.95f), c = new Color(0.95f, 0.3f, 0.7f), d = new Color(0.6f, 0.35f, 0.85f)
            },
            _ => new Palette
            {
                sky = new Color(0.45f, 0.72f, 0.98f), ground = new Color(0.45f, 0.78f, 0.3f), pedestal = new Color(0.5f, 0.25f, 0.7f),
                a = new Color(0.95f, 0.8f, 0.15f), b = new Color(0.55f, 0.25f, 0.85f), c = new Color(0.9f, 0.2f, 0.25f), d = new Color(0.25f, 0.45f, 0.9f)
            },
        };

        public static Theme ThemeFor(int level)
        {
            int r = (level * 7 + 3) % 10;
            if (r < 6) return Theme.Grass;
            if (r < 9) return Theme.Winter;
            return Theme.Desert;
        }

        // ---------------- 환경 ----------------

        public static void BuildEnvironment(Transform root, Camera cam, Theme theme)
        {
            var p = GetPalette(theme);
            PedestalColliders.Clear();
            cam.backgroundColor = p.sky;
            cam.transform.position = GameManager.CamDefaultPos;
            cam.transform.rotation = GameManager.CamDefaultRot;

            // 하늘 그림판·질감 바닥·나무/바위 장식 (전부 콜라이더 없음)
            BackgroundArt.Build(root, theme);

            // 멀리 보이는 "성" 실루엣 (장식)
            var castle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            castle.name = "CastleDeco";
            Object.DestroyImmediate(castle.GetComponent<Collider>());
            castle.transform.SetParent(root);
            castle.transform.position = new Vector3(0f, 1.4f, 36f);
            castle.transform.localScale = new Vector3(10f, 6f, 3f);
            castle.GetComponent<Renderer>().material = Materials.Get(theme == Theme.Desert ? new Color(0.85f, 0.7f, 0.45f) : new Color(0.75f, 0.7f, 0.9f));
            for (int i = -1; i <= 1; i += 2)
            {
                var tower = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(tower.GetComponent<Collider>());
                tower.transform.SetParent(root);
                tower.transform.position = new Vector3(i * 5f, 3.5f, 36f);
                tower.transform.localScale = new Vector3(2f, 5f, 2f);
                tower.GetComponent<Renderer>().material = Materials.Get(new Color(0.55f, 0.35f, 0.8f));
            }
        }

        static GameObject Deco(PrimitiveType prim, Transform root, string name, Vector3 pos, Vector3 scale, Material mat, float bevel)
        {
            var go = GameObject.CreatePrimitive(prim);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().material = mat;
            if (bevel > 0f) RoundedMesh.Apply(go, bevel);
            return go;
        }

        static void Pedestal(Transform root, Vector3 center, float radius, Palette p, bool square = false)
        {
            var gold = Materials.Get(new Color(1f, 0.78f, 0.25f), true, true);
            var purpleDark = Materials.Get(p.pedestal * 0.75f, true);

            // 상판(콜라이더 있음) — 위치·두께는 물리와 맞물려 있으므로 유지
            var top = GameObject.CreatePrimitive(square ? PrimitiveType.Cube : PrimitiveType.Cylinder);
            top.name = "PedestalTop";
            top.transform.SetParent(root);
            top.transform.position = new Vector3(center.x, PedestalTop - 0.08f, center.z);
            top.transform.localScale = square ? new Vector3(radius * 2f, 0.16f, radius * 1.4f) : new Vector3(radius * 2f, 0.08f, radius * 2f);
            top.GetComponent<Renderer>().material = Materials.Get(p.pedestal, true);
            FlattenCollider(top, false);
            RoundedMesh.Apply(top, 0.03f);
            PedestalColliders.Add(top.GetComponent<Collider>());

            // 상판 아래 금색 테두리 + 진한 보라 밑판(두께감)
            float ringY = PedestalTop - 0.16f - 0.03f;
            if (square)
            {
                Deco(PrimitiveType.Cube, root, "PedestalRim", new Vector3(center.x, ringY, center.z), new Vector3(radius * 2f + 0.06f, 0.06f, radius * 1.4f + 0.06f), gold, 0.02f);
                Deco(PrimitiveType.Cube, root, "PedestalUnder", new Vector3(center.x, ringY - 0.09f, center.z), new Vector3(radius * 2f - 0.1f, 0.12f, radius * 1.4f - 0.1f), purpleDark, 0.03f);
            }
            else
            {
                Deco(PrimitiveType.Cylinder, root, "PedestalRim", new Vector3(center.x, ringY, center.z), new Vector3(radius * 2f + 0.06f, 0.03f, radius * 2f + 0.06f), gold, 0.02f);
                Deco(PrimitiveType.Cylinder, root, "PedestalUnder", new Vector3(center.x, ringY - 0.09f, center.z), new Vector3(radius * 2f - 0.1f, 0.06f, radius * 2f - 0.1f), purpleDark, 0.03f);
            }

            // 기둥(콜라이더 있음): 보라색 본체 + 위아래 금색 링
            float colTop = PedestalTop - 0.16f, colBottom = -1.5f;
            var col = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            col.name = "PedestalColumn";
            col.transform.SetParent(root);
            col.transform.position = new Vector3(center.x, (colTop + colBottom) * 0.5f, center.z);
            col.transform.localScale = new Vector3(0.42f, (colTop - colBottom) * 0.5f, 0.42f);
            col.GetComponent<Renderer>().material = Materials.Get(p.pedestal, true);
            RoundedMesh.Apply(col, 0.04f);
            PedestalColliders.Add(col.GetComponent<Collider>());
            Deco(PrimitiveType.Cylinder, root, "ColumnCap", new Vector3(center.x, colTop - 0.22f, center.z), new Vector3(0.56f, 0.06f, 0.56f), gold, 0.02f);
            Deco(PrimitiveType.Cylinder, root, "ColumnBase", new Vector3(center.x, -1.05f, center.z), new Vector3(0.56f, 0.06f, 0.56f), gold, 0.02f);
            // 기둥 세로 홈 느낌의 얇은 금색 줄 4개
            for (int k = 0; k < 4; k++)
            {
                float a = k * 90f * Mathf.Deg2Rad;
                Deco(PrimitiveType.Cube, root, "ColumnStripe", new Vector3(center.x + Mathf.Cos(a) * 0.2f, (colTop + colBottom) * 0.5f - 0.1f, center.z + Mathf.Sin(a) * 0.2f),
                    new Vector3(0.05f, (colTop - colBottom) - 0.7f, 0.05f), gold, 0.01f);
            }

            // 받침 발: 넓은 둥근 판 두 장
            Deco(PrimitiveType.Cylinder, root, "PedestalFoot", new Vector3(center.x, -1.3f, center.z), new Vector3(1.2f, 0.12f, 1.2f), Materials.Get(p.pedestal, true), 0.06f);
            Deco(PrimitiveType.Cylinder, root, "PedestalFoot2", new Vector3(center.x, -1.45f, center.z), new Vector3(1.6f, 0.08f, 1.6f), purpleDark, 0.05f);
        }

        /// <summary>Unity의 Cylinder 프리미티브는 캡슐 콜라이더라 윗면이 둥글다. 메시 콜라이더로 바꿔 평평하게 만든다.</summary>
        static void FlattenCollider(GameObject go, bool convex)
        {
            var cap = go.GetComponent<Collider>();
            if (cap != null) Object.DestroyImmediate(cap);
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            mc.convex = convex;
        }

        // ---------------- 블록 생성 ----------------

        public const float BlockBevel = 0.035f; // 블록 모서리 라운딩 반지름(월드 단위)

        static Block MakeBlock(Transform root, PrimitiveType prim, BlockKind kind, Vector3 pos, Vector3 scale, Quaternion rot, Color color, float mass, List<Block> list)
        {
            var go = GameObject.CreatePrimitive(prim);
            go.name = kind.ToString();
            go.transform.SetParent(root);
            go.transform.position = pos;
            go.transform.rotation = rot;
            go.transform.localScale = scale;
            if (prim == PrimitiveType.Cylinder) FlattenCollider(go, true); // 캡슐 → 원기둥 (윗면이 평평해야 쌓인다)
            RoundedMesh.Apply(go, BlockBevel); // 보이는 메시만 둥근 모서리로 (콜라이더는 각진 원본 유지)
            var b = go.AddComponent<Block>();
            b.fallY = PedestalTop - 1.0f;
            b.Setup(kind, color, mass, 1);
            list.Add(b);
            return b;
        }

        static float MassFor(BlockKind k) => k switch
        {
            BlockKind.Stone => 3.0f,
            BlockKind.Log => 1.6f,
            BlockKind.Crate => 1.2f,
            BlockKind.Plank => 1.0f,
            BlockKind.Ice => 0.8f,
            BlockKind.Candy => 0.7f,
            _ => 1.0f,
        };

        // ---------------- 메인 빌드 ----------------

        public static LevelInfo Build(int level, Transform root, SaveData data, Camera cam)
        {
            var info = new LevelInfo { level = level, theme = ThemeFor(level), hard = Balance.IsHardLevel(level), pedestalTop = PedestalTop };
            var rng = new System.Random(level * 7919 + 13);
            var p = GetPalette(info.theme);
            BuildEnvironment(root, cam, info.theme);

            int type = info.hard ? (level / 10) % 6 : (level * 3 + rng.Next(0, 2)) % 6;
            switch (type)
            {
                case 0: BuildCylinderCluster(root, rng, p, info); break;
                case 1: BuildCubeGrid(root, rng, p, info); break;
                case 2: BuildFrameShelf(root, rng, p, info); break;
                case 3: BuildLogTower(root, rng, p, info); break;
                case 4: BuildIceWall(root, rng, p, info); break;
                default: BuildMultiPedestal(root, rng, p, info); break;
            }

            // 왕관 블록 (퍼펙트 히트 타겟) — 레벨 12부터, 아래쪽 절반에서 1~2개
            if (level >= Balance.CrownUnlockLevel && info.blocks.Count > 3)
            {
                var candidates = new List<Block>();
                float midY = 0;
                foreach (var b in info.blocks) midY += b.transform.position.y;
                midY /= info.blocks.Count;
                foreach (var b in info.blocks) if (b.transform.position.y <= midY && b.kind != BlockKind.Ice) candidates.Add(b);
                int crowns = candidates.Count >= 8 ? 2 : 1;
                for (int i = 0; i < crowns && candidates.Count > 0; i++)
                {
                    var b = candidates[rng.Next(candidates.Count)];
                    candidates.Remove(b);
                    b.MakeCrown();
                }
            }

            // 강화 블록 — 레벨 61부터, 돌·상자·판자에만, 20% 이하
            if (level >= Balance.ReinforcedFromLevel)
            {
                var cand = info.blocks.FindAll(b => b.kind == BlockKind.Stone || b.kind == BlockKind.Crate || b.kind == BlockKind.Plank || b.kind == BlockKind.Cube);
                int max = Mathf.FloorToInt(info.blocks.Count * Balance.ReinforcedRatioCap);
                int n = Mathf.Min(max, 1 + (level - Balance.ReinforcedFromLevel) / 20);
                for (int i = 0; i < n && cand.Count > 0; i++)
                {
                    var b = cand[rng.Next(cand.Count)];
                    cand.Remove(b);
                    int hp = level >= 120 ? 3 : 2;
                    b.Setup(b.kind, b.BaseColor, b.GetComponent<Rigidbody>().mass, hp);
                }
            }

            // 접착 블록 — 레벨 91부터, 인접 블록 1~2쌍을 FixedJoint로
            if (level >= Balance.StickyFromLevel)
            {
                int pairs = level >= 150 ? 2 : 1;
                var pool = new List<Block>(info.blocks);
                for (int i = 0; i < pairs && pool.Count > 1; i++)
                {
                    var a = pool[rng.Next(pool.Count)];
                    Block best = null; float bd = 99f;
                    foreach (var o in pool)
                    {
                        if (o == a) continue;
                        float d = Vector3.Distance(a.transform.position, o.transform.position);
                        if (d < bd) { bd = d; best = o; }
                    }
                    if (best != null && bd < 1.3f)
                    {
                        var j = a.gameObject.AddComponent<FixedJoint>();
                        j.connectedBody = best.GetComponent<Rigidbody>();
                        a.sticky = best.sticky = true;
                        a.Retint(a.BaseColor * 0.85f + new Color(0.1f, 0.1f, 0f)); // 접착 표시: 살짝 누런 톤
                        pool.Remove(a); pool.Remove(best);
                    }
                }
            }

            // 장애물 — 구조물 앞면보다 앞에 두어 공만 막고 블록은 건드리지 않게 (통나무처럼 z로 긴 구조물 대응)
            if (info.hard || level % 7 == 3)
            {
                Physics.SyncTransforms(); // 같은 프레임에 만든 콜라이더의 bounds를 정확히 읽기 위해
                float minZ = 0f;
                foreach (var b in info.blocks) { var c = b.GetComponent<Collider>(); if (c != null) minZ = Mathf.Min(minZ, c.bounds.min.z); }
                if (info.hard && level % 20 == 0) Windmill.Create(root, new Vector3(0f, PedestalTop + 2.2f, minZ - 0.6f), 1.8f);
                else PendulumHammer.Create(root, new Vector3(0f, PedestalTop + 7.5f, minZ - 0.9f), 5.2f); // 망치 머리 반지름 0.35 + 여유
            }

            // 구조물을 정지 상태로 잠재운다 (물리 솔버의 미세 떨림으로 저절로 무너지는 것 방지)
            foreach (var b in info.blocks) b.SettleAndSleep();

            // 시작 공
            int baseBalls = info.hard ? 15 : 22 + (level * 5) % 11; // 22~32
            info.startBalls = baseBalls;
            info.structureName = type switch { 0 => "원통 다발", 1 => "큐브 격자", 2 => "판자 선반", 3 => "통나무 탑", 4 => "얼음 벽", _ => "삼중 받침대" };
            return info;
        }

        // ---------------- 구조물 6종 ----------------

        static void BuildCylinderCluster(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 1.7f, p);
            float y0 = PedestalTop;
            int cols = 3, rows = 2;
            float sp = 0.75f;
            for (int layer = 0; layer < 2; layer++)
            {
                float h = layer == 0 ? 1.6f : 1.2f;
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        float x = (c - 1) * sp, z = (r - 0.5f) * sp;
                        var kind = rng.Next(3) == 0 ? BlockKind.Candy : BlockKind.Cylinder;
                        Color col = (c + r + layer) % 2 == 0 ? p.a : p.b;
                        if (kind == BlockKind.Candy) col = p.c;
                        MakeBlock(root, PrimitiveType.Cylinder, kind, new Vector3(x, y0 + h * 0.5f, z), new Vector3(0.55f, h * 0.5f, 0.55f), Quaternion.identity, col, MassFor(kind), info.blocks);
                    }
                // 층 사이 판자
                y0 += h;
                if (layer == 0)
                {
                    MakeBlock(root, PrimitiveType.Cube, BlockKind.Plank, new Vector3(0, y0 + 0.1f, 0), new Vector3(2.6f, 0.2f, 1.6f), Quaternion.identity, p.d, MassFor(BlockKind.Plank), info.blocks);
                    y0 += 0.2f;
                }
            }
        }

        static void BuildCubeGrid(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 1.65f, p, true);
            int w = 6, h = 7;
            float s = 0.5f;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    float x = (i - (w - 1) * 0.5f) * s;
                    float y = PedestalTop + s * 0.5f + j * s;
                    Color col = ((i + j) % 3 == 0) ? p.b : p.a;
                    var kind = rng.Next(9) == 0 ? BlockKind.Crate : BlockKind.Cube;
                    if (kind == BlockKind.Crate) col = new Color(0.65f, 0.42f, 0.2f);
                    MakeBlock(root, PrimitiveType.Cube, kind, new Vector3(x, y, 0), Vector3.one * (s - 0.01f), Quaternion.identity, col, MassFor(kind), info.blocks);
                }
        }

        static void BuildFrameShelf(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 1.8f, p);
            float y = PedestalTop;
            Color plank = info.theme == Theme.Winter ? new Color(0.95f, 0.9f, 0.75f) : p.c;
            for (int tier = 0; tier < 3; tier++)
            {
                // 바닥 판자
                MakeBlock(root, PrimitiveType.Cube, BlockKind.Plank, new Vector3(0, y + 0.1f, 0), new Vector3(2.8f, 0.2f, 1.2f), Quaternion.identity, plank, MassFor(BlockKind.Plank), info.blocks);
                y += 0.2f;
                // 기둥 2~3개
                int posts = tier == 2 ? 2 : 3;
                float ph = 1.1f;
                for (int i = 0; i < posts; i++)
                {
                    float x = posts == 3 ? (i - 1) * 1.1f : (i == 0 ? -0.9f : 0.9f);
                    var kind = info.theme == Theme.Desert ? BlockKind.Stone : BlockKind.Cylinder;
                    Color col = kind == BlockKind.Stone ? new Color(0.95f, 0.95f, 0.9f) : (i % 2 == 0 ? p.a : p.b);
                    MakeBlock(root, PrimitiveType.Cylinder, kind, new Vector3(x, y + ph * 0.5f, 0), new Vector3(0.45f, ph * 0.5f, 0.45f), Quaternion.identity, col, MassFor(kind), info.blocks);
                }
                y += ph;
            }
            // 꼭대기 사탕
            MakeBlock(root, PrimitiveType.Cube, BlockKind.Plank, new Vector3(0, y + 0.1f, 0), new Vector3(2.2f, 0.2f, 1.0f), Quaternion.identity, plank, MassFor(BlockKind.Plank), info.blocks);
            y += 0.2f;
            for (int i = -1; i <= 1; i++)
                MakeBlock(root, PrimitiveType.Cylinder, BlockKind.Candy, new Vector3(i * 0.7f, y + 0.45f, 0), new Vector3(0.4f, 0.45f, 0.4f), Quaternion.identity, p.c, MassFor(BlockKind.Candy), info.blocks);
        }

        static void BuildLogTower(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 1.7f, p);
            Color wood = new Color(0.6f, 0.38f, 0.18f);
            float y = PedestalTop;
            int layers = 6;
            for (int l = 0; l < layers; l++)
            {
                bool alongX = l % 2 == 0;
                for (int i = -1; i <= 1; i++)
                {
                    Vector3 pos = alongX ? new Vector3(0, y + 0.3f, i * 0.65f) : new Vector3(i * 0.65f, y + 0.3f, 0);
                    Quaternion rot = alongX ? Quaternion.Euler(0, 0, 90) : Quaternion.Euler(90, 0, 0);
                    var kind = (l == 2 || l == 4) && i == 0 ? BlockKind.Crate : BlockKind.Log;
                    if (kind == BlockKind.Crate)
                        MakeBlock(root, PrimitiveType.Cube, kind, new Vector3(pos.x, pos.y, pos.z), Vector3.one * 0.6f, Quaternion.identity, p.d, MassFor(kind), info.blocks);
                    else
                        MakeBlock(root, PrimitiveType.Cylinder, kind, pos, new Vector3(0.6f, 1.0f, 0.6f), rot, wood, MassFor(kind), info.blocks);
                }
                y += 0.6f;
            }
            // 꼭대기 큐브 3개
            for (int i = -1; i <= 1; i++)
                MakeBlock(root, PrimitiveType.Cube, BlockKind.Cube, new Vector3(i * 0.6f, y + 0.3f, 0), Vector3.one * 0.55f, Quaternion.identity, p.b, 1f, info.blocks);
        }

        static void BuildIceWall(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 1.5f, p, true);
            Color ice = new Color(0.6f, 0.9f, 1f);
            Color crate = new Color(0.72f, 0.5f, 0.25f);
            int w = 5, h = 6;
            float s = 0.55f;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    float x = (i - (w - 1) * 0.5f) * s;
                    float y = PedestalTop + s * 0.5f + j * s;
                    bool isCrate = (j >= 4 && (i + j) % 2 == 0) || (j == 1 && i == 2);
                    var kind = isCrate ? BlockKind.Crate : BlockKind.Ice;
                    MakeBlock(root, PrimitiveType.Cube, kind, new Vector3(x, y, 0), Vector3.one * (s - 0.01f), Quaternion.identity, isCrate ? crate : ice, MassFor(kind), info.blocks);
                }
            // 가운데 사탕 원통 하나
            MakeBlock(root, PrimitiveType.Cylinder, BlockKind.Candy, new Vector3(0, PedestalTop + h * s + 0.5f, 0), new Vector3(0.45f, 0.5f, 0.45f), Quaternion.identity, p.c, MassFor(BlockKind.Candy), info.blocks);
        }

        static void BuildMultiPedestal(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            int count = rng.Next(2) == 0 ? 2 : 3;
            float spacing = count == 3 ? 2.1f : 2.4f;
            for (int k = 0; k < count; k++)
            {
                float cx = (k - (count - 1) * 0.5f) * spacing;
                Pedestal(root, new Vector3(cx, 0, 0), 0.9f, p);
                float y = PedestalTop;
                int tall = 3 + rng.Next(2);
                for (int l = 0; l < tall; l++)
                {
                    var kind = l == tall - 1 ? BlockKind.Candy : (info.theme == Theme.Desert ? BlockKind.Stone : BlockKind.Cylinder);
                    Color col = kind == BlockKind.Candy ? p.c : kind == BlockKind.Stone ? new Color(0.95f, 0.95f, 0.9f) : ((l + k) % 2 == 0 ? p.a : p.b);
                    float h = 0.9f;
                    MakeBlock(root, PrimitiveType.Cylinder, kind, new Vector3(cx, y + h * 0.5f, 0), new Vector3(0.5f, h * 0.5f, 0.5f), Quaternion.identity, col, MassFor(kind), info.blocks);
                    y += h;
                }
            }
            if (count == 2)
            {
                // 두 받침대 사이 판자 + 그 위 원통
                float y = PedestalTop + 3.6f + 0.1f;
                MakeBlock(root, PrimitiveType.Cube, BlockKind.Plank, new Vector3(0, y, 0), new Vector3(3.2f, 0.2f, 0.8f), Quaternion.identity, p.d, MassFor(BlockKind.Plank), info.blocks);
                for (int i = -1; i <= 1; i++)
                    MakeBlock(root, PrimitiveType.Cylinder, BlockKind.Cylinder, new Vector3(i * 0.8f, y + 0.1f + 0.5f, 0), new Vector3(0.4f, 0.5f, 0.4f), Quaternion.identity, i == 0 ? p.a : p.b, 1f, info.blocks);
            }
        }

        // ---------------- 로비 / 대장간 시험 발사대 ----------------

        public static void BuildLobbyBackdrop(Transform root, Camera cam, SaveData data)
        {
            var theme = (Theme)Mathf.Clamp(data.trainingChapter, 0, 2);
            BuildEnvironment(root, cam, theme);
            var go = new GameObject("TestRange");
            go.transform.SetParent(root);
            var tr = go.AddComponent<TestRange>();
            tr.Init(root, cam, theme);
        }

        /// <summary>대장간 시험 발사대 겸 로비 배경: 작은 구조물이 무너지면 2초 뒤 다시 쌓인다.</summary>
        public class TestRange : MonoBehaviour
        {
            Transform root;
            Camera cam;
            Theme theme;
            Transform structRoot;
            Cannon cannon;
            float rebuildAt = -1f;
            List<Block> blocks = new();

            public void Init(Transform r, Camera c, Theme t)
            {
                root = r; cam = c; theme = t;
                var p = GetPalette(theme);
                Pedestal(root, Vector3.zero, 1.6f, p);
                cannon = Cannon.Create(root, cam, GameManager.I.CurrentBallStats(), null);
                cannon.inputEnabled = false;
                Rebuild();
            }

            public void RefreshStats() { if (cannon != null) cannon.SetStats(GameManager.I.CurrentBallStats()); }

            public void Rebuild()
            {
                if (structRoot != null) DestroyImmediate(structRoot.gameObject);
                structRoot = new GameObject("TestStructure").transform;
                structRoot.SetParent(root);
                blocks.Clear();
                var p = GetPalette(theme);
                float s = 0.5f;
                for (int j = 0; j < 4; j++)
                    for (int i = 0; i < 4; i++)
                    {
                        float x = (i - 1.5f) * s;
                        float y = PedestalTop + s * 0.5f + j * s;
                        Color col = (i + j) % 2 == 0 ? p.a : p.b;
                        MakeBlock(structRoot, PrimitiveType.Cube, BlockKind.Cube, new Vector3(x, y, 0), Vector3.one * (s - 0.01f), Quaternion.identity, col, 1f, blocks);
                    }
                foreach (var b in blocks) b.SettleAndSleep();
                rebuildAt = -1f;
            }

            public void TestFire()
            {
                RefreshStats();
                cannon.FireAt(new Vector3(0f, PedestalTop + 0.9f, 0f));
                if (rebuildAt < 0f) rebuildAt = Time.time + 2.5f;
            }

            // ---- 훈련 모드: 1초에 한 발씩 받침대 위 블록을 자동 조준 ----
            public bool autoFire;
            public float autoFireInterval = 1f;
            float nextAutoFire;

            public void SetAutoFire(bool on)
            {
                autoFire = on;
                nextAutoFire = Time.time + 0.3f;
                if (on && AliveBlockCount() == 0) Rebuild();
            }

            int AliveBlockCount()
            {
                int n = 0;
                foreach (var b in blocks) if (b != null && !b.Removed) n++;
                return n;
            }

            void AutoShot()
            {
                RefreshStats();
                Block target = null;
                int alive = AliveBlockCount();
                if (alive == 0) { Rebuild(); return; }
                int pick = Random.Range(0, alive);
                foreach (var b in blocks)
                {
                    if (b == null || b.Removed) continue;
                    if (pick-- == 0) { target = b; break; }
                }
                if (target == null) return;
                Vector3 aim = target.transform.position + Random.insideUnitSphere * 0.15f;
                cannon.FireAt(aim);
            }

            void Update()
            {
                if (autoFire)
                {
                    if (Time.time >= nextAutoFire)
                    {
                        nextAutoFire = Time.time + autoFireInterval;
                        AutoShot();
                    }
                    // 전부 떨어지면 코인 보상 + 1.5초 뒤 다시 쌓기
                    if (AliveBlockCount() == 0 && rebuildAt < 0f)
                    {
                        rebuildAt = Time.time + 1.5f;
                        if (blocks.Count > 0) GameManager.I.OnTrainingStructureCleared();
                    }
                }
                if (rebuildAt > 0f && Time.time >= rebuildAt) Rebuild();
            }
        }
    }
}
