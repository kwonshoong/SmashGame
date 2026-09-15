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
        public bool tower;   // 격파 도전: 제한 시간·공 무제한, 전부 무너뜨리면 단계 상승
        public int towerStage;
        public float timeLimit;
        public int rangeTier;   // 0 단거리 · 1 중거리 · 2 장거리
        public float rangeZ;    // 구조물이 뒤로 밀린 거리(월드 z)
    }

    /// <summary>
    /// 레벨을 절차적으로 생성한다. 레벨 번호가 시드이므로 같은 레벨은 항상 같은 구조물이다.
    /// 소재 9종·받침대 1~3개·장애물 2종·테마 3종 조합(역기획서 3장)을 코드로 옮겼다.
    /// </summary>
    public static class LevelBuilder
    {
        public const float PedestalTop = 1.6f;

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
        /// <summary>현재 빌드 중인 레벨의 블록 질량 배율 (Build가 설정, 로비·격파 도전은 1)</summary>
        static float massScale = 1f;

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
            b.Setup(kind, color, mass * massScale, 1);
            list.Add(b);
            return b;
        }

        // ---------------- 규격 블록: 짧은 것(한 칸) / 긴 것(세로 두 칸) ----------------

        /// <summary>규격 블록 한 칸 크기(월드). 짧은 블록 = U×U×U, 긴 블록 = U×2U×U (원통은 지름 U, 높이 U 또는 2U).</summary>
        public const float Unit = 0.5f;
        /// <summary>일반 레벨용 조밀 규격(월드). 블록을 작게 해 같은 자리에 약 2배 수를 놓는다 (시원하게 부수는 느낌).</summary>
        public const float DU = 0.36f;

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
            return MakeBlock(root, prim, kind, basePos + Vector3.up * h * 0.5f, scale, Quaternion.identity, color, MassFor(kind) * (tall ? 2f : 1f) * UnitMass(unit), list, tall);
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

        /// <summary>규격 블록 질량 배율: 한 칸 0.5를 기준으로 크기의 제곱(작은 블록은 그만큼 가볍게, 대신 수가 많다)</summary>
        static float UnitMass(float unit) => (unit / 0.5f) * (unit / 0.5f);

        static float MassFor(BlockKind k) => k switch
        {
            BlockKind.Stone => 2.2f,   // 긴 돌기둥 = 4.4, 레벨 배율 최대 1.6 → 7.0 (파괴력 200%대에서 넘어가는 선)
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
            massScale = Balance.BlockMassScale(level);

            if (Balance.IsBonusLevel(level))
            {
                info.bonus = true;
                BuildCarStage(root, rng, p, info);
                foreach (var b in info.blocks) b.SettleAndSleep();
                info.startBalls = 9999;
                info.timeLimit = Balance.BonusSeconds;
                info.structureName = "보너스: 자동차 부수기";
                return info;
            }

            int T = level >= Balance.NewStructuresFromLevel ? Balance.StructureTypes : 6;   // 초반엔 기본 6종만
            int type = info.hard ? (level / 10 + 6) % T : (level * 5 + rng.Next(0, 3)) % T;
            if (level <= 3) type = new[] { 1, 0, 2 }[level - 1];   // 튜토리얼 구간은 쉬운 구조물

            // 사거리: 구조물(받침대 포함)을 자식 루트에 짓고 통째로 뒤로 민다. 카메라·대포는 그대로라 멀수록 작게 보이고 포물선이 높아진다.
            info.rangeTier = Balance.RangeTier(level);
            info.rangeZ = Balance.RangeZ[info.rangeTier];
            var levelRoot = root;
            root = new GameObject("Structure").transform;
            root.SetParent(levelRoot);
            switch (type)
            {
                case 0: BuildCylinderCluster(root, rng, p, info); break;
                case 1: BuildCubeGrid(root, rng, p, info); break;
                case 2: BuildFrameShelf(root, rng, p, info); break;
                case 3: BuildLogTower(root, rng, p, info); break;
                case 4: BuildIceWall(root, rng, p, info); break;
                case 5: BuildMultiPedestal(root, rng, p, info); break;
                case 6: BuildPyramid(root, rng, p, info); break;
                case 7: BuildFortress(root, rng, p, info); break;
                case 8: BuildGate(root, rng, p, info); break;
                case 9: BuildTwinTowers(root, rng, p, info); break;
                case 10: BuildStaircase(root, rng, p, info); break;
                default: BuildRing(root, rng, p, info); break;
            }
            root.position = new Vector3(0f, 0f, info.rangeZ);
            Physics.SyncTransforms();

            // 강화 블록 — 레벨 61부터, 돌·상자·판자에만, 20% 이하
            if (level >= Balance.ReinforcedFromLevel)
            {
                // 강화 블록은 바닥 줄(받침대에 직접 닿는 블록)에만 둔다. 위에 얹히면 무게+마찰로 아래 블록을 눌러
                // 구조물 전체가 붙은 듯 굳어 버린다(플레이 로그로 확인). 바닥에 있으면 자기 자리만 지키는 "닻" 역할.
                var cand = info.blocks.FindAll(b =>
                    (b.kind == BlockKind.Stone || b.kind == BlockKind.Crate || b.kind == BlockKind.Cube)
                    && b.GetComponent<Renderer>().bounds.min.y < PedestalTop + 0.12f);
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
                        // 접착 표시: 두 블록 다 누런 톤 + 사이를 잇는 노란 "접착제 띠" (붙어서 같이 움직이는 게 의도임을 보이게)
                        a.Retint(Color.Lerp(a.BaseColor, new Color(1f, 0.85f, 0.2f), 0.45f));
                        best.Retint(Color.Lerp(best.BaseColor, new Color(1f, 0.85f, 0.2f), 0.45f));
                        var glue = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        glue.name = "Glue";
                        Object.DestroyImmediate(glue.GetComponent<Collider>());
                        Vector3 pa = a.transform.position, pb = best.transform.position;
                        glue.transform.position = (pa + pb) * 0.5f;
                        glue.transform.rotation = Quaternion.FromToRotation(Vector3.up, (pb - pa).normalized);
                        glue.transform.localScale = new Vector3(0.16f, (pb - pa).magnitude * 0.5f, 0.16f);
                        glue.transform.SetParent(a.transform, true);
                        var gm = Materials.Get(new Color(1f, 0.8f, 0.1f), true);
                        if (gm.HasProperty("_EmissionColor")) { gm.EnableKeyword("_EMISSION"); gm.SetColor("_EmissionColor", new Color(0.6f, 0.45f, 0f)); }
                        glue.GetComponent<Renderer>().material = gm;
                        pool.Remove(a); pool.Remove(best);
                    }
                }
            }

            // 장애물 — 구조물 앞면보다 앞에 두어 공만 막고 블록은 건드리지 않게 (통나무처럼 z로 긴 구조물 대응)
            if (Balance.HasObstacle(level))
            {
                Physics.SyncTransforms(); // 같은 프레임에 만든 콜라이더의 bounds를 정확히 읽기 위해
                float minZ = info.rangeZ;
                foreach (var b in info.blocks) { var c = b.GetComponent<Collider>(); if (c != null) minZ = Mathf.Min(minZ, c.bounds.min.z); }
                if (info.hard && level % 20 == 0) Windmill.Create(root, new Vector3(0f, PedestalTop + 2.2f, minZ - 0.6f), 1.8f);
                else PendulumHammer.Create(root, new Vector3(0f, PedestalTop + 7.5f, minZ - 0.9f), 5.2f); // 망치 머리 반지름 0.35 + 여유
            }

            // 구조물을 정지 상태로 잠재운다 (물리 솔버의 미세 떨림으로 저절로 무너지는 것 방지)
            foreach (var b in info.blocks) b.SettleAndSleep();

            // 시작 공
            info.startBalls = Balance.StartBalls(level, info.hard, info.blocks.Count) + Balance.RangeExtraBalls(info.rangeTier);
            info.structureName = type switch
            {
                0 => "원통 다발", 1 => "큐브 격자", 2 => "판자 선반", 3 => "통나무 탑", 4 => "얼음 벽", 5 => "삼중 받침대",
                6 => "피라미드", 7 => "요새", 8 => "성문", 9 => "쌍둥이 탑", 10 => "계단", _ => "돌기둥 원진"
            };
            return info;
        }

        // ---------------- 구조물 12종 (조밀 규격 DU: 작은 블록을 약 2배 수로) ----------------

        static void BuildCylinderCluster(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            float u = DU;
            float y0 = PedestalTop;
            int cols = Balance.Grow(info.level, 4, 20, 7), rows = Balance.Grow(info.level, 3, 30, 4);
            int layers = info.level >= 40 ? 3 : 2;
            float sp = u + 0.14f;
            Pedestal(root, Vector3.zero, Mathf.Max(1.7f, cols * sp * 0.5f + 0.5f), p);
            // 층 2~3개, 각 층 높이 = 두 칸(긴 원통 1개 또는 짧은 원통 2개)
            for (int layer = 0; layer < layers; layer++)
            {
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        float x = (c - (cols - 1) * 0.5f) * sp, z = (r - (rows - 1) * 0.5f) * sp;
                        int cc = c, rr = r, ll = layer;
                        FillColumn(root, rng, new Vector3(x, y0, z), 2, 0.6f, (j, tall) =>
                        {
                            var kind = rng.Next(3) == 0 ? BlockKind.Candy : BlockKind.Cylinder;
                            Color col = kind == BlockKind.Candy ? p.c : ((cc + rr + ll + j) % 2 == 0 ? p.a : p.b);
                            return (kind, col);
                        }, info.blocks, u);
                    }
                y0 += u * 2f;
                if (layer < layers - 1)
                {
                    MakeBlock(root, PrimitiveType.Cube, BlockKind.Plank, new Vector3(0, y0 + 0.08f, 0), new Vector3(cols * sp + 0.3f, 0.16f, rows * sp + 0.25f), Quaternion.identity, p.d, MassFor(BlockKind.Plank), info.blocks);
                    y0 += 0.16f;
                }
            }
        }

        static void BuildCubeGrid(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            int w = Balance.Grow(info.level, 8, 12, 14), h = Balance.Grow(info.level, 10, 15, 16);
            float s = DU;
            Pedestal(root, Vector3.zero, w * s * 0.5f + 0.15f, p, true);
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
            float u = DU;
            float y = PedestalTop;
            Color plank = info.theme == Theme.Winter ? new Color(0.95f, 0.9f, 0.75f) : p.c;
            for (int tier = 0; tier < 3; tier++)
            {
                MakeBlock(root, PrimitiveType.Cube, BlockKind.Plank, new Vector3(0, y + 0.08f, 0), new Vector3(2.8f, 0.16f, 1.0f), Quaternion.identity, plank, MassFor(BlockKind.Plank), info.blocks);
                y += 0.16f;
                // 기둥 5개(맨 위 층은 4개): 세 칸 높이를 긴/짧은 기둥 조합으로
                int posts = tier == 2 ? 4 : 5;
                for (int i = 0; i < posts; i++)
                {
                    float x = (i - (posts - 1) * 0.5f) * 0.6f;
                    int ii = i;
                    FillColumn(root, rng, new Vector3(x, y, 0), 3, 0.5f, (j, tall) =>
                    {
                        var kind = info.theme == Theme.Desert ? BlockKind.Stone : BlockKind.Cylinder;
                        Color col = kind == BlockKind.Stone ? new Color(0.95f, 0.95f, 0.9f) : ((ii + j) % 2 == 0 ? p.a : p.b);
                        return (kind, col);
                    }, info.blocks, u);
                }
                y += u * 3f;
            }
            // 꼭대기 판자 + 짧은 사탕 4개
            MakeBlock(root, PrimitiveType.Cube, BlockKind.Plank, new Vector3(0, y + 0.08f, 0), new Vector3(2.2f, 0.16f, 0.9f), Quaternion.identity, plank, MassFor(BlockKind.Plank), info.blocks);
            y += 0.16f;
            for (int i = 0; i < 4; i++)
                MakeUnit(root, BlockKind.Candy, new Vector3((i - 1.5f) * 0.5f, y, 0), false, p.c, info.blocks, u);
        }

        static void BuildLogTower(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 1.7f, p);
            Color wood = new Color(0.6f, 0.38f, 0.18f);
            float y = PedestalTop;
            const float d = 0.42f; // 통나무 지름
            int layers = 8;
            for (int l = 0; l < layers; l++)
            {
                bool alongX = l % 2 == 0;
                for (int i = 0; i < 4; i++)
                {
                    float off = (i - 1.5f) * 0.5f;
                    Vector3 pos = alongX ? new Vector3(0, y + d * 0.5f, off) : new Vector3(off, y + d * 0.5f, 0);
                    Quaternion rot = alongX ? Quaternion.Euler(0, 0, 90) : Quaternion.Euler(90, 0, 0);
                    var kind = l % 3 == 2 && (i == 1 || i == 2) ? BlockKind.Crate : BlockKind.Log;
                    if (kind == BlockKind.Crate)
                        MakeBlock(root, PrimitiveType.Cube, kind, pos, Vector3.one * d, Quaternion.identity, p.d, MassFor(kind) * 0.5f, info.blocks);
                    else
                        MakeBlock(root, PrimitiveType.Cylinder, kind, pos, new Vector3(d, 1.0f, d), rot, wood, MassFor(kind) * 0.7f, info.blocks);
                }
                y += d;
            }
            // 꼭대기: 세워 둔 짧은/긴 통나무와 큐브
            for (int i = -2; i <= 2; i++)
            {
                bool tall = i == 0;
                var kind = i % 2 == 0 ? BlockKind.Log : BlockKind.Cube;
                MakeUnit(root, kind, new Vector3(i * 0.42f, y, 0), tall, kind == BlockKind.Log ? wood : p.b, info.blocks, DU);
            }
        }

        static void BuildIceWall(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Color ice = new Color(0.6f, 0.9f, 1f);
            Color crate = new Color(0.72f, 0.5f, 0.25f);
            int w = Balance.Grow(info.level, 7, 12, 13), h = Balance.Grow(info.level, 8, 15, 14);
            float s = DU;
            Pedestal(root, Vector3.zero, w * s * 0.5f + 0.15f, p, true);
            for (int i = 0; i < w; i++)
            {
                float x = (i - (w - 1) * 0.5f) * s;
                int ii = i;
                FillColumn(root, rng, new Vector3(x, PedestalTop, 0), h, 0.3f, (j, tall) =>
                {
                    bool isCrate = (j >= 5 && (ii + j) % 2 == 0) || (j == 1 && ii == 3);
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
            float u = DU;
            int stackRows = count == 2 ? 9 : 7 + rng.Next(3); // 두 받침대일 땐 판자를 얹기 위해 높이 통일
            for (int k = 0; k < count; k++)
            {
                float cx = (k - (count - 1) * 0.5f) * spacing;
                Pedestal(root, new Vector3(cx, 0, 0), 0.9f, p);
                int rows = count == 2 ? stackRows : 7 + rng.Next(3);
                // 받침대마다 나란한 두 열
                for (int c = 0; c < 2; c++)
                {
                    int kk = k, cc = c;
                    FillColumn(root, rng, new Vector3(cx + (c - 0.5f) * u, PedestalTop, 0), rows, 0.55f, (j, tall) =>
                    {
                        bool top = j + (tall ? 2 : 1) >= rows;
                        var kind = top ? BlockKind.Candy : (info.theme == Theme.Desert ? BlockKind.Stone : BlockKind.Cylinder);
                        Color col = kind == BlockKind.Candy ? p.c : kind == BlockKind.Stone ? new Color(0.95f, 0.95f, 0.9f) : ((j + kk + cc) % 2 == 0 ? p.a : p.b);
                        return (kind, col);
                    }, info.blocks, u);
                }
            }
            if (count == 2)
            {
                // 두 받침대 사이 판자 + 그 위 짧은/긴 원통
                float y = PedestalTop + stackRows * u + 0.08f;
                MakeBlock(root, PrimitiveType.Cube, BlockKind.Plank, new Vector3(0, y, 0), new Vector3(3.2f, 0.16f, 0.7f), Quaternion.identity, p.d, MassFor(BlockKind.Plank), info.blocks);
                for (int i = -2; i <= 2; i++)
                    MakeUnit(root, BlockKind.Cylinder, new Vector3(i * 0.55f, y + 0.08f, 0), i == 0, i == 0 ? p.a : p.b, info.blocks, u);
            }
        }

        // 레벨에 따라 완만하게: 25레벨 전엔 한 겹, 30레벨 전엔 돌 대신 상자·원통

        static int Depth(LevelInfo info) => info.level >= Balance.DeepStructuresFromLevel ? 2 : 1;
        static bool Heavy(LevelInfo info) => info.level >= Balance.HeavyStructuresFromLevel;
        static readonly Color StoneCol = new Color(0.9f, 0.88f, 0.82f);
        static readonly Color CrateCol = new Color(0.65f, 0.42f, 0.2f);
        /// <summary>무거운 바닥용 소재: 30레벨부터 돌, 그 전엔 상자</summary>
        static (BlockKind, Color) Base(LevelInfo info) => Heavy(info) ? (BlockKind.Stone, StoneCol) : (BlockKind.Crate, CrateCol);
        /// <summary>기둥용 소재: 30레벨부터 돌기둥, 그 전엔 원통</summary>
        static (BlockKind, Color) Pillar(LevelInfo info, Color light) => Heavy(info) ? (BlockKind.Stone, StoneCol) : (BlockKind.Cylinder, light);

        /// <summary>피라미드: 13열, 가운데가 높고 양끝이 낮은 두 겹. 아래층은 돌·상자, 위층은 큐브.</summary>
        static void BuildPyramid(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            int cols = Balance.Grow(info.level, 13, 25, 15); float u = DU; int depth = Depth(info);
            Pedestal(root, Vector3.zero, cols * u * 0.5f + 0.25f, p, true);
            int center = cols / 2;
            for (int i = 0; i < cols; i++)
            {
                int rows = Mathf.Max(2, Mathf.FloorToInt(8.6f - Mathf.Abs(i - center) * 0.7f) + (cols - 13) / 2);   // 13열: 4..8..4
                float x = (i - (cols - 1) * 0.5f) * u;
                for (int d = 0; d < depth; d++)
                {
                    float z = (d - (depth - 1) * 0.5f) * u;
                    int ii = i;
                    FillColumn(root, rng, new Vector3(x, PedestalTop, z), rows, 0.4f, (j, tall) =>
                    {
                        if (j == 0) return Base(info);
                        if (j == 1 && rng.Next(2) == 0) return (BlockKind.Crate, CrateCol);
                        return (BlockKind.Cube, (ii + j) % 2 == 0 ? p.a : p.b);
                    }, info.blocks, u);
                }
            }
        }

        /// <summary>요새: 앞쪽 낮고 무거운 성벽이 뒤쪽 높은 본성(큐브·얼음)을 가린다. 성벽을 먼저 치우거나 위로 넘겨 맞혀야 한다.</summary>
        static void BuildFortress(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            float u = DU;
            int wall = Balance.Grow(info.level, 11, 25, 14);
            Pedestal(root, Vector3.zero, Mathf.Max(2.4f, wall * u * 0.5f + 0.3f), p, true);
            // 앞 성벽: 11~14열 × 4칸, 돌·상자
            for (int i = 0; i < wall; i++)
            {
                float x = (i - (wall - 1) * 0.5f) * u;
                int ii = i;
                FillColumn(root, rng, new Vector3(x, PedestalTop, -0.6f), 4, 0.5f, (j, tall) =>
                    ii % 3 == 1 ? (BlockKind.Crate, CrateCol) : Base(info), info.blocks, u);
            }
            // 본성: 6열 × 9칸 (25레벨부터 두 겹), 큐브 + 얼음
            for (int i = 0; i < 6; i++)
                for (int d = 0; d < Depth(info); d++)
                {
                    float x = (i - 2.5f) * u, z = 0.2f + d * u;
                    int ii = i;
                    FillColumn(root, rng, new Vector3(x, PedestalTop, z), 9, 0.4f, (j, tall) =>
                        (j >= 6 && (ii + j) % 2 == 0) ? (BlockKind.Ice, new Color(0.6f, 0.9f, 1f)) : (BlockKind.Cube, (ii + j) % 2 == 0 ? p.a : p.b), info.blocks, u);
                }
            // 본성 꼭대기 사탕
            MakeUnit(root, BlockKind.Candy, new Vector3(0f, PedestalTop + 9 * u, 0.2f), true, p.c, info.blocks, u);
        }

        /// <summary>성문: 두꺼운 돌기둥 두 개(각 두 열) + 상인방 판자, 그 위 성가퀴. 기둥 사이에는 작은 탑.</summary>
        static void BuildGate(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 2.2f, p);
            float u = DU;
            int depth = Depth(info);
            foreach (float x in new[] { -1.25f, 1.25f })
                for (int c = 0; c < 2; c++)
                    for (int d = 0; d < depth; d++)
                        FillColumn(root, rng, new Vector3(x + (c - 0.5f) * u, PedestalTop, (d - (depth - 1) * 0.5f) * u), 7, 0.8f, (j, tall) => Pillar(info, j % 2 == 0 ? p.a : p.b), info.blocks, u);
            float top = PedestalTop + 7 * u;
            MakeBlock(root, PrimitiveType.Cube, BlockKind.Plank, new Vector3(0, top + 0.08f, 0), new Vector3(3.4f, 0.16f, 1.0f), Quaternion.identity, p.d, MassFor(BlockKind.Plank) * 1.5f, info.blocks);
            // 성가퀴: 상인방 위 큐브 7개 + 가운데 사탕
            for (int i = -3; i <= 3; i++)
                MakeUnit(root, i == 0 ? BlockKind.Candy : BlockKind.Cube, new Vector3(i * 0.48f, top + 0.16f, 0), i == 0, i == 0 ? p.c : p.b, info.blocks, u);
            // 문 안쪽 작은 탑 3열 (기둥에 가려짐)
            for (int c = -1; c <= 1; c++)
                for (int d = 0; d < depth; d++)
                    FillColumn(root, rng, new Vector3(c * u, PedestalTop, (d - (depth - 1) * 0.5f) * u), 4, 0.5f, (j, tall) => (BlockKind.Crate, CrateCol), info.blocks, u);
        }

        /// <summary>쌍둥이 탑: 세 열짜리 탑 두 개를 다리 판자로 잇고, 다리 위에 원통. 한쪽만 무너뜨리면 다리가 기운다.</summary>
        static void BuildTwinTowers(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 2.4f, p, true);
            float u = DU;
            int depth = Depth(info);
            int twinRows = Balance.Grow(info.level, 10, 30, 13);
            foreach (float cx in new[] { -1.25f, 1.25f })
                for (int i = 0; i < 3; i++)
                    for (int d = 0; d < depth; d++)
                    {
                        int ii = i, dd = d;
                        FillColumn(root, rng, new Vector3(cx + (i - 1f) * u, PedestalTop, (d - (depth - 1) * 0.5f) * u), twinRows, 0.5f, (j, tall) =>
                            j < 2 ? Base(info) : (BlockKind.Cube, (ii + j + dd) % 2 == 0 ? p.a : p.b), info.blocks, u);
                    }
            float top = PedestalTop + twinRows * u;
            MakeBlock(root, PrimitiveType.Cube, BlockKind.Plank, new Vector3(0, top + 0.08f, 0), new Vector3(3.6f, 0.16f, 0.9f), Quaternion.identity, p.d, MassFor(BlockKind.Plank) * 1.5f, info.blocks);
            for (int i = -2; i <= 2; i++)
                MakeUnit(root, i == 0 ? BlockKind.Candy : BlockKind.Cylinder, new Vector3(i * 0.55f, top + 0.16f, 0), i != 0 && i % 2 == 0, i == 0 ? p.c : p.a, info.blocks, u);
        }

        /// <summary>계단: 11열, 왼쪽부터 1~11칸으로 높아지는 두 겹. 낮은 쪽은 무거운 돌, 높은 쪽은 큐브·얼음.</summary>
        static void BuildStaircase(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            int cols = Balance.Grow(info.level, 11, 30, 14);
            float u = DU;
            Pedestal(root, Vector3.zero, cols * u * 0.5f + 0.4f, p, true);
            bool flip = rng.Next(2) == 0;
            for (int i = 0; i < cols; i++)
            {
                int col = flip ? cols - 1 - i : i;
                float x = (col - (cols - 1) * 0.5f) * u;
                int rows = i + 1;
                int depth = Depth(info);
                for (int d = 0; d < depth; d++)
                {
                    int ii = i;
                    FillColumn(root, rng, new Vector3(x, PedestalTop, (d - (depth - 1) * 0.5f) * u), rows, 0.45f, (j, tall) =>
                    {
                        if (ii < 4) return Base(info);
                        if (j >= 7 && (ii + j) % 2 == 0) return (BlockKind.Ice, new Color(0.6f, 0.9f, 1f));
                        return (BlockKind.Cube, (ii + j) % 2 == 0 ? p.a : p.b);
                    }, info.blocks, u);
                }
            }
        }

        /// <summary>돌기둥 원진: 긴 돌기둥 10개가 원을 그리고, 안쪽에 사탕 탑 다섯 기둥, 그 위 원형 판자와 큐브. 뒤쪽 기둥은 앞 기둥에 가려진다.</summary>
        static void BuildRing(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 2.0f, p);
            float u = DU;
            int n = 10; float r = 1.25f;
            for (int k = 0; k < n; k++)
            {
                float a = (k + 0.5f) / n * Mathf.PI * 2f;
                var pos = new Vector3(Mathf.Cos(a) * r, PedestalTop, Mathf.Sin(a) * r);
                int kk = k;
                FillColumn(root, rng, pos, 6, 0.7f, (j, tall) => Pillar(info, (kk + j) % 2 == 0 ? p.a : p.b), info.blocks, u);
            }
            // 가운데 사탕 탑 4칸 + 주위 4개 3칸
            FillColumn(root, rng, new Vector3(0, PedestalTop, 0), 4, 0.5f, (j, tall) => (BlockKind.Candy, p.c), info.blocks, u);
            for (int k = 0; k < 4; k++)
            {
                float a = k / 4f * Mathf.PI * 2f + 0.4f;
                FillColumn(root, rng, new Vector3(Mathf.Cos(a) * 0.62f, PedestalTop, Mathf.Sin(a) * 0.62f), 3, 0.5f, (j, tall) => (BlockKind.Candy, p.c), info.blocks, u);
            }
            // 지붕 원판 + 그 위 큐브
            float top = PedestalTop + 6 * u;
            var roof = MakeBlock(root, PrimitiveType.Cylinder, BlockKind.Plank, new Vector3(0, top + 0.1f, 0), new Vector3(3.2f, 0.1f, 3.2f), Quaternion.identity, p.d, MassFor(BlockKind.Plank) * 2f, info.blocks);
            for (int k = 0; k < 6; k++)
            {
                float a = k / 6f * Mathf.PI * 2f + 0.4f;
                MakeUnit(root, BlockKind.Cube, new Vector3(Mathf.Cos(a) * 0.9f, top + 0.2f, Mathf.Sin(a) * 0.9f), false, k % 2 == 0 ? p.a : p.b, info.blocks, u);
            }
        }

        // ---------------- 격파 도전: 초중량 거대 탑 ----------------

        /// <summary>
        /// 격파 도전 스테이지. 단계가 오를수록 탑이 넓고 높고 두꺼워지며(최대 10×11×2), 블록 질량이 기하급수로 커지고
        /// 4단계부터 강화 블록이 섞인다. 20초 무제한 발사. 파괴력 스탯이 낮으면 블록이 밀리지도 않는다.
        /// </summary>
        public static LevelInfo BuildTower(int stage, Transform root, SaveData data, Camera cam)
        {
            var theme = (Theme)((stage - 1) % 3);
            var info = new LevelInfo { level = data.currentLevel, theme = theme, tower = true, towerStage = stage, timeLimit = Balance.TowerSeconds, pedestalTop = PedestalTop };
            var rng = new System.Random(stage * 4241 + 99);
            var p = GetPalette(theme);
            BuildEnvironment(root, cam, theme);
            massScale = 1f;

            int cols = Balance.TowerCols(stage), rows = Balance.TowerRows(stage), depth = Balance.TowerDepth(stage);
            float u = Unit;
            Pedestal(root, Vector3.zero, cols * u * 0.5f + 0.45f, p, true);

            // 무거워 보이는 팔레트: 강철 회색 큐브 · 나무 상자 · 돌기둥, 단계마다 톤이 조금씩 어두워진다
            float tone = Mathf.Clamp01(1f - (stage - 1) * 0.04f);
            Color steelA = new Color(0.55f, 0.58f, 0.64f) * tone, steelB = new Color(0.42f, 0.45f, 0.52f) * tone;
            Color crate = new Color(0.62f, 0.42f, 0.22f) * tone;
            Color stone = new Color(0.8f, 0.78f, 0.72f) * tone;

            for (int i = 0; i < cols; i++)
                for (int d = 0; d < depth; d++)
                {
                    float x = (i - (cols - 1) * 0.5f) * u;
                    float z = (d - (depth - 1) * 0.5f) * u;
                    int ii = i, dd = d;
                    FillColumn(root, rng, new Vector3(x, PedestalTop, z), rows, 0.45f, (j, tall) =>
                    {
                        int r = rng.Next(10);
                        BlockKind kind = r < 5 ? BlockKind.Cube : (r < 8 ? BlockKind.Crate : BlockKind.Stone);
                        Color col = kind == BlockKind.Cube ? ((ii + j + dd) % 2 == 0 ? steelA : steelB) : kind == BlockKind.Crate ? crate : stone;
                        return (kind, col);
                    }, info.blocks, u);
                }

            // 초중량 + 강화
            float massMult = Balance.TowerMassMult(stage);
            int hp = Balance.TowerHp(stage);
            int reinforced = hp > 1 ? Mathf.RoundToInt(info.blocks.Count * Balance.TowerReinforcedRatio) : 0;
            var pool = new List<Block>(info.blocks);
            foreach (var b in info.blocks)
            {
                var rb = b.GetComponent<Rigidbody>();
                b.Setup(b.kind, b.BaseColor, rb.mass * massMult, 1);
            }
            for (int k = 0; k < reinforced && pool.Count > 0; k++)
            {
                var b = pool[rng.Next(pool.Count)];
                pool.Remove(b);
                b.Setup(b.kind, b.BaseColor, b.GetComponent<Rigidbody>().mass, hp);
            }

            foreach (var b in info.blocks) b.SettleAndSleep();
            info.startBalls = 9999;
            info.structureName = $"격파 도전 {stage}단계";
            return info;
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
            massScale = Balance.BlockMassBase;
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
