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
        public bool bonus;   // 보너스 스테이지(자동차 부수기): 제한 시간·공 무제한·실패 없음
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

        static Block MakeBlock(Transform root, PrimitiveType prim, BlockKind kind, Vector3 pos, Vector3 scale, Quaternion rot, Color color, float mass, List<Block> list, bool tall = false)
        {
            var go = GameObject.CreatePrimitive(prim);
            go.name = kind.ToString() + (tall ? "_Tall" : "");
            go.transform.SetParent(root);
            go.transform.position = pos;
            go.transform.rotation = rot;
            go.transform.localScale = scale;
            if (prim == PrimitiveType.Cylinder) FlattenCollider(go, true); // 캡슐 → 원기둥 (윗면이 평평해야 쌓인다)
            RoundedMesh.Apply(go, BlockBevel); // 보이는 메시만 둥근 모서리로 (콜라이더는 각진 원본 유지)
            var b = go.AddComponent<Block>();
            b.tall = tall;
            b.fallY = PedestalTop - 1.0f;
            b.Setup(kind, color, mass, 1);
            list.Add(b);
            return b;
        }

        // ---------------- 규격 블록: 짧은 것(한 칸) / 긴 것(세로 두 칸) ----------------

        /// <summary>규격 블록 한 칸 크기(월드). 짧은 블록 = U×U×U, 긴 블록 = U×2U×U (원통은 지름 U, 높이 U 또는 2U).</summary>
        public const float Unit = 0.5f;

        static bool IsCylinderKind(BlockKind k) => k == BlockKind.Cylinder || k == BlockKind.Candy || k == BlockKind.Log || k == BlockKind.Stone;

        /// <summary>
        /// 규격 블록 하나를 만든다. basePos는 블록 바닥 중심. tall이면 높이가 정확히 2배(질량도 2배).
        /// 소재가 원통 계열이면 원기둥, 아니면 육면체로 만들어진다.
        /// </summary>
        static Block MakeUnit(Transform root, BlockKind kind, Vector3 basePos, bool tall, Color color, List<Block> list, float unit = Unit)
        {
            float h = tall ? unit * 2f : unit;
            bool cyl = IsCylinderKind(kind);
            var prim = cyl ? PrimitiveType.Cylinder : PrimitiveType.Cube;
            Vector3 scale = cyl ? new Vector3(unit - 0.01f, h * 0.5f - 0.005f, unit - 0.01f) : new Vector3(unit - 0.01f, h - 0.01f, unit - 0.01f);
            return MakeBlock(root, prim, kind, basePos + Vector3.up * h * 0.5f, scale, Quaternion.identity, color, MassFor(kind) * (tall ? 2f : 1f), list, tall);
        }

        /// <summary>
        /// 한 열(column)을 아래에서 위로 rows칸 채운다. 남은 칸이 2 이상이면 tallChance 확률로 긴 블록(두 칸), 아니면 짧은 블록.
        /// pick(rowIndex, isTall) 으로 칸마다 소재·색을 정한다. rowIndex는 블록 바닥 칸 번호.
        /// </summary>
        static void FillColumn(Transform root, System.Random rng, Vector3 basePos, int rows, float tallChance, System.Func<int, bool, (BlockKind kind, Color color)> pick, List<Block> list, float unit = Unit)
        {
            int j = 0;
            while (j < rows)
            {
                bool tall = rows - j >= 2 && rng.NextDouble() < tallChance;
                var (kind, color) = pick(j, tall);
                MakeUnit(root, kind, basePos + Vector3.up * (j * unit), tall, color, list, unit);
                j += tall ? 2 : 1;
            }
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

            if (Balance.IsBonusLevel(level))
            {
                info.bonus = true;
                BuildCarStage(root, rng, p, info);
                foreach (var b in info.blocks) b.SettleAndSleep();
                info.startBalls = 9999;
                info.structureName = "보너스: 자동차 부수기";
                return info;
            }

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

        // ---------------- 구조물 6종 (짧은/긴 규격 블록 혼합) ----------------

        static void BuildCylinderCluster(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 1.7f, p);
            float u = 0.55f;
            float y0 = PedestalTop;
            int cols = 3, rows = 2;
            float sp = 0.75f;
            // 층 2개, 각 층 높이 = 두 칸(긴 원통 1개 또는 짧은 원통 2개)
            for (int layer = 0; layer < 2; layer++)
            {
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        float x = (c - 1) * sp, z = (r - 0.5f) * sp;
                        int cc = c, rr = r, ll = layer;
                        FillColumn(root, rng, new Vector3(x, y0, z), 2, 0.6f, (j, tall) =>
                        {
                            var kind = rng.Next(3) == 0 ? BlockKind.Candy : BlockKind.Cylinder;
                            Color col = kind == BlockKind.Candy ? p.c : ((cc + rr + ll + j) % 2 == 0 ? p.a : p.b);
                            return (kind, col);
                        }, info.blocks, u);
                    }
                y0 += u * 2f;
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
            float s = Unit;
            Color crate = new Color(0.65f, 0.42f, 0.2f);
            for (int i = 0; i < w; i++)
            {
                float x = (i - (w - 1) * 0.5f) * s;
                int ii = i;
                FillColumn(root, rng, new Vector3(x, PedestalTop, 0), h, 0.35f, (j, tall) =>
                {
                    var kind = rng.Next(9) == 0 ? BlockKind.Crate : BlockKind.Cube;
                    Color col = kind == BlockKind.Crate ? crate : (((ii + j) % 3 == 0) ? p.b : p.a);
                    return (kind, col);
                }, info.blocks, s);
            }
        }

        static void BuildFrameShelf(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 1.8f, p);
            float u = 0.55f;
            float y = PedestalTop;
            Color plank = info.theme == Theme.Winter ? new Color(0.95f, 0.9f, 0.75f) : p.c;
            for (int tier = 0; tier < 3; tier++)
            {
                MakeBlock(root, PrimitiveType.Cube, BlockKind.Plank, new Vector3(0, y + 0.1f, 0), new Vector3(2.8f, 0.2f, 1.2f), Quaternion.identity, plank, MassFor(BlockKind.Plank), info.blocks);
                y += 0.2f;
                // 기둥 2~3개: 긴 기둥 하나 또는 짧은 기둥 두 개 (높이 = 두 칸)
                int posts = tier == 2 ? 2 : 3;
                for (int i = 0; i < posts; i++)
                {
                    float x = posts == 3 ? (i - 1) * 1.1f : (i == 0 ? -0.9f : 0.9f);
                    int ii = i;
                    FillColumn(root, rng, new Vector3(x, y, 0), 2, 0.7f, (j, tall) =>
                    {
                        var kind = info.theme == Theme.Desert ? BlockKind.Stone : BlockKind.Cylinder;
                        Color col = kind == BlockKind.Stone ? new Color(0.95f, 0.95f, 0.9f) : ((ii + j) % 2 == 0 ? p.a : p.b);
                        return (kind, col);
                    }, info.blocks, u);
                }
                y += u * 2f;
            }
            // 꼭대기 판자 + 짧은 사탕 3개
            MakeBlock(root, PrimitiveType.Cube, BlockKind.Plank, new Vector3(0, y + 0.1f, 0), new Vector3(2.2f, 0.2f, 1.0f), Quaternion.identity, plank, MassFor(BlockKind.Plank), info.blocks);
            y += 0.2f;
            for (int i = -1; i <= 1; i++)
                MakeUnit(root, BlockKind.Candy, new Vector3(i * 0.7f, y, 0), false, p.c, info.blocks, u);
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
            // 꼭대기: 세워 둔 짧은/긴 통나무와 큐브
            for (int i = -1; i <= 1; i++)
            {
                bool tall = i == 0;
                var kind = i == 0 ? BlockKind.Log : BlockKind.Cube;
                MakeUnit(root, kind, new Vector3(i * 0.6f, y, 0), tall, kind == BlockKind.Log ? wood : p.b, info.blocks, 0.55f);
            }
        }

        static void BuildIceWall(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 1.5f, p, true);
            Color ice = new Color(0.6f, 0.9f, 1f);
            Color crate = new Color(0.72f, 0.5f, 0.25f);
            int w = 5, h = 6;
            float s = 0.55f;
            for (int i = 0; i < w; i++)
            {
                float x = (i - (w - 1) * 0.5f) * s;
                int ii = i;
                FillColumn(root, rng, new Vector3(x, PedestalTop, 0), h, 0.3f, (j, tall) =>
                {
                    bool isCrate = (j >= 4 && (ii + j) % 2 == 0) || (j == 1 && ii == 2);
                    return (isCrate ? BlockKind.Crate : BlockKind.Ice, isCrate ? crate : ice);
                }, info.blocks, s);
            }
            // 가운데 긴 사탕 원통 하나
            MakeUnit(root, BlockKind.Candy, new Vector3(0, PedestalTop + h * s, 0), true, p.c, info.blocks, s);
        }

        static void BuildMultiPedestal(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            int count = rng.Next(2) == 0 ? 2 : 3;
            float spacing = count == 3 ? 2.1f : 2.4f;
            float u = Unit;
            int stackRows = count == 2 ? 7 : 6 + rng.Next(3); // 두 받침대일 땐 판자를 얹기 위해 높이 통일
            for (int k = 0; k < count; k++)
            {
                float cx = (k - (count - 1) * 0.5f) * spacing;
                Pedestal(root, new Vector3(cx, 0, 0), 0.9f, p);
                int kk = k;
                int rows = count == 2 ? stackRows : 6 + rng.Next(3);
                FillColumn(root, rng, new Vector3(cx, PedestalTop, 0), rows, 0.55f, (j, tall) =>
                {
                    bool top = j + (tall ? 2 : 1) >= rows;
                    var kind = top ? BlockKind.Candy : (info.theme == Theme.Desert ? BlockKind.Stone : BlockKind.Cylinder);
                    Color col = kind == BlockKind.Candy ? p.c : kind == BlockKind.Stone ? new Color(0.95f, 0.95f, 0.9f) : ((j + kk) % 2 == 0 ? p.a : p.b);
                    return (kind, col);
                }, info.blocks, u);
            }
            if (count == 2)
            {
                // 두 받침대 사이 판자 + 그 위 짧은/긴 원통
                float y = PedestalTop + stackRows * u + 0.1f;
                MakeBlock(root, PrimitiveType.Cube, BlockKind.Plank, new Vector3(0, y, 0), new Vector3(3.2f, 0.2f, 0.8f), Quaternion.identity, p.d, MassFor(BlockKind.Plank), info.blocks);
                for (int i = -1; i <= 1; i++)
                    MakeUnit(root, BlockKind.Cylinder, new Vector3(i * 0.8f, y + 0.1f, 0), i == 0, i == 0 ? p.a : p.b, info.blocks, u);
            }
        }

        // ---------------- 보너스 스테이지: 자동차 ----------------

        /// <summary>
        /// 스트리트 파이터 보너스 스테이지식 "차 부수기". 블록으로 조립한 자동차 한 대가 넓은 받침대 위에 놓인다.
        /// 바퀴(원통) 위에 바닥 판, 그 위 차체(큐브), 유리창(얼음: 맞으면 깨짐), 지붕(판자), 범퍼·전조등.
        /// </summary>
        static void BuildCarStage(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 2.3f, p, true);
            float y0 = PedestalTop;
            var body = new Color(0.9f, 0.15f, 0.2f);      // 차체 빨강
            var bodyDark = new Color(0.65f, 0.1f, 0.15f);
            var tire = new Color(0.16f, 0.16f, 0.18f);
            var glass = new Color(0.6f, 0.9f, 1f);
            var chrome = new Color(0.8f, 0.82f, 0.85f);
            var lamp = new Color(1f, 0.9f, 0.35f);
            var seat = new Color(0.35f, 0.25f, 0.2f);
            var L = info.blocks;

            // 바퀴 4개: 옆으로 눕힌 짧은 원통 (흰 테두리 띠가 타이어 옆면처럼 보인다)
            foreach (float x in new[] { -1.05f, 1.05f })
                foreach (float z in new[] { -0.55f, 0.55f })
                    MakeBlock(root, PrimitiveType.Cylinder, BlockKind.Cylinder, new Vector3(x, y0 + 0.3f, z), new Vector3(0.6f, 0.15f, 0.6f), Quaternion.Euler(90, 0, 0), tire, 1.2f, L);

            // 바닥 판(섀시)
            MakeBlock(root, PrimitiveType.Cube, BlockKind.Plank, new Vector3(0, y0 + 0.7f, 0), new Vector3(3.6f, 0.2f, 1.5f), Quaternion.identity, bodyDark, 1.5f, L);

            // 차체: 6 × 3 큐브 한 층
            for (int i = 0; i < 6; i++)
                for (int k = -1; k <= 1; k++)
                {
                    float x = (i - 2.5f) * Unit;
                    MakeUnit(root, BlockKind.Cube, new Vector3(x, y0 + 0.8f, k * Unit), false, (i + k) % 2 == 0 ? body : bodyDark, L);
                }

            // 캐빈: 4 × 3, 바깥 고리는 유리창(얼음), 안쪽 2칸은 좌석
            for (int i = 0; i < 4; i++)
                for (int k = -1; k <= 1; k++)
                {
                    float x = (i - 1.5f) * Unit;
                    bool inner = k == 0 && (i == 1 || i == 2);
                    MakeUnit(root, inner ? BlockKind.Cube : BlockKind.Ice, new Vector3(x, y0 + 1.3f, k * Unit), false, inner ? seat : glass, L);
                }

            // 지붕
            MakeBlock(root, PrimitiveType.Cube, BlockKind.Plank, new Vector3(0, y0 + 1.9f, 0), new Vector3(2.2f, 0.2f, 1.6f), Quaternion.identity, body, 1.2f, L);

            // 범퍼(앞뒤) + 전조등/후미등
            foreach (float sx in new[] { -1f, 1f })
            {
                MakeBlock(root, PrimitiveType.Cube, BlockKind.Cube, new Vector3(sx * 1.65f, y0 + 0.95f, 0), new Vector3(0.28f, 0.28f, 1.5f), Quaternion.identity, chrome, 0.8f, L);
                foreach (float z in new[] { -0.5f, 0.5f })
                    MakeBlock(root, PrimitiveType.Cube, BlockKind.Cube, new Vector3(sx * 1.65f, y0 + 1.22f, z), Vector3.one * 0.26f, Quaternion.identity, sx > 0 ? lamp : new Color(1f, 0.3f, 0.2f), 0.3f, L);
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
                float s = Unit;
                var rng = new System.Random(Random.Range(0, 9999));
                for (int i = 0; i < 4; i++)
                {
                    float x = (i - 1.5f) * s;
                    int ii = i;
                    FillColumn(structRoot, rng, new Vector3(x, PedestalTop, 0), 4, 0.35f, (j, tall) => (BlockKind.Cube, (ii + j) % 2 == 0 ? p.a : p.b), blocks, s);
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
