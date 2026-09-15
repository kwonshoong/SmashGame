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
        public float fitScale = 1f;   // 화면 맞춤으로 축소된 배율 (1 = 그대로)
        public Balance.MotionKind motion; // 받침대 움직임
    }

    /// <summary>
    /// 레벨을 절차적으로 생성한다. 레벨 번호가 시드이므로 같은 레벨은 항상 같은 구조물이다.
    /// 소재 9종·받침대 1~3개·장애물 2종·테마 3종 조합(역기획서 3장)을 코드로 옮겼다.
    /// </summary>
    public static class LevelBuilder
    {
        public const float PedestalTop = 1.6f;
        /// <summary>받침대 상판의 앞뒤 깊이 = 반지름 × 이 값 (좌우 폭은 반지름 × 2). 얕을수록 공이 상판 앞을 덜 스친다.</summary>
        public const float PedestalDepthRound = 1.5f, PedestalDepthSquare = 1.0f;

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
            pedestalCenters.Clear();
            pedestalGroups.Clear();
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

        static void Pedestal(Transform parent, Vector3 center, float radius, Palette p, bool square = false)
        {
            var gold = Materials.Get(new Color(1f, 0.78f, 0.25f), true, true);
            var purpleDark = Materials.Get(p.pedestal * 0.75f, true);

            // 받침대 묶음(상판·테두리·기둥·발): 움직이는 받침대는 이 묶음째 kinematic으로 움직인다
            var group = new GameObject("PedestalGroup");
            group.transform.SetParent(parent);
            group.transform.position = new Vector3(center.x, PedestalTop, center.z);   // 회전축 = 상판 중심
            var root = group.transform;
            pedestalGroups.Add(group);

            // 상판(콜라이더 있음) — 위치·두께는 물리와 맞물려 있으므로 유지
            var top = GameObject.CreatePrimitive(square ? PrimitiveType.Cube : PrimitiveType.Cylinder);
            top.name = "PedestalTop";
            top.transform.SetParent(root);
            top.transform.position = new Vector3(center.x, PedestalTop - 0.08f, center.z);
            // 앞뒤 깊이는 좌우 폭보다 얕게 (원형은 타원, 사각형은 가로로 긴 판). 구조물 깊이(원진 1.43, 통나무 1.0, 원통 다발 1.2)는 다 들어간다.
            top.transform.localScale = square ? new Vector3(radius * 2f, 0.16f, radius * PedestalDepthSquare) : new Vector3(radius * 2f, 0.08f, radius * PedestalDepthRound);
            top.GetComponent<Renderer>().material = Materials.Get(p.pedestal, true);
            if (!square) FlattenCollider(top, false);   // 사각 상판은 기본 BoxCollider가 이미 평평하다 (움직이는 받침대에선 메시보다 접촉이 안정적)
            RoundedMesh.Apply(top, 0.03f);
            PedestalColliders.Add(top.GetComponent<Collider>());

            // 상판 아래 금색 테두리 + 진한 보라 밑판(두께감)
            float ringY = PedestalTop - 0.16f - 0.03f;
            if (square)
            {
                Deco(PrimitiveType.Cube, root, "PedestalRim", new Vector3(center.x, ringY, center.z), new Vector3(radius * 2f + 0.06f, 0.06f, radius * PedestalDepthSquare + 0.06f), gold, 0.02f);
                Deco(PrimitiveType.Cube, root, "PedestalUnder", new Vector3(center.x, ringY - 0.09f, center.z), new Vector3(radius * 2f - 0.1f, 0.12f, radius * PedestalDepthSquare - 0.1f), purpleDark, 0.03f);
            }
            else
            {
                Deco(PrimitiveType.Cylinder, root, "PedestalRim", new Vector3(center.x, ringY, center.z), new Vector3(radius * 2f + 0.06f, 0.03f, radius * PedestalDepthRound + 0.06f), gold, 0.02f);
                Deco(PrimitiveType.Cylinder, root, "PedestalUnder", new Vector3(center.x, ringY - 0.09f, center.z), new Vector3(radius * 2f - 0.1f, 0.06f, radius * PedestalDepthRound - 0.1f), purpleDark, 0.03f);
            }

            pedestalCenters.Add(center);
            PedestalColumn(root, center, PedestalTop - 0.16f, p);
        }

        /// <summary>
        /// 구조물을 미리 물리로 몇 스텝 굴려 접촉이 안정된 자세(솔버 평형)로 만든 뒤 잠재운다. 이렇게 하지 않으면 첫 발에 깨어나는 순간
        /// 접촉 오프셋만큼(줄당 ~0.005) 내려앉고 위쪽 블록이 살짝 흔들려 "떠 있다가 주저앉는" 것처럼 보인다.
        /// </summary>
        public static void PreSettle(List<Block> blocks, int steps = 40)
        {
            Physics.SyncTransforms();
            var prev = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try { for (int i = 0; i < steps; i++) Physics.Simulate(Time.fixedDeltaTime); }
            finally { Physics.simulationMode = prev; }
            foreach (var b in blocks) if (b != null) b.SettleAndSleep();
        }


        /// <summary>받침대 기둥·발 (상판과 분리: 화면 맞춤으로 상판을 줄여도 기둥은 땅까지 닿아야 하므로 따로 다시 만든다)</summary>
        static void PedestalColumn(Transform root, Vector3 center, float colTop, Palette p, float sizeMul = 1f)
        {
            // sizeMul: 부모가 s배로 축소돼 있을 때 1/s를 넘기면 월드 크기가 원래대로 유지된다 (위치는 월드 좌표로 직접 지정)
            var gold = Materials.Get(new Color(1f, 0.78f, 0.25f), true, true);
            var purpleDark = Materials.Get(p.pedestal * 0.75f, true);
            float colBottom = -1.5f - 0.6f;   // 승강 받침대가 올라가도 기둥이 땅에서 뜨지 않게 아래로 더 묻어 둔다
            var col = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            col.name = "PedestalColumn";
            col.transform.SetParent(root);
            col.transform.position = new Vector3(center.x, (colTop + colBottom) * 0.5f, center.z);
            col.transform.localScale = new Vector3(0.42f, (colTop - colBottom) * 0.5f, 0.42f) * sizeMul;
            col.GetComponent<Renderer>().material = Materials.Get(p.pedestal, true);
            RoundedMesh.Apply(col, 0.04f);
            PedestalColliders.Add(col.GetComponent<Collider>());
            Deco(PrimitiveType.Cylinder, root, "ColumnCap", new Vector3(center.x, colTop - 0.22f, center.z), new Vector3(0.56f, 0.06f, 0.56f) * sizeMul, gold, 0.02f);
            // 아래 링·발은 받침대가 오르내려도 땅에 남아 있도록 묶음 바깥(부모)에 둔다
            var ground = root.parent != null ? root.parent : root;
            Deco(PrimitiveType.Cylinder, ground, "ColumnBase", new Vector3(center.x, -1.05f, center.z), new Vector3(0.56f, 0.06f, 0.56f) * sizeMul, gold, 0.02f);
            // 기둥 세로 홈 느낌의 얇은 금색 줄 4개
            for (int k = 0; k < 4; k++)
            {
                float a = k * 90f * Mathf.Deg2Rad;
                Deco(PrimitiveType.Cube, root, "ColumnStripe", new Vector3(center.x + Mathf.Cos(a) * 0.2f, (colTop - 1.5f) * 0.5f - 0.1f, center.z + Mathf.Sin(a) * 0.2f),
                    new Vector3(0.05f, (colTop + 1.5f) - 0.7f, 0.05f) * sizeMul, gold, 0.01f);
            }
            // 받침 발: 넓은 둥근 판 두 장
            Deco(PrimitiveType.Cylinder, ground, "PedestalFoot", new Vector3(center.x, -1.3f, center.z), new Vector3(1.2f, 0.12f, 1.2f) * sizeMul, Materials.Get(p.pedestal, true), 0.06f);
            Deco(PrimitiveType.Cylinder, ground, "PedestalFoot2", new Vector3(center.x, -1.45f, center.z), new Vector3(1.6f, 0.08f, 1.6f) * sizeMul, purpleDark, 0.05f);
        }

        /// <summary>이번 빌드에서 만든 받침대 중심들 (화면 맞춤 축소 후 기둥을 다시 세울 때 사용)</summary>
        static readonly List<Vector3> pedestalCenters = new();
        static readonly List<GameObject> pedestalGroups = new();

        /// <summary>
        /// 구조물(받침대 상판 포함)이 화면 좌우를 벗어나면 상판 높이를 축으로 통째로 균일 축소한다. 받침대 기둥·발은 축소하지 않고
        /// 땅까지 닿게 다시 세운다. 블록 질량은 규격 규칙(크기 제곱)에 맞춰 s²배. 세로 폰(9:19.5) 기준으로 맞추므로 어느 기기에서도 잘리지 않는다.
        /// </summary>
        static float FitToScreen(Transform structRoot, Transform levelRoot, Camera cam, List<Block> blocks, float rangeZ, Palette p)
        {
            float halfW = 0f;
            foreach (var r in structRoot.GetComponentsInChildren<Renderer>())
            {
                if (r.name.StartsWith("Column") || r.name.StartsWith("PedestalFoot") || r.name == "PedestalColumn" || r.name == "Glue") continue;
                halfW = Mathf.Max(halfW, Mathf.Abs(r.bounds.min.x), Mathf.Abs(r.bounds.max.x));
            }
            float dist = rangeZ - cam.transform.position.z;
            float aspect = Mathf.Min(cam.aspect, 9f / 19.5f);   // 가장 좁은 폰 기준
            float allowed = dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * aspect * FitMargin;
            float s = halfW > allowed ? allowed / halfW : 1f;
            if (s >= 0.999f) return 1f;

            // 상판 윗면(y = PedestalTop) 높이를 축으로 축소: 상판 위치는 그대로, 폭·블록만 작아진다
            structRoot.localScale = Vector3.one * s;
            structRoot.position = new Vector3(0f, PedestalTop * (1f - s), rangeZ);
            foreach (var b in blocks) { var rb = b.GetComponent<Rigidbody>(); if (rb != null) rb.mass *= s * s; }

            // 기둥·발은 버리고 각 받침대 묶음 안에 원래 크기(1/s)로 땅까지 닿게 다시 세운다 (묶음이 움직이면 같이 움직인다)
            var doomed = new List<GameObject>();
            foreach (var t in structRoot.GetComponentsInChildren<Transform>())
                if (t.name == "PedestalColumn" || t.name.StartsWith("Column") || t.name.StartsWith("PedestalFoot")) doomed.Add(t.gameObject);
            foreach (var g in doomed) { var c = g.GetComponent<Collider>(); if (c != null) PedestalColliders.Remove(c); Object.DestroyImmediate(g); }
            foreach (var g in pedestalGroups)
            {
                var wc = g.transform.position;   // 축소 후 상판 중심(월드)
                PedestalColumn(g.transform, new Vector3(wc.x, 0f, wc.z), PedestalTop - 0.16f * s, p, 1f / s);
            }
            Physics.SyncTransforms();
            return s;
        }
        public const float FitMargin = 0.93f;   // 화면 반폭의 93%까지만 (가장자리 여유)


        /// <summary>
        /// 움직이는 받침대 적용 (Balance.PedestalMotionKind). 받침대가 여럿이고 서로 독립된 탑(삼중 받침대)이면 위상을 어긋나게,
        /// 하나의 구조물이 여러 받침대에 걸쳐 있으면(얼음 성문·통나무 다리) 같은 위상으로 오르내려 구조물이 찢어지지 않는다.
        /// </summary>
        static void ApplyPedestalMotion(int level, int type, LevelInfo info)
        {
            var kind = Balance.PedestalMotionKind(level);
            if (kind == Balance.MotionKind.None) return;
            bool independent = type == 5;   // 삼중 받침대만 탑이 독립
            float spin = kind == Balance.MotionKind.Bob ? 0f : Balance.PedestalSpinDegPerSec(level);
            float bob = kind == Balance.MotionKind.Spin ? 0f : Balance.PedestalBobAmplitude;
            for (int i = 0; i < pedestalGroups.Count; i++)
            {
                float phase = independent ? i * Mathf.PI * 2f / Mathf.Max(1, pedestalGroups.Count) : 0f;
                // 회전은 받침대가 하나일 때만 (여러 받침대가 각자 돌면 걸쳐 있는 구조물이 즉시 찢어진다)
                float sp = pedestalGroups.Count == 1 || independent ? spin : 0f;
                PedestalMotion.Attach(pedestalGroups[i], sp, bob, Balance.PedestalBobPeriod, phase).blocks = info.blocks;
            }
            info.motion = kind;
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

        static Block MakeBlock(Transform root, PrimitiveType prim, BlockKind kind, Vector3 pos, Vector3 scale, Quaternion rot, Color color, float mass, List<Block> list, bool tall = false, bool boxCollider = false)
        {
            var go = GameObject.CreatePrimitive(prim);
            go.name = kind.ToString() + (tall ? "_Tall" : "");
            go.transform.SetParent(root);
            go.transform.position = pos;
            go.transform.rotation = rot;
            go.transform.localScale = scale;
            if (prim == PrimitiveType.Cylinder && boxCollider)
            {
                // 눕힌 긴 통나무(보): 보이는 건 원통이지만 충돌은 상자로 — 받침 위에서 굴러떨어지지 않고 위에 블록을 얹을 수 있다
                Object.DestroyImmediate(go.GetComponent<Collider>());
                var bc = go.AddComponent<BoxCollider>();
                bc.size = new Vector3(1f, 2f, 1f);
            }
            else if (prim == PrimitiveType.Cylinder) FlattenCollider(go, true); // 캡슐 → 원기둥 (윗면이 평평해야 쌓인다)
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
            // 옆으로는 0.01 틈(이웃과 마찰로 엉기지 않게), 위아래는 틈 없이 정확히 맞닿게. 세로 틈을 두면 쌓인 블록이 살짝 떠 있다가
            // 첫 발에 깨어나며 줄마다 0.005씩 내려앉아(10줄이면 0.1) 구조물이 주저앉는 것처럼 보인다.
            Vector3 scale = cyl ? new Vector3(unit - 0.01f, h * 0.5f, unit - 0.01f) : new Vector3(unit - 0.01f, h, unit - 0.01f);
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
                PreSettle(info.blocks);
                info.startBalls = 9999;
                info.timeLimit = Balance.BonusSeconds;
                info.structureName = "보너스: 자동차 부수기";
                return info;
            }

            int T = level >= Balance.WideStructuresFromLevel ? Balance.StructureTypes : level >= Balance.NewStructuresFromLevel ? 12 : 6;   // 초반엔 기본 6종만
            // 곱수는 T와 서로소여야 모든 종류가 고르게 나온다 (5는 15와 서로소가 아니라 20레벨부터는 7을 쓴다)
            int mult = T == 15 ? 7 : 5;
            int type = info.hard ? (level / 10 + 6) % T : (level * mult + rng.Next(0, 3)) % T;
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
                case 11: BuildRing(root, rng, p, info); break;
                case 12: BuildIceGate(root, rng, p, info); break;
                case 13: BuildLogBridge(root, rng, p, info); break;
                default: BuildSlabJenga(root, rng, p, info); break;
            }
            root.position = new Vector3(0f, 0f, info.rangeZ);
            Physics.SyncTransforms();
            info.fitScale = FitToScreen(root, levelRoot, cam, info.blocks, info.rangeZ, p);
            ApplyPedestalMotion(level, type, info);

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
            // 회전하는 받침대에는 장애물을 두지 않는다: 구조물이 돌면서 앞에 매달린 망치에 스스로 부딪혀 무너진다(실측: 50° 근처에서 붕괴)
            bool spinning = info.motion == Balance.MotionKind.Spin || info.motion == Balance.MotionKind.SpinBob;
            if (Balance.HasObstacle(level) && !spinning)
            {
                Physics.SyncTransforms(); // 같은 프레임에 만든 콜라이더의 bounds를 정확히 읽기 위해
                float minZ = info.rangeZ;
                foreach (var b in info.blocks) { var c = b.GetComponent<Collider>(); if (c != null) minZ = Mathf.Min(minZ, c.bounds.min.z); }
                if (info.hard && level % 20 == 0) Windmill.Create(root, new Vector3(0f, PedestalTop + 2.2f, minZ - 0.6f), 1.8f);
                else PendulumHammer.Create(root, new Vector3(0f, PedestalTop + 7.5f, minZ - 0.9f), 5.2f); // 망치 머리 반지름 0.35 + 여유
            }

            // 구조물을 정지 상태로 잠재운다 (물리 솔버의 미세 떨림으로 저절로 무너지는 것 방지)
            PreSettle(info.blocks);

            // 시작 공
            info.startBalls = Balance.StartBalls(level, info.hard, info.blocks.Count) + Balance.RangeExtraBalls(info.rangeTier) + (info.motion != Balance.MotionKind.None ? Balance.MotionExtraBalls : 0);
            info.structureName = type switch
            {
                0 => "원통 다발", 1 => "큐브 격자", 2 => "판자 선반", 3 => "통나무 탑", 4 => "얼음 벽", 5 => "삼중 받침대",
                6 => "피라미드", 7 => "요새", 8 => "성문", 9 => "쌍둥이 탑", 10 => "계단", 11 => "돌기둥 원진",
                12 => "얼음 성문", 13 => "통나무 다리", _ => "얼음 젠가"
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
            int w = Balance.Grow(info.level, 8, 12, 14), h = Balance.Grow(info.level, 10, 15, 14);   // 한 겹 벽은 14칸(높이 5.0)까지만: 더 높으면 깨어날 때 기울어 흔들린다
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
                        // 눕힌 통나무는 원통 그대로 굴러가되, 정지 상태에선 구름 저항(Block.rollingLog)으로 미세 떨림에 저절로 구르지 않게
                        MakeBlock(root, PrimitiveType.Cylinder, kind, pos, new Vector3(d, 1.0f, d), rot, wood, MassFor(kind) * 0.7f, info.blocks).SetRollingLog();
                }
                y += d;
            }
            // 꼭대기: 판자 한 장 위에 세워 둔 짧은/긴 통나무와 큐브 (둥근 통나무 등 위에 바로 세우면 깨어날 때 굴러떨어진다)
            MakeBlock(root, PrimitiveType.Cube, BlockKind.Plank, new Vector3(0, y + 0.08f, 0), new Vector3(2.0f, 0.16f, 2.0f), Quaternion.identity, p.d, MassFor(BlockKind.Plank), info.blocks);
            y += 0.16f;
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


        // ---------------- 긴 부재 구조물 3종 (레퍼런스 375레벨: 얇은 얼음 판 기둥·긴 통나무 보·보라 상자 줄) ----------------

        static readonly Color IceCol = new Color(0.6f, 0.9f, 1f);
        static readonly Color WoodCol = new Color(0.6f, 0.38f, 0.18f);
        static readonly Color PurpleCol = new Color(0.5f, 0.22f, 0.8f);
        const float LogD = 0.42f;   // 긴 통나무 지름

        /// <summary>세로로 세운 얇은 얼음 판 (폭 0.24 × 높이 h × 깊이 0.5). basePos는 바닥 중심.</summary>
        static Block IceSlabV(Transform root, Vector3 basePos, float h, List<Block> list, float mass = 0.9f)
            => MakeBlock(root, PrimitiveType.Cube, BlockKind.Ice, basePos + Vector3.up * h * 0.5f, new Vector3(0.24f, h, 0.5f), Quaternion.identity, IceCol, mass, list);
        /// <summary>가로로 눕힌 얼음 판(보). basePos는 바닥 중심, len은 x 길이.</summary>
        static Block IceSlabH(Transform root, Vector3 basePos, float len, List<Block> list, float mass = 1.0f)
            => MakeBlock(root, PrimitiveType.Cube, BlockKind.Ice, basePos + Vector3.up * 0.1f, new Vector3(len, 0.2f, 0.5f), Quaternion.identity, IceCol, mass, list);
        /// <summary>세운 통나무. basePos는 바닥 중심.</summary>
        static Block LogV(Transform root, Vector3 basePos, float h, List<Block> list, float mass = 1.3f)
            => MakeBlock(root, PrimitiveType.Cylinder, BlockKind.Log, basePos + Vector3.up * h * 0.5f, new Vector3(LogD, h * 0.5f, LogD), Quaternion.identity, WoodCol, mass, list);
        /// <summary>x 방향으로 눕힌 긴 통나무(보). basePos는 바닥 중심.</summary>
        static Block LogH(Transform root, Vector3 basePos, float len, List<Block> list, float mass = 1.8f)
            => MakeBlock(root, PrimitiveType.Cylinder, BlockKind.Log, basePos + Vector3.up * LogD * 0.5f, new Vector3(LogD, len * 0.5f, LogD), Quaternion.Euler(0, 0, 90), WoodCol, mass, list, false, true);
        /// <summary>보라 상자 한 줄(n개, 간격 0.37). basePos는 줄 가운데 바닥.</summary>
        static void PurpleRow(Transform root, Vector3 basePos, int n, List<Block> list)
        {
            for (int i = 0; i < n; i++)
                MakeUnit(root, BlockKind.Cube, basePos + Vector3.right * ((i - (n - 1) * 0.5f) * 0.37f), false, PurpleCol, list, DU);
        }

        /// <summary>
        /// 얼음 성문: 양쪽에 얇은 얼음 판 기둥 3개씩, 그 위 얼음 보 → 보라 상자 2줄 → 얼음 판, 바깥쪽엔 세운 통나무 2단 기둥,
        /// 맨 위에 긴 통나무 상인방. 얼음 기둥 하나만 빼도 한쪽이 기울며 상인방이 굴러떨어진다.
        /// </summary>
        static void BuildIceGate(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 2.45f, p, true);
            float y0 = PedestalTop;
            float legH = 1.5f;
            foreach (float sx in new[] { -1f, 1f })
            {
                float cx = sx * 1.3f;
                for (int i = 0; i < 3; i++) IceSlabV(root, new Vector3(cx + (i - 1) * 0.34f, y0, -0.15f), legH, info.blocks);
                float yb = y0 + legH;
                IceSlabH(root, new Vector3(cx, yb, -0.15f), 1.2f, info.blocks);
                PurpleRow(root, new Vector3(cx, yb + 0.2f, -0.15f), 2, info.blocks);
                PurpleRow(root, new Vector3(cx, yb + 0.2f + DU, -0.15f), 3, info.blocks);
                IceSlabH(root, new Vector3(cx, yb + 0.2f + DU * 2f, -0.15f), 1.1f, info.blocks);
                // 바깥 통나무 기둥 2단 (상인방 받침)
                float lx = cx + sx * 0.75f;
                LogV(root, new Vector3(lx, y0, 0.25f), 1.6f, info.blocks);
                LogV(root, new Vector3(lx, y0 + 1.6f, 0.25f), 1.6f, info.blocks);
            }
            // 상인방: 긴 통나무 + 그 위 얼음 판 두 장
            float yl = y0 + 3.2f;
            LogH(root, new Vector3(0f, yl, 0.25f), 4.6f, info.blocks, 2.2f);
            IceSlabH(root, new Vector3(-1.3f, yl + LogD, 0.25f), 1.0f, info.blocks, 0.8f);
            IceSlabH(root, new Vector3(1.3f, yl + LogD, 0.25f), 1.0f, info.blocks, 0.8f);
            if (info.level >= 40) PurpleRow(root, new Vector3(0f, yl + LogD, 0.25f), 2, info.blocks);
        }

        /// <summary>
        /// 통나무 다리: 보라 상자 탑 두 개 사이에 긴 통나무 보, 그 위 얼음 판 기둥 3개와 두 번째 통나무 보, 얼음 판 지붕 위 상자 줄.
        /// 아래 탑을 치면 보가 통째로 굴러 내리며 위층이 쏟아진다.
        /// </summary>
        static void BuildLogBridge(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 2.3f, p, true);
            float y0 = PedestalTop;
            int towerRows = Balance.Grow(info.level, 3, 40, 5);
            foreach (float sx in new[] { -1f, 1f })
            {
                float cx = sx * 1.45f;
                for (int c = 0; c < 2; c++)
                {
                    int cc = c;
                    FillColumn(root, rng, new Vector3(cx + (c - 0.5f) * DU, y0, 0f), towerRows, 0.4f,
                        (j, tall) => (j + cc) % 2 == 0 ? (BlockKind.Cube, PurpleCol) : (BlockKind.Crate, CrateCol), info.blocks, DU);
                }
            }
            float yb = y0 + towerRows * DU;
            LogH(root, new Vector3(0f, yb, 0f), 3.9f, info.blocks, 2.0f);
            float ys = yb + LogD;
            // 보 위 얼음 판 (통나무 위에 바로 세우면 구르므로 판을 한 장 깐다)
            IceSlabH(root, new Vector3(0f, ys, 0f), 3.4f, info.blocks, 1.2f);
            ys += 0.2f;
            for (int i = -1; i <= 1; i++) IceSlabV(root, new Vector3(i * 1.1f, ys, 0f), 1.2f, info.blocks);
            float yt = ys + 1.2f;
            LogH(root, new Vector3(0f, yt, 0f), 3.9f, info.blocks, 2.0f);
            IceSlabH(root, new Vector3(0f, yt + LogD, 0f), 2.6f, info.blocks, 1.0f);
            PurpleRow(root, new Vector3(0f, yt + LogD + 0.2f, 0f), info.level >= 60 ? 5 : 4, info.blocks);
            // 다리 아래 가운데 작은 사탕 탑 (보에 가려짐)
            FillColumn(root, rng, new Vector3(0f, y0, 0f), 2, 0.5f, (j, tall) => (BlockKind.Candy, p.c), info.blocks, DU);
        }

        /// <summary>
        /// 얼음 젠가: 긴 얼음 판·통나무를 층마다 90° 돌려 엇갈리게 쌓은 탑. 가운데 부재를 뽑듯 쳐내면 위층이 내려앉는다.
        /// </summary>
        static void BuildSlabJenga(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 1.8f, p);
            float y = PedestalTop;
            int layers = Balance.Grow(info.level, 7, 30, 9);
            const float len = 1.5f, th = 0.3f, w = 0.48f;
            for (int l = 0; l < layers; l++)
            {
                bool alongX = l % 2 == 0;
                for (int i = -1; i <= 1; i++)
                {
                    float off = i * 0.5f;
                    bool log = (l % 3 == 1 && i == 0) || (l % 3 == 2 && i != 0);
                    Vector3 pos = alongX ? new Vector3(0f, y + th * 0.5f, off) : new Vector3(off, y + th * 0.5f, 0f);
                    if (log)
                    {
                        Quaternion rot = alongX ? Quaternion.Euler(0, 0, 90) : Quaternion.Euler(90, 0, 0);
                        MakeBlock(root, PrimitiveType.Cylinder, BlockKind.Log, pos, new Vector3(th, len * 0.5f, th), rot, WoodCol, 1.0f, info.blocks, false, true);
                    }
                    else
                    {
                        Vector3 scale = alongX ? new Vector3(len, th, w) : new Vector3(w, th, len);
                        MakeBlock(root, PrimitiveType.Cube, BlockKind.Ice, pos, scale, Quaternion.identity, IceCol, 0.9f, info.blocks);
                    }
                }
                y += th;
            }
            // 꼭대기: 보라 상자 2개 + 긴 사탕
            PurpleRow(root, new Vector3(0f, y, 0f), 2, info.blocks);
            MakeUnit(root, BlockKind.Candy, new Vector3(0f, y + DU, 0f), true, p.c, info.blocks, DU);
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
            var levelRoot = root;
            root = new GameObject("Structure").transform;
            root.SetParent(levelRoot);

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

            Physics.SyncTransforms();
            info.fitScale = FitToScreen(root, levelRoot, cam, info.blocks, 0f, p);

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

            PreSettle(info.blocks);
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
                PreSettle(blocks);
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
