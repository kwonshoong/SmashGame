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
        public const float PedestalTop = 0.98f;   // 상판 윗면 높이. 땅(-1.5)에서 다리 2.48 (1.6이었을 때 3.1의 80%)
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
            pedestalLegOffsets.Clear();
            independentPedestals = false;
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

        /// <summary>
        /// 받침대 하나. legs: 상판을 받치는 기둥 수(1·3·5, 레퍼런스 202·243·252처럼 넓은 판 아래 다리 여러 개).
        /// raise: 상판을 기본 높이보다 올림(가운데가 높은 3단 배치 등). 블록은 PedestalTop + raise 위에 놓아야 한다.
        /// </summary>
        static void Pedestal(Transform parent, Vector3 center, float radius, Palette p, bool square = false, int legs = 1, float raise = 0f, float depth = 0f)
        {
            // 상판 앞뒤 깊이: 지정이 없으면 반지름 비례. 구조물 발자국보다 크게 두면 쓰러진 블록이 상판에 쌓여 떨어지지 않는다
            float dz = depth > 0f ? depth : radius * (square ? PedestalDepthSquare : PedestalDepthRound);
            var gold = Materials.Get(new Color(1f, 0.78f, 0.25f), true, true);
            var purpleDark = Materials.Get(p.pedestal * 0.75f, true);
            float plateY = PedestalTop + raise;

            // 받침대 묶음(상판·테두리·기둥·발): 움직이는 받침대는 이 묶음째 kinematic으로 움직인다
            var group = new GameObject("PedestalGroup");
            group.transform.SetParent(parent);
            group.transform.position = new Vector3(center.x, plateY, center.z);   // 회전축 = 상판 중심
            var root = group.transform;
            pedestalGroups.Add(group);
            // 다리 x 오프셋 (화면 맞춤 후 기둥을 다시 세울 때도 사용)
            var offs = new List<float>();
            if (legs <= 1) offs.Add(0f);
            else for (int i = 0; i < legs; i++) offs.Add(Mathf.Lerp(-(radius - 0.45f), radius - 0.45f, (float)i / (legs - 1)));
            pedestalLegOffsets.Add(offs);

            // 상판(콜라이더 있음) — 위치·두께는 물리와 맞물려 있으므로 유지
            var top = GameObject.CreatePrimitive(square ? PrimitiveType.Cube : PrimitiveType.Cylinder);
            top.name = "PedestalTop";
            top.transform.SetParent(root);
            top.transform.position = new Vector3(center.x, plateY - 0.08f, center.z);
            // 앞뒤 깊이는 좌우 폭보다 얕게 (원형은 타원, 사각형은 가로로 긴 판). 구조물 깊이(원진 1.43, 통나무 1.0, 원통 다발 1.2)는 다 들어간다.
            top.transform.localScale = square ? new Vector3(radius * 2f, 0.16f, dz) : new Vector3(radius * 2f, 0.08f, dz);
            top.GetComponent<Renderer>().material = Materials.Get(p.pedestal, true);
            if (!square) FlattenCollider(top, false);   // 사각 상판은 기본 BoxCollider가 이미 평평하다 (움직이는 받침대에선 메시보다 접촉이 안정적)
            RoundedMesh.Apply(top, 0.03f);
            PedestalColliders.Add(top.GetComponent<Collider>());

            // 상판 아래 금색 테두리 + 진한 보라 밑판(두께감)
            float ringY = plateY - 0.16f - 0.03f;
            if (square)
            {
                Deco(PrimitiveType.Cube, root, "PedestalRim", new Vector3(center.x, ringY, center.z), new Vector3(radius * 2f + 0.06f, 0.06f, dz + 0.06f), gold, 0.02f);
                Deco(PrimitiveType.Cube, root, "PedestalUnder", new Vector3(center.x, ringY - 0.09f, center.z), new Vector3(radius * 2f - 0.1f, 0.12f, dz - 0.1f), purpleDark, 0.03f);
            }
            else
            {
                Deco(PrimitiveType.Cylinder, root, "PedestalRim", new Vector3(center.x, ringY, center.z), new Vector3(radius * 2f + 0.06f, 0.03f, dz + 0.06f), gold, 0.02f);
                Deco(PrimitiveType.Cylinder, root, "PedestalUnder", new Vector3(center.x, ringY - 0.09f, center.z), new Vector3(radius * 2f - 0.1f, 0.06f, dz - 0.1f), purpleDark, 0.03f);
            }

            pedestalCenters.Add(center);
            foreach (var ox in offs) PedestalColumn(root, new Vector3(center.x + ox, 0f, center.z), plateY - 0.16f, p);
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
            // 받침 발: 넓은 둥근 판 두 장 (지름 1.2/1.6 → 0.84/1.12, 30% 축소)
            Deco(PrimitiveType.Cylinder, ground, "PedestalFoot", new Vector3(center.x, -1.3f, center.z), new Vector3(0.84f, 0.12f, 0.84f) * sizeMul, Materials.Get(p.pedestal, true), 0.06f);
            Deco(PrimitiveType.Cylinder, ground, "PedestalFoot2", new Vector3(center.x, -1.45f, center.z), new Vector3(1.12f, 0.08f, 1.12f) * sizeMul, purpleDark, 0.05f);
        }

        /// <summary>이번 빌드에서 만든 받침대 중심들 (화면 맞춤 축소 후 기둥을 다시 세울 때 사용)</summary>
        static readonly List<Vector3> pedestalCenters = new();
        static readonly List<GameObject> pedestalGroups = new();
        static readonly List<List<float>> pedestalLegOffsets = new();
        /// <summary>이번 구조물의 받침대들이 서로 독립된 탑인가(승강 위상을 어긋나게 해도 되는가). 빌더가 설정</summary>
        static bool independentPedestals;

        public const float PlateMargin = 0.22f;   // 상판이 블록 발자국보다 밖으로 나오는 여유

        /// <summary>
        /// 상판을 그 위에 놓인 블록의 발자국에 맞춰 줄인다 (줄이기만 한다). 상판이 블록보다 넓으면 쓰러진 블록이 상판 위에 쌓여
        /// 떨어지지 않아 몇 발로 끝나거나 반대로 끝이 안 나고, 레퍼런스처럼 "블록이 깔린 만큼"의 테이블이 보기에도 맞다.
        /// 앞뒤(z)는 항상, 좌우(x)는 다리가 하나인 받침대만 줄인다 (다리 여럿은 폭에 맞춰 세워져 있다).
        /// 구조물 루트가 원점(z=0)에 있을 때, 화면 맞춤 전에 호출한다.
        /// </summary>
        static void FitPlatesToBlocks(List<Block> blocks)
        {
            for (int gi = 0; gi < pedestalGroups.Count; gi++)
            {
                var g = pedestalGroups[gi].transform;
                var top = g.Find("PedestalTop");
                if (top == null) continue;
                float cx = g.position.x, cz = g.position.z, plateY = g.position.y;
                float halfX = top.localScale.x * 0.5f, halfZ = top.localScale.z * 0.5f;
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                int n = 0;
                foreach (var b in blocks)
                {
                    var c = b.GetComponent<Collider>(); if (c == null) continue;
                    var bd = c.bounds;
                    if (bd.min.y > plateY + 0.12f) continue;                       // 상판에 직접 놓인 블록만
                    if (Mathf.Abs(bd.center.x - cx) > halfX || Mathf.Abs(bd.center.z - cz) > halfZ) continue;   // 이 상판 위
                    minX = Mathf.Min(minX, bd.min.x); maxX = Mathf.Max(maxX, bd.max.x);
                    minZ = Mathf.Min(minZ, bd.min.z); maxZ = Mathf.Max(maxZ, bd.max.z);
                    n++;
                }
                if (n == 0) continue;
                bool singleLeg = pedestalLegOffsets[gi].Count <= 1;
                float newHalfX = singleLeg ? Mathf.Min(halfX, Mathf.Max(0.45f, Mathf.Max(maxX - cx, cx - minX) + PlateMargin)) : halfX;
                float newHalfZ = Mathf.Min(halfZ, Mathf.Max(0.4f, (maxZ - minZ) * 0.5f + PlateMargin));
                float zc = (minZ + maxZ) * 0.5f;
                // 상판이 기둥(z = cz) 위에 남도록 중심 이동을 제한
                zc = Mathf.Clamp(zc, cz - Mathf.Max(0f, newHalfZ - 0.25f), cz + Mathf.Max(0f, newHalfZ - 0.25f));
                if (newHalfX >= halfX - 0.01f && newHalfZ >= halfZ - 0.01f) continue;
                float sx = newHalfX / halfX, sz = newHalfZ / halfZ;
                foreach (var name in new[] { "PedestalTop", "PedestalRim", "PedestalUnder" })
                {
                    var t = g.Find(name); if (t == null) continue;
                    var ls = t.localScale; t.localScale = new Vector3(ls.x * sx, ls.y, ls.z * sz);
                    var pos = t.position; t.position = new Vector3(pos.x, pos.y, zc);
                }
            }
        }

        /// <summary>
        /// 구조물(받침대 상판 포함)이 화면 좌우를 벗어나면 상판 높이를 축으로 통째로 균일 축소한다. 받침대 기둥·발은 축소하지 않고
        /// 땅까지 닿게 다시 세운다. 블록 질량은 규격 규칙(크기 제곱)에 맞춰 s²배. 세로 폰(9:19.5) 기준으로 맞추므로 어느 기기에서도 잘리지 않는다.
        /// </summary>
        static float FitToScreen(Transform structRoot, Transform levelRoot, Camera cam, List<Block> blocks, float rangeZ, Palette p)
        {
            float halfW = 0f, topY = PedestalTop;
            foreach (var r in structRoot.GetComponentsInChildren<Renderer>())
            {
                if (r.name.StartsWith("Column") || r.name.StartsWith("PedestalFoot") || r.name == "PedestalColumn" || r.name == "Glue") continue;
                halfW = Mathf.Max(halfW, Mathf.Abs(r.bounds.min.x), Mathf.Abs(r.bounds.max.x));
                topY = Mathf.Max(topY, r.bounds.max.y);
            }
            float dist = rangeZ - cam.transform.position.z;
            float aspect = Mathf.Min(cam.aspect, 9f / 19.5f);   // 가장 좁은 폰 기준
            float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float allowed = dist * Mathf.Tan(halfFov) * aspect * FitMargin;
            // 세로 한계: 화면 세로 반높이의 FitTopMargin까지 (HUD가 위를 가림). 상판을 축으로 키우므로 (top - 상판) 이 늘어난다
            float centerY = cam.transform.position.y - dist * Mathf.Tan(cam.transform.eulerAngles.x * Mathf.Deg2Rad);
            float allowedTop = centerY + dist * Mathf.Tan(halfFov) * FitTopMargin;
            // 레퍼런스처럼 구조물이 화면 폭을 꽉 채우도록: 좁으면 키우고(최대 FitMaxUp) 넓으면 줄인다
            float s = allowed / Mathf.Max(0.3f, halfW);
            if (topY > PedestalTop + 0.05f) s = Mathf.Min(s, (allowedTop - PedestalTop) / (topY - PedestalTop));
            s = Mathf.Clamp(s, 0.3f, FitMaxUp);
            if (Mathf.Abs(s - 1f) < 0.01f) return 1f;

            // 상판 윗면(y = PedestalTop) 높이를 축으로 축소: 상판 위치는 그대로, 폭·블록만 작아진다
            structRoot.localScale = Vector3.one * s;
            structRoot.position = new Vector3(0f, PedestalTop * (1f - s), rangeZ);
            // 축소는 규격 규칙대로 s²(작은 블록은 가볍게). 확대는 질량을 그대로 둔다: 화면 맞춤 확대는 "보이는 크기"를 맞추는 것이지
            // 난이도를 올리려는 게 아니고, 커진 블록은 그 자체로 넘어뜨리는 데 더 많은 밀림이 필요하다 (봇 실측: s 배율 질량에서도 장거리 3종 실패)
            float massMul = s < 1f ? s * s : 1f;
            foreach (var b in blocks) { var rb = b.GetComponent<Rigidbody>(); if (rb != null) rb.mass *= massMul; }

            // 기둥·발은 버리고 각 받침대 묶음 안에 원래 크기(1/s)로 땅까지 닿게 다시 세운다 (묶음이 움직이면 같이 움직인다)
            var doomed = new List<GameObject>();
            foreach (var t in structRoot.GetComponentsInChildren<Transform>())
                if (t.name == "PedestalColumn" || t.name.StartsWith("Column") || t.name.StartsWith("PedestalFoot")) doomed.Add(t.gameObject);
            foreach (var g in doomed) { var c = g.GetComponent<Collider>(); if (c != null) PedestalColliders.Remove(c); Object.DestroyImmediate(g); }
            for (int gi = 0; gi < pedestalGroups.Count; gi++)
            {
                var g = pedestalGroups[gi];
                var wc = g.transform.position;   // 축소 후 상판 중심(월드)
                foreach (var ox in pedestalLegOffsets[gi])
                    PedestalColumn(g.transform, new Vector3(wc.x + ox * s, 0f, wc.z), wc.y - 0.16f * s, p, 1f / s);
            }
            Physics.SyncTransforms();
            return s;
        }
        public const float FitMargin = 0.9f;    // 화면 반폭의 90%까지 채운다 (레퍼런스: 구조물이 폭의 85~90%)
        public const float FitTopMargin = 0.7f; // 구조물 꼭대기는 화면 세로 반높이의 70%까지 (위쪽 HUD 여유)
        public const float FitMaxUp = 1.1f;     // 확대는 미세 조정만: 블록 크기가 구조물마다 달라지지 않게 (폭 채우기는 구조물 설계가 맡는다)


        /// <summary>
        /// 움직이는 받침대 적용 (Balance.PedestalMotionKind). 받침대가 여럿이고 서로 독립된 탑(삼중 받침대)이면 위상을 어긋나게,
        /// 하나의 구조물이 여러 받침대에 걸쳐 있으면(얼음 성문·통나무 다리) 같은 위상으로 오르내려 구조물이 찢어지지 않는다.
        /// </summary>
        static void ApplyPedestalMotion(int level, int type, LevelInfo info)
        {
            var kind = Balance.PedestalMotionKind(level);
            bool independent = independentPedestals || type == 5;
            // 독립 받침대가 여럿인 구조물(쌍둥이 탑·삼중 받침대 등)은 25레벨부터 기본으로 번갈아 오르내린다 (레퍼런스 206·215·228·297)
            if (kind == Balance.MotionKind.None && independent && pedestalGroups.Count >= 2 && level >= Balance.MultiPedestalBobFromLevel)
                kind = Balance.MotionKind.Bob;
            if (kind == Balance.MotionKind.None) return;
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
        /// <summary>일반 레벨용 규격(월드). 레퍼런스 기준 블록 한 칸 = 화면 폭의 약 1/9~1/10. 모든 구조물이 이 크기를 쓰고,
        /// 화면 맞춤(FitToScreen)은 0.9~1.1배 미세 조정만 하므로 레벨마다 블록 크기가 들쭉날쭉하지 않다.</summary>
        public const float DU = 0.45f;
        /// <summary>규격 블록 옆 간격(0.01 틈 포함)</summary>
        public const float DS = DU + 0.01f;

        static bool IsCylinderKind(BlockKind k) => k == BlockKind.Cylinder || k == BlockKind.Candy || k == BlockKind.Log || k == BlockKind.Stone;

        /// <summary>
        /// 규격 블록 하나를 만든다. basePos는 블록 바닥 중심. cells = 높이 칸 수(1~3, 가로·세로는 항상 1칸). 질량은 칸 수에 비례.
        /// 소재가 원통 계열이면 원기둥, 아니면 육면체로 만들어진다.
        /// </summary>
        static Block MakeUnit(Transform root, BlockKind kind, Vector3 basePos, int cells, Color color, List<Block> list, float unit = Unit)
        {
            cells = Mathf.Clamp(cells, 1, 3);
            float h = unit * cells;
            bool cyl = IsCylinderKind(kind);
            var prim = cyl ? PrimitiveType.Cylinder : PrimitiveType.Cube;
            // 옆으로는 0.01 틈(이웃과 마찰로 엉기지 않게), 위아래는 틈 없이 정확히 맞닿게. 세로 틈을 두면 쌓인 블록이 살짝 떠 있다가
            // 첫 발에 깨어나며 줄마다 0.005씩 내려앉아(10줄이면 0.1) 구조물이 주저앉는 것처럼 보인다.
            Vector3 scale = cyl ? new Vector3(unit - 0.01f, h * 0.5f, unit - 0.01f) : new Vector3(unit - 0.01f, h, unit - 0.01f);
            return MakeBlock(root, prim, kind, basePos + Vector3.up * h * 0.5f, scale, Quaternion.identity, color, MassFor(kind) * cells * UnitMass(unit), list, cells > 1);
        }
        static Block MakeUnit(Transform root, BlockKind kind, Vector3 basePos, bool tall, Color color, List<Block> list, float unit = Unit)
            => MakeUnit(root, kind, basePos, tall ? 2 : 1, color, list, unit);

        /// <summary>
        /// 한 열(column)을 아래에서 위로 rows칸 채운다. 남은 칸이 2 이상이면 tallChance 확률로 긴 블록(2칸, 3칸 여유가 있으면 반은 3칸), 아니면 한 칸.
        /// pick(rowIndex, cells) 으로 칸마다 소재·색을 정한다. rowIndex는 블록 바닥 칸 번호.
        /// </summary>
        static void FillColumn(Transform root, System.Random rng, Vector3 basePos, int rows, float tallChance, System.Func<int, int, (BlockKind kind, Color color)> pick, List<Block> list, float unit = Unit)
        {
            int j = 0;
            while (j < rows)
            {
                int cells = 1;
                if (rows - j >= 2 && rng.NextDouble() < tallChance) cells = (rows - j >= 3 && rng.Next(2) == 0) ? 3 : 2;
                var (kind, color) = pick(j, cells);
                MakeUnit(root, kind, basePos + Vector3.up * (j * unit), cells, color, list, unit);
                j += cells;
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

            // 레벨 구간별로 나올 수 있는 구조물 목록. 원통 다발·판자 선반·통나무 탑은 한 발에 무너지는 극초반용이라 12레벨부터 제외
            var allowed = Balance.StructurePool(level);
            int T = allowed.Length;
            // 곱수는 T와 서로소여야 모든 종류가 고르게 나온다 (T=6,9,12 → 5, T=15 → 7)
            int mult = T % 5 == 0 ? 7 : 5;
            int pick = info.hard ? (level / 10 + 6) % T : (level * mult + rng.Next(0, 3)) % T;
            int type = allowed[pick];
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
                case 14: BuildSlabJenga(root, rng, p, info); break;
                case 15: BuildTwinCylinderTowers(root, rng, p, info); break;
                case 16: BuildCenterHighPyramid(root, rng, p, info); break;
                case 17: BuildTemple(root, rng, p, info); break;
                case 18: BuildRoundCylinderTower(root, rng, p, info); break;
                case 19: BuildFrame8(root, rng, p, info); break;
                case 20: BuildDiamondTower(root, rng, p, info); break;
                default: BuildCrateWallWithSide(root, rng, p, info); break;
            }
            Physics.SyncTransforms();
            FitPlatesToBlocks(info.blocks);
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
            info.startBalls = Balance.StartBalls(level, info.hard, info.blocks.Count) + Balance.RangeExtraBalls(info.rangeTier) + Balance.StructureExtraBalls(type) + (info.motion != Balance.MotionKind.None ? Balance.MotionExtraBalls : 0);
            info.structureName = type switch
            {
                0 => "원통 다발", 1 => "큐브 격자", 2 => "원통 선반", 3 => "통나무 탑", 4 => "얼음 벽", 5 => "삼중 받침대",
                6 => "피라미드", 7 => "요새", 8 => "성문", 9 => "쌍둥이 탑", 10 => "계단", 11 => "돌기둥 원진",
                12 => "얼음 성문", 13 => "통나무 벽", 14 => "얼음 격자 탑",
                15 => "쌍둥이 원통 탑", 16 => "가운데 높은 피라미드", 17 => "신전", 18 => "둥근 원통 탑", 19 => "세 탑", 20 => "마름모 무늬 벽", _ => "상자 벽과 곁탑"
            };
            return info;
        }

        // ---------------- 구조물 22종 ----------------
        // 규칙: 모든 블록은 가로·세로 1칸(DU), 높이 1~3칸. 긴 보·판자·얇은 판은 쓰지 않는다 — 한 부재를 치면 위가 통째로 쏟아져
        // 몇 발에 끝나던(통나무 다리·얼음 성문) 문제의 원인. 대신 열(column)을 촘촘히 세워 하나씩 밀어내야 한다.

        static int Depth(LevelInfo info) => info.level >= Balance.DeepStructuresFromLevel ? 3 : info.level >= Balance.NewStructuresFromLevel ? 2 : 1;   // 8레벨부터 두 겹(레퍼런스), 60레벨부터 세 겹. 튜토리얼 구간은 한 겹
        static bool Heavy(LevelInfo info) => info.level >= Balance.HeavyStructuresFromLevel;
        static readonly Color StoneCol = new Color(0.9f, 0.88f, 0.82f);
        static readonly Color CrateCol = new Color(0.65f, 0.42f, 0.2f);
        static readonly Color IceCol = new Color(0.6f, 0.9f, 1f);
        static readonly Color WoodCol = new Color(0.6f, 0.38f, 0.18f);
        static readonly Color PurpleCol = new Color(0.5f, 0.22f, 0.8f);
        static readonly Color PinkCol = new Color(0.95f, 0.35f, 0.75f);
        static readonly Color BlueCol = new Color(0.25f, 0.5f, 0.95f);
        static readonly Color RedCol = new Color(0.9f, 0.2f, 0.25f);
        static readonly Color GoldCol = new Color(0.95f, 0.78f, 0.2f);
        static readonly Color MarbleCol = new Color(0.93f, 0.92f, 0.88f);
        static readonly Color GreenCol = new Color(0.3f, 0.75f, 0.3f);
        /// <summary>무거운 바닥용 소재: 30레벨부터 돌, 그 전엔 상자</summary>
        static (BlockKind, Color) Base(LevelInfo info) => Heavy(info) ? (BlockKind.Stone, StoneCol) : (BlockKind.Crate, CrateCol);
        /// <summary>기둥용 소재: 30레벨부터 돌기둥, 그 전엔 원통</summary>
        static (BlockKind, Color) Pillar(LevelInfo info, Color light) => Heavy(info) ? (BlockKind.Stone, StoneCol) : (BlockKind.Cylinder, light);

        /// <summary>격자 채우기: (cx, cz) 중심으로 cols×depth 열을 rows칸씩. pick(i, d, j, cells) → 소재·색. 열마다 1~3칸 블록을 섞는다.</summary>
        static void Grid(Transform root, System.Random rng, float cx, float cz, int cols, int depth, int rows, float tallChance,
            System.Func<int, int, int, int, (BlockKind kind, Color color)> pick, List<Block> list, float spacing = 0f)
        {
            float sp = spacing > 0f ? spacing : DS;
            for (int i = 0; i < cols; i++)
                for (int d = 0; d < depth; d++)
                {
                    int ii = i, dd = d;
                    FillColumn(root, rng, new Vector3(cx + (i - (cols - 1) * 0.5f) * sp, PedestalTop, cz + (d - (depth - 1) * 0.5f) * sp), rows, tallChance,
                        (j, cells) => pick(ii, dd, j, cells), list, DU);
                }
        }
        /// <summary>격자 채우기(기준 높이 지정)</summary>
        static void GridAt(Transform root, System.Random rng, Vector3 basePos, int cols, int depth, int rows, float tallChance,
            System.Func<int, int, int, int, (BlockKind kind, Color color)> pick, List<Block> list)
        {
            for (int i = 0; i < cols; i++)
                for (int d = 0; d < depth; d++)
                {
                    int ii = i, dd = d;
                    FillColumn(root, rng, basePos + new Vector3((i - (cols - 1) * 0.5f) * DS, 0f, (d - (depth - 1) * 0.5f) * DS), rows, tallChance,
                        (j, cells) => pick(ii, dd, j, cells), list, DU);
                }
        }
        /// <summary>한 줄(x 방향) n개, 앞뒤 depth겹. basePos는 줄 가운데 바닥.</summary>
        static void Row(Transform root, Vector3 basePos, int n, int depth, System.Func<int, int, (BlockKind kind, Color color)> pick, List<Block> list, int cells = 1)
        {
            for (int i = 0; i < n; i++)
                for (int d = 0; d < depth; d++)
                {
                    var (kind, color) = pick(i, d);
                    MakeUnit(root, kind, basePos + new Vector3((i - (n - 1) * 0.5f) * DS, 0f, (d - (depth - 1) * 0.5f) * DS), cells, color, list, DU);
                }
        }
        /// <summary>보라 상자 한 줄(n개). basePos는 줄 가운데 바닥.</summary>
        static void PurpleRow(Transform root, Vector3 basePos, int n, List<Block> list, int depth = 1)
            => Row(root, basePos, n, depth, (i, d) => (BlockKind.Cube, PurpleCol), list);

        /// <summary>0 원통 다발: 원통·사탕 열을 격자로 4칸 높이. 극초반용.</summary>
        static void BuildCylinderCluster(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            int cols = Balance.Grow(info.level, 4, 20, 6), depth = Balance.Grow(info.level, 3, 30, 4);
            float sp = DU + 0.1f;
            Pedestal(root, Vector3.zero, Mathf.Max(1.7f, cols * sp * 0.5f + 0.4f), p, false, 1, 0f, depth * sp + 0.5f);
            Grid(root, rng, 0f, 0f, cols, depth, info.level >= 40 ? 6 : 4, 0.6f, (i, d, j, cells) =>
            {
                var kind = rng.Next(3) == 0 ? BlockKind.Candy : BlockKind.Cylinder;
                return (kind, kind == BlockKind.Candy ? p.c : ((i + d + j) % 2 == 0 ? p.a : p.b));
            }, info.blocks, sp);
        }

        /// <summary>1 큐브 격자: 7~9열 × 6~9칸 큐브 벽, 두 겹.</summary>
        static void BuildCubeGrid(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            int w = Balance.Grow(info.level, 7, 20, 9), h = Balance.Grow(info.level, 6, 12, 9), depth = Mathf.Min(2, Depth(info));
            Pedestal(root, Vector3.zero, w * DS * 0.5f + 0.15f, p, true, Balance.PedestalLegs(info.level), 0f, depth * DS + 0.5f);
            Grid(root, rng, 0f, 0f, w, depth, h, 0.35f, (i, d, j, cells) =>
            {
                var kind = rng.Next(9) == 0 ? BlockKind.Crate : BlockKind.Cube;
                return (kind, kind == BlockKind.Crate ? CrateCol : (((i + j + d) % 3 == 0) ? p.b : p.a));
            }, info.blocks);
        }

        /// <summary>2 원통 선반: 원통(사막은 돌) 기둥 6열 × 2겹, 5칸. 위에 사탕. 극초반용.</summary>
        static void BuildFrameShelf(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 1.8f, p, false, 1, 0f, 1.5f);
            Grid(root, rng, 0f, 0f, 6, 2, 5, 0.5f, (i, d, j, cells) =>
            {
                if (j >= 4) return (BlockKind.Candy, p.c);
                var kind = info.theme == Theme.Desert ? BlockKind.Stone : BlockKind.Cylinder;
                return (kind, kind == BlockKind.Stone ? StoneCol : ((i + j + d) % 2 == 0 ? p.a : p.b));
            }, info.blocks, 0.55f);
        }

        /// <summary>3 통나무 탑: 세운 통나무 다발 4×3, 6칸. 사이사이 상자, 꼭대기 큐브. 극초반용.</summary>
        static void BuildLogTower(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 1.5f, p, false, 1, 0f, 1.8f);
            Grid(root, rng, 0f, 0f, 4, 3, 6, 0.7f, (i, d, j, cells) =>
            {
                if (j >= 5) return (BlockKind.Cube, p.b);
                bool crate = (i + d) % 3 == 1 && j % 3 == 2;
                return crate ? (BlockKind.Crate, CrateCol) : (BlockKind.Log, WoodCol);
            }, info.blocks);
        }

        /// <summary>4 얼음 벽: 얼음 큐브 벽에 상자 무늬, 두 겹, 가운데 긴 사탕.</summary>
        static void BuildIceWall(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            int w = Balance.Grow(info.level, 7, 20, 9), h = Balance.Grow(info.level, 6, 14, 9), depth = Mathf.Min(2, Depth(info));
            Pedestal(root, Vector3.zero, w * DS * 0.5f + 0.15f, p, true, Balance.PedestalLegs(info.level), 0f, depth * DS + 0.5f);
            Grid(root, rng, 0f, 0f, w, depth, h, 0.3f, (i, d, j, cells) =>
            {
                bool isCrate = (j >= 5 && (i + j) % 2 == 0) || (j == 1 && i == 3);
                return (isCrate ? BlockKind.Crate : BlockKind.Ice, isCrate ? CrateCol : IceCol);
            }, info.blocks);
            MakeUnit(root, BlockKind.Candy, new Vector3(0, PedestalTop + h * DU, 0), 2, p.c, info.blocks, DU);
        }

        /// <summary>5 삼중 받침대: 독립 받침대 2~3개 위에 2×2 원통(돌) 탑, 꼭대기 사탕.</summary>
        static void BuildMultiPedestal(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            int count = rng.Next(2) == 0 ? 2 : 3;
            float spacing = count == 3 ? 1.5f : 2.6f;   // 바깥 받침대 가장자리가 화면 폭(반폭 2.2) 안에 들게
            for (int k = 0; k < count; k++)
            {
                float cx = (k - (count - 1) * 0.5f) * spacing;
                Pedestal(root, new Vector3(cx, 0, 0), 0.7f, p);
                int rows = 6 + rng.Next(3), kk = k;
                Grid(root, rng, cx, 0f, 2, 2, rows, 0.55f, (i, d, j, cells) =>
                {
                    bool top = j + cells >= rows;
                    var kind = top ? BlockKind.Candy : (info.theme == Theme.Desert ? BlockKind.Stone : BlockKind.Cylinder);
                    Color col = kind == BlockKind.Candy ? p.c : kind == BlockKind.Stone ? StoneCol : ((j + kk + i + d) % 2 == 0 ? p.a : p.b);
                    return (kind, col);
                }, info.blocks);
            }
        }

        /// <summary>6 피라미드: 9열, 가운데가 높고 양끝이 낮은 두 겹. 아래층은 돌·상자, 위층은 큐브.</summary>
        static void BuildPyramid(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            int cols = 9, depth = Mathf.Min(2, Depth(info));   // 9열 × 0.45 = 4.05 (화면 폭)
            Pedestal(root, Vector3.zero, cols * DS * 0.5f + 0.15f, p, true, Balance.PedestalLegs(info.level), 0f, depth * DS + 0.5f);
            int center = cols / 2;
            for (int i = 0; i < cols; i++)
            {
                int rows = Mathf.Max(2, Mathf.FloorToInt(8.6f - Mathf.Abs(i - center) * 0.8f));   // 5..8..5
                for (int d = 0; d < depth; d++)
                {
                    int ii = i;
                    FillColumn(root, rng, new Vector3((i - (cols - 1) * 0.5f) * DS, PedestalTop, (d - (depth - 1) * 0.5f) * DS), rows, 0.4f, (j, cells) =>
                    {
                        if (j == 0) return Base(info);
                        if (j == 1 && rng.Next(2) == 0) return (BlockKind.Crate, CrateCol);
                        return (BlockKind.Cube, (ii + j) % 2 == 0 ? p.a : p.b);
                    }, info.blocks, DU);
                }
            }
        }

        /// <summary>7 요새: 앞쪽 낮은 성벽(두 겹)이 뒤쪽 높은 본성(큐브·얼음, 두 겹)을 가린다.</summary>
        static void BuildFortress(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            const int wall = 9;
            Pedestal(root, Vector3.zero, wall * DS * 0.5f + 0.15f, p, true, Balance.PedestalLegs(info.level), 0f, 2.0f);
            // 앞 성벽: 9열 × 3칸 × 두 겹 (z -0.6, -0.14). 바닥 줄만 무거운 소재
            GridAt(root, rng, new Vector3(0f, PedestalTop, -0.37f), wall, 2, 3, 0.3f, (i, d, j, cells) =>
                j == 0 ? ((i + d) % 3 == 1 ? (BlockKind.Crate, CrateCol) : Base(info)) : (BlockKind.Cube, (i + j + d) % 2 == 0 ? p.b : p.a), info.blocks);
            // 본성: 6열 × 8칸 × 두 겹 (z 0.3, 0.76), 큐브 + 얼음
            GridAt(root, rng, new Vector3(0f, PedestalTop, 0.53f), 6, 2, 8, 0.4f, (i, d, j, cells) =>
                (j >= 6 && (i + j) % 2 == 0) ? (BlockKind.Ice, IceCol) : (BlockKind.Cube, (i + j + d) % 2 == 0 ? p.a : p.b), info.blocks);
            MakeUnit(root, BlockKind.Candy, new Vector3(0f, PedestalTop + 8 * DU, 0.53f), 2, p.c, info.blocks, DU);
        }

        /// <summary>8 성문: 두꺼운 기둥 탑 두 개(2열×깊이×8칸) 사이에 낮은 상자 벽(3열×4칸). 기둥 위 큐브·사탕.</summary>
        static void BuildGate(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            int depth = Depth(info);
            Pedestal(root, Vector3.zero, 2.0f, p, true, Balance.PedestalLegs(info.level), 0f, depth * DS + 0.6f);
            foreach (float x in new[] { -1.25f, 1.25f })
            {
                Grid(root, rng, x, 0f, 2, depth, 8, 0.8f, (i, d, j, cells) => Pillar(info, (j + i) % 2 == 0 ? p.a : p.b), info.blocks);
                Row(root, new Vector3(x, PedestalTop + 8 * DU, 0f), 2, depth, (i, d) => (BlockKind.Cube, p.b), info.blocks);
                MakeUnit(root, BlockKind.Candy, new Vector3(x, PedestalTop + 9 * DU, 0f), 1, p.c, info.blocks, DU);
            }
            Grid(root, rng, 0f, 0f, 3, depth, 4, 0.5f, (i, d, j, cells) => (BlockKind.Crate, CrateCol), info.blocks);
        }

        /// <summary>9 쌍둥이 탑: 세 열짜리 탑 두 개(두 겹), 꼭대기에 원통·사탕.</summary>
        static void BuildTwinTowers(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            int depth = Mathf.Min(2, Depth(info));
            Pedestal(root, Vector3.zero, 2.2f, p, true, Balance.PedestalLegs(info.level), 0f, depth * DS + 0.6f);
            int rows = Balance.Grow(info.level, 7, 40, 8);
            foreach (float cx in new[] { -1.25f, 1.25f })
            {
                Grid(root, rng, cx, 0f, 3, depth, rows, 0.5f, (i, d, j, cells) =>
                    j < 2 ? Base(info) : (BlockKind.Cube, (i + j + d) % 2 == 0 ? p.a : p.b), info.blocks);
                Row(root, new Vector3(cx, PedestalTop + rows * DU, 0f), 3, 1, (i, d) => i == 1 ? (BlockKind.Candy, p.c) : (BlockKind.Cylinder, p.a), info.blocks, 2);
            }
        }

        /// <summary>10 계단: 9열, 한쪽부터 1~9칸으로 높아지는 두 겹. 낮은 쪽은 무거운 돌, 높은 쪽은 큐브·얼음.</summary>
        static void BuildStaircase(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            const int cols = 9;
            int depth = Mathf.Min(2, Depth(info));
            Pedestal(root, Vector3.zero, cols * DS * 0.5f + 0.2f, p, true, Balance.PedestalLegs(info.level), 0f, depth * DS + 0.5f);
            bool flip = rng.Next(2) == 0;
            for (int i = 0; i < cols; i++)
            {
                int col = flip ? cols - 1 - i : i;
                for (int d = 0; d < depth; d++)
                {
                    int ii = i;
                    FillColumn(root, rng, new Vector3((col - (cols - 1) * 0.5f) * DS, PedestalTop, (d - (depth - 1) * 0.5f) * DS), i + 1, 0.45f, (j, cells) =>
                    {
                        if (ii < 4) return Base(info);
                        if (j >= 7 && (ii + j) % 2 == 0) return (BlockKind.Ice, IceCol);
                        return (BlockKind.Cube, (ii + j) % 2 == 0 ? p.a : p.b);
                    }, info.blocks, DU);
                }
            }
        }

        /// <summary>11 돌기둥 원진: 기둥 10개가 원을 그리고 안쪽에 사탕 탑. 기둥 위 큐브.</summary>
        static void BuildRing(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 1.7f, p);
            const int n = 10; const float r = 1.25f;
            for (int k = 0; k < n; k++)
            {
                float a = (k + 0.5f) / n * Mathf.PI * 2f;
                var pos = new Vector3(Mathf.Cos(a) * r, PedestalTop, Mathf.Sin(a) * r);
                int kk = k;
                FillColumn(root, rng, pos, 7, 0.7f, (j, cells) => Pillar(info, (kk + j) % 2 == 0 ? p.a : p.b), info.blocks, DU);
                MakeUnit(root, BlockKind.Cube, pos + Vector3.up * (7 * DU), 1, k % 2 == 0 ? p.a : p.b, info.blocks, DU);
            }
            FillColumn(root, rng, new Vector3(0, PedestalTop, 0), 6, 0.5f, (j, cells) => (BlockKind.Candy, p.c), info.blocks, DU);
            for (int k = 0; k < 4; k++)
            {
                float a = k / 4f * Mathf.PI * 2f + 0.4f;
                FillColumn(root, rng, new Vector3(Mathf.Cos(a) * 0.62f, PedestalTop, Mathf.Sin(a) * 0.62f), 4, 0.5f, (j, cells) => (BlockKind.Candy, p.c), info.blocks, DU);
            }
        }

        /// <summary>12 얼음 성문: 얼음 큐브 기둥 두 개(2×2×6) + 그 위 보라 상자 2단, 바깥 통나무 기둥, 가운데 낮은 큐브 벽.</summary>
        static void BuildIceGate(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 2.4f, p, true, Balance.PedestalLegs(info.level), 0f, 1.6f);
            foreach (float sx in new[] { -1f, 1f })
            {
                float cx = sx * 1.2f;
                Grid(root, rng, cx, 0f, 2, 2, 6, 0.5f, (i, d, j, cells) => (BlockKind.Ice, IceCol), info.blocks);
                GridAt(root, rng, new Vector3(cx, PedestalTop + 6 * DU, 0f), 2, 2, 2, 0f, (i, d, j, cells) => (BlockKind.Cube, j == 0 ? PurpleCol : BlueCol), info.blocks);
                // 바깥 통나무 기둥 (3칸 통나무 두 개) 앞뒤 2개
                Grid(root, rng, sx * 2.0f, 0f, 1, 2, 6, 1f, (i, d, j, cells) => (BlockKind.Log, WoodCol), info.blocks);
                MakeUnit(root, BlockKind.Cube, new Vector3(sx * 2.0f, PedestalTop + 6 * DU, 0f), 1, PurpleCol, info.blocks, DU);
            }
            Grid(root, rng, 0f, 0f, 2, 2, 4, 0.3f, (i, d, j, cells) => (BlockKind.Cube, (i + d + j) % 2 == 0 ? PurpleCol : BlueCol), info.blocks);
            if (info.level >= 40) Row(root, new Vector3(0f, PedestalTop + 4 * DU, 0f), 2, 2, (i, d) => (BlockKind.Ice, IceCol), info.blocks);
        }

        /// <summary>13 통나무 벽: 보라 상자·상자 탑 두 개 사이를 세운 통나무 열로 잇고, 통나무 위에 얼음·보라 상자 줄.</summary>
        static void BuildLogBridge(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 2.2f, p, true, Balance.PedestalLegs(info.level), 0f, 1.5f);
            int towerRows = Balance.Grow(info.level, 6, 40, 8);
            foreach (float cx in new[] { -1.4f, 1.4f })
                Grid(root, rng, cx, 0f, 3, 2, towerRows, 0.3f, (i, d, j, cells) =>
                    (j + i + d) % 2 == 0 ? (BlockKind.Cube, PurpleCol) : (BlockKind.Crate, CrateCol), info.blocks);
            int logRows = towerRows - 2;
            Grid(root, rng, 0f, 0f, 3, 2, logRows, 1f, (i, d, j, cells) => (BlockKind.Log, WoodCol), info.blocks);
            Row(root, new Vector3(0f, PedestalTop + logRows * DU, 0f), 3, 2, (i, d) => (BlockKind.Ice, IceCol), info.blocks);
            Row(root, new Vector3(0f, PedestalTop + (logRows + 1) * DU, 0f), 3, 2, (i, d) => (BlockKind.Cube, PurpleCol), info.blocks);
            MakeUnit(root, BlockKind.Candy, new Vector3(0f, PedestalTop + (logRows + 2) * DU, 0f), 2, p.c, info.blocks, DU);
        }

        /// <summary>14 얼음 격자 탑: 얼음 큐브와 세운 통나무가 바둑판처럼 섞인 3×3 탑, 꼭대기 보라 상자와 사탕.</summary>
        static void BuildSlabJenga(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 1.0f, p, true, 1, 0f, 1.9f);
            int rows = Balance.Grow(info.level, 8, 30, 10);
            Grid(root, rng, 0f, 0f, 3, 3, rows, 0.6f, (i, d, j, cells) =>
                (i + d + j / 2) % 2 == 0 ? (BlockKind.Ice, IceCol) : (BlockKind.Log, WoodCol), info.blocks);
            Row(root, new Vector3(0f, PedestalTop + rows * DU, 0f), 2, 2, (i, d) => (BlockKind.Cube, PurpleCol), info.blocks);
            MakeUnit(root, BlockKind.Candy, new Vector3(0f, PedestalTop + (rows + 1) * DU, 0f), 2, p.c, info.blocks, DU);
        }

        /// <summary>15 쌍둥이 원통 탑 (206·253): 독립 원형 받침대 2개 위에 분홍 원통 줄과 상자 줄을 번갈아. 25레벨부터 번갈아 오르내린다.</summary>
        static void BuildTwinCylinderTowers(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            independentPedestals = true;
            int rows = Balance.Grow(info.level, 6, 40, 8);
            foreach (float cx in new[] { -1.2f, 1.2f })
            {
                Pedestal(root, new Vector3(cx, 0, 0), 0.9f, p);
                Grid(root, rng, cx, 0f, 3, 2, rows, 0f, (i, d, j, cells) => j % 2 == 0 ? (BlockKind.Cylinder, PinkCol) : (BlockKind.Crate, CrateCol), info.blocks);
                MakeUnit(root, BlockKind.Candy, new Vector3(cx, PedestalTop + rows * DU, 0f), 1, p.c, info.blocks, DU);
            }
        }

        /// <summary>16 가운데 높은 피라미드 (210·300): 가운데 받침대가 0.8 높고 그 위 원통 피라미드(두 겹), 양옆 낮은 받침대엔 큐브 2×2.</summary>
        static void BuildCenterHighPyramid(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            independentPedestals = true;
            const float raise = 0.8f;
            const int baseN = 6;
            float rc = baseN * DS * 0.5f + 0.15f;
            Pedestal(root, Vector3.zero, rc, p, true, 1, raise);
            float y0 = PedestalTop + raise;
            for (int r = 0; r < baseN; r++)
            {
                int n = baseN - r;
                Row(root, new Vector3(0f, y0 + r * DU, 0f), n, 2, (i, d) =>
                {
                    var kind = (r + i + d) % 3 == 2 ? BlockKind.Crate : BlockKind.Cylinder;
                    return (kind, kind == BlockKind.Crate ? CrateCol : BlueCol);
                }, info.blocks);
            }
            float sideX = rc + 0.5f;
            foreach (float sx in new[] { -1f, 1f })
            {
                Pedestal(root, new Vector3(sx * sideX, 0, 0), 0.45f, p, false, 1, 0f, 1.0f);
                Grid(root, rng, sx * sideX, 0f, 2, 2, 3, 0.4f, (i, d, j, cells) => (BlockKind.Cube, (j + i + d) % 2 == 0 ? BlueCol : p.a), info.blocks);
            }
        }

        /// <summary>17 신전 (204·215·230): 파랑 큐브 바닥 → 빨강 긴 원통·상자 → 상자 띠 → 대리석 기둥과 사이 큐브 → 빨강 원통 지붕. 전부 두 겹.</summary>
        static void BuildTemple(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 2.0f, p, true, Balance.PedestalLegs(info.level), 0f, 1.5f);
            float y = PedestalTop;
            Row(root, new Vector3(0, y, 0), 8, 2, (i, d) => (BlockKind.Cube, BlueCol), info.blocks);
            y += DU;
            // 1층: 양끝 상자 기둥(2칸) + 안쪽 빨강 원통(2칸) 4개, 두 겹
            Row(root, new Vector3(0, y, 0), 8, 2, (i, d) => (i == 0 || i == 7) ? (BlockKind.Crate, CrateCol) : (i % 2 == 1 ? (BlockKind.Cylinder, RedCol) : (BlockKind.Cube, GoldCol)), info.blocks, 2);
            y += DU * 2f;
            // 상자 띠 2칸 × 두 겹
            for (int j = 0; j < 2; j++) Row(root, new Vector3(0, y + j * DU, 0), 8, 2, (i, d) => (BlockKind.Cube, (i + j + d) % 2 == 0 ? BlueCol : p.b), info.blocks);
            y += DU * 2f;
            // 대리석 기둥(2칸) 4개 + 사이 큐브 2칸, 두 겹
            Row(root, new Vector3(0, y, 0), 8, 2, (i, d) => (i % 2 == 0) ? (BlockKind.Stone, MarbleCol) : (BlockKind.Cube, (i / 2 + d) % 2 == 0 ? RedCol : GoldCol), info.blocks, 2);
            y += DU * 2f;
            // 지붕: 빨강 원통 줄 + 파랑 큐브 3개 (+50레벨부터 사탕)
            Row(root, new Vector3(0, y, 0), 8, 2, (i, d) => (BlockKind.Cylinder, RedCol), info.blocks);
            Row(root, new Vector3(0, y + DU, 0), 3, 2, (i, d) => (BlockKind.Cube, BlueCol), info.blocks);
            if (info.level >= 50) MakeUnit(root, BlockKind.Candy, new Vector3(0, y + DU * 2f, 0), 1, p.c, info.blocks, DU);
        }

        /// <summary>18 둥근 원통 탑 (201·260): 짧은 원통 10개 고리를 6~8층, 가운데 상자 기둥. 원통이 굴러 떨어져야 해서 어려운 축.</summary>
        static void BuildRoundCylinderTower(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Pedestal(root, Vector3.zero, 1.15f, p);
            int layers = Balance.Grow(info.level, 6, 40, 8);
            const int n = 10; const float r = 0.75f;
            for (int l = 0; l < layers; l++)
                for (int k = 0; k < n; k++)
                {
                    float a = k / (float)n * Mathf.PI * 2f;
                    MakeUnit(root, BlockKind.Cylinder, new Vector3(Mathf.Cos(a) * r, PedestalTop + l * DU, Mathf.Sin(a) * r), 1, l % 2 == 0 ? BlueCol : PinkCol, info.blocks, DU);
                }
            FillColumn(root, rng, new Vector3(0, PedestalTop, 0), layers, 0.5f, (j, cells) => (BlockKind.Crate, CrateCol), info.blocks, DU);
            MakeUnit(root, BlockKind.Candy, new Vector3(0, PedestalTop + layers * DU, 0), 1, p.c, info.blocks, DU);
        }

        /// <summary>19 세 탑 (276·251): 받침대 3개 위에 금·보라 큐브 탑, 가운데가 낮다. 꼭대기 초록 큐브·사탕.</summary>
        static void BuildFrame8(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            independentPedestals = true;
            float[] xs = { -1.5f, 0f, 1.5f };
            int[] hs = { 8, 5, 8 };
            for (int k = 0; k < 3; k++)
            {
                Pedestal(root, new Vector3(xs[k], 0, 0), 0.6f, p, false, 1, 0f, 1.3f);
                int kk = k;
                Grid(root, rng, xs[k], 0f, 2, 2, hs[k], 0.4f, (i, d, j, cells) => (BlockKind.Cube, (j / 2 + kk) % 2 == 0 ? GoldCol : PurpleCol), info.blocks);
                Row(root, new Vector3(xs[k], PedestalTop + hs[k] * DU, 0f), 2, 2, (i, d) => (BlockKind.Cube, GreenCol), info.blocks);
                MakeUnit(root, BlockKind.Candy, new Vector3(xs[k], PedestalTop + (hs[k] + 1) * DU, 0f), 1, p.c, info.blocks, DU);
            }
        }

        /// <summary>20 마름모 무늬 벽 (237): 금·보라 큐브 벽(7×8, 두 겹)에 얼음 큐브로 마름모 무늬. 꼭대기 사탕.</summary>
        static void BuildDiamondTower(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            const int w = 7, h = 8;
            Pedestal(root, Vector3.zero, w * DS * 0.5f + 0.15f, p, true, Balance.PedestalLegs(info.level), 0f, 2 * DS + 0.5f);
            Grid(root, rng, 0f, 0f, w, 2, h, 0f, (i, d, j, cells) =>
            {
                int dx = Mathf.Abs(i - 3), dy = Mathf.Abs(j - 4);
                if (dx + dy == 3) return (BlockKind.Ice, IceCol);           // 마름모 테두리
                return (BlockKind.Cube, (j % 2 == 0) ? GoldCol : PurpleCol);
            }, info.blocks);
            MakeUnit(root, BlockKind.Candy, new Vector3(0, PedestalTop + h * DU, 0), 2, p.c, info.blocks, DU);
        }

        /// <summary>21 상자 벽과 곁탑 (203): 큰 받침대에 상자·보라 큐브 5열 벽(두 겹), 위에 큐브 줄. 오른쪽 작은 받침대에 사탕 기둥. 곁탑은 독립.</summary>
        static void BuildCrateWallWithSide(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            independentPedestals = true;
            Pedestal(root, new Vector3(-0.5f, 0, 0), 1.4f, p, true);
            int rows = Balance.Grow(info.level, 6, 40, 8);
            Grid(root, rng, -0.5f, 0f, 5, 2, rows, 0.4f, (i, d, j, cells) => (i + j) % 3 == 0 ? (BlockKind.Cube, PurpleCol) : (BlockKind.Crate, CrateCol), info.blocks);
            Row(root, new Vector3(-0.5f, PedestalTop + rows * DU, 0), 3, 2, (i, d) => (BlockKind.Cube, p.b), info.blocks);
            Pedestal(root, new Vector3(1.65f, 0, 0), 0.7f, p);
            Grid(root, rng, 1.65f, 0f, 3, 2, 4, 0.5f, (i, d, j, cells) => (BlockKind.Candy, p.c), info.blocks);
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
