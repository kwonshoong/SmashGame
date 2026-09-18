using System.Linq;
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
        public const float PedestalTop = 0.98f;   // 상판 윗면 높이
        /// <summary>땅 높이. 레퍼런스 실측: 상판에서 받침대 발까지 화면 높이의 약 15% ≈ 1.3 → 상판 아래 1.28</summary>
        public const float GroundY = -0.45f;
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
            keepPlateShape = false;
            fixedFront = false;
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
        static void Pedestal(Transform parent, Vector3 center, float radius, Palette p, bool square = false, int legs = 1, float raise = 0f, float depth = 0f, float yaw = 0f)
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
            top.transform.position = new Vector3(center.x, plateY - 0.04f, center.z);
            // 앞뒤 깊이는 좌우 폭보다 얕게 (원형은 타원, 사각형은 가로로 긴 판). 구조물 깊이(원진 1.43, 통나무 1.0, 원통 다발 1.2)는 다 들어간다.
            top.transform.localScale = square ? new Vector3(radius * 2f, 0.08f, dz) : new Vector3(radius * 2f, 0.04f, dz);   // 두께 0.08 (레퍼런스처럼 얇게)
            top.GetComponent<Renderer>().material = Materials.Get(p.pedestal, true);
            if (!square) FlattenCollider(top, false);   // 사각 상판은 기본 BoxCollider가 이미 평평하다 (움직이는 받침대에선 메시보다 접촉이 안정적)
            RoundedMesh.Apply(top, 0.03f);
            PedestalColliders.Add(top.GetComponent<Collider>());

            // 상판 아래 금색 테두리 + 진한 보라 밑판(두께감)
            float ringY = plateY - 0.08f - 0.015f;
            if (square)
            {
                Deco(PrimitiveType.Cube, root, "PedestalRim", new Vector3(center.x, ringY, center.z), new Vector3(radius * 2f + 0.06f, 0.03f, dz + 0.06f), gold, 0.01f);
                Deco(PrimitiveType.Cube, root, "PedestalUnder", new Vector3(center.x, ringY - 0.045f, center.z), new Vector3(radius * 2f - 0.1f, 0.06f, dz - 0.1f), purpleDark, 0.02f);
            }
            else
            {
                Deco(PrimitiveType.Cylinder, root, "PedestalRim", new Vector3(center.x, ringY, center.z), new Vector3(radius * 2f + 0.06f, 0.015f, dz + 0.06f), gold, 0.01f);
                Deco(PrimitiveType.Cylinder, root, "PedestalUnder", new Vector3(center.x, ringY - 0.045f, center.z), new Vector3(radius * 2f - 0.1f, 0.03f, dz - 0.1f), purpleDark, 0.02f);
            }

            pedestalCenters.Add(center);
            foreach (var ox in offs) PedestalColumn(root, new Vector3(center.x + ox, 0f, center.z), plateY - 0.08f, p);
            // 상판을 y축으로 돌린다 (자식은 상판 중심을 축으로 함께 돈다). 돌린 상판은 FitPlatesToBlocks의 축 정렬 계산이 맞지 않으니 keepPlateShape와 함께 쓴다
            if (Mathf.Abs(yaw) > 0.01f) group.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
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
            float colBottom = GroundY - 0.6f;   // 승강 받침대가 올라가도 기둥이 땅에서 뜨지 않게 아래로 더 묻어 둔다
            var col = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            col.name = "PedestalColumn";
            col.transform.SetParent(root);
            col.transform.position = new Vector3(center.x, (colTop + colBottom) * 0.5f, center.z);
            col.transform.localScale = new Vector3(0.30f, (colTop - colBottom) * 0.5f, 0.30f) * sizeMul;   // 가는 기둥 (굵으면 짧아 보인다)
            col.GetComponent<Renderer>().material = Materials.Get(p.pedestal, true);
            RoundedMesh.Apply(col, 0.04f);
            PedestalColliders.Add(col.GetComponent<Collider>());
            Deco(PrimitiveType.Cylinder, root, "ColumnCap", new Vector3(center.x, colTop - 0.16f, center.z), new Vector3(0.42f, 0.05f, 0.42f) * sizeMul, gold, 0.02f);
            // 아래 링·발은 받침대가 오르내려도 땅에 남아 있도록 묶음 바깥(부모)에 둔다
            var ground = root.parent != null ? root.parent : root;
            Deco(PrimitiveType.Cylinder, ground, "ColumnBase", new Vector3(center.x, GroundY + 0.22f, center.z), new Vector3(0.42f, 0.05f, 0.42f) * sizeMul, gold, 0.02f);
            // 기둥 세로 홈 느낌의 얇은 금색 줄 4개
            for (int k = 0; k < 4; k++)
            {
                float a = k * 90f * Mathf.Deg2Rad;
                Deco(PrimitiveType.Cube, root, "ColumnStripe", new Vector3(center.x + Mathf.Cos(a) * 0.14f, (colTop + GroundY) * 0.5f, center.z + Mathf.Sin(a) * 0.14f),
                    new Vector3(0.04f, (colTop - GroundY) - 0.5f, 0.04f) * sizeMul, gold, 0.01f);
            }
            // 받침 발: 넓은 둥근 판 두 장 (지름 1.2/1.6 → 0.84/1.12, 30% 축소)
            Deco(PrimitiveType.Cylinder, ground, "PedestalFoot", new Vector3(center.x, GroundY + 0.1f, center.z), new Vector3(0.7f, 0.1f, 0.7f) * sizeMul, Materials.Get(p.pedestal, true), 0.05f);
            Deco(PrimitiveType.Cylinder, ground, "PedestalFoot2", new Vector3(center.x, GroundY + 0.03f, center.z), new Vector3(0.95f, 0.06f, 0.95f) * sizeMul, purpleDark, 0.04f);
        }

        /// <summary>이번 빌드에서 만든 받침대 중심들 (화면 맞춤 축소 후 기둥을 다시 세울 때 사용)</summary>
        static readonly List<Vector3> pedestalCenters = new();
        static readonly List<GameObject> pedestalGroups = new();
        static readonly List<List<float>> pedestalLegOffsets = new();
        /// <summary>이번 구조물의 받침대들이 서로 독립된 탑인가(승강 위상을 어긋나게 해도 되는가). 빌더가 설정</summary>
        static bool independentPedestals;
        /// <summary>true면 상판을 블록 발자국에 맞춰 줄이지 않는다 (둥근 상판 위 호 배치처럼 발자국 사각형이 상판 모양과 다를 때)</summary>
        static bool keepPlateShape;

        public const float PlateMargin = 0.22f;   // 상판이 블록 발자국보다 밖으로 나오는 여유

        /// <summary>
        /// 상판을 그 위에 놓인 블록의 발자국에 맞춰 줄인다 (줄이기만 한다). 상판이 블록보다 넓으면 쓰러진 블록이 상판 위에 쌓여
        /// 떨어지지 않아 몇 발로 끝나거나 반대로 끝이 안 나고, 레퍼런스처럼 "블록이 깔린 만큼"의 테이블이 보기에도 맞다.
        /// 앞뒤(z)는 항상, 좌우(x)는 다리가 하나인 받침대만 줄인다 (다리 여럿은 폭에 맞춰 세워져 있다).
        /// 구조물 루트가 원점(z=0)에 있을 때, 화면 맞춤 전에 호출한다.
        /// </summary>
        static void FitPlatesToBlocks(List<Block> blocks)
        {
            if (keepPlateShape) return;
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
                    PedestalColumn(g.transform, new Vector3(wc.x + ox * s, 0f, wc.z), wc.y - 0.08f * s, p, 1f / s);
            }
            Physics.SyncTransforms();
            return s;
        }
        public const float FitMargin = 0.9f;    // 화면 반폭의 90%까지 채운다 (레퍼런스: 구조물이 폭의 85~90%)
        public const float FitTopMargin = 0.85f; // 구조물 꼭대기는 화면 세로 반높이의 85%까지 (레퍼런스: 피라미드 꼭대기가 화면 높이 14% 지점)
        public const float FitMaxUp = 1.2f;     // 확대는 미세 조정만: 블록 크기가 구조물마다 달라지지 않게 (폭 채우기는 구조물 설계가 맡는다)


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
            b.fallY = PedestalTop - 0.5f;   // 땅(GroundY)에 세로로 선 3칸 블록(중심 0.375)도 떨어진 것으로 센다
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
            // T레벨마다 모든 종류가 정확히 한 번씩 나오도록 주기별로 섞은 순열에서 고른다 (곱수+흔들기 방식은 특정 종류가 200레벨 넘게 안 나왔다)
            int cycle = level / T;
            var perm = new int[T]; for (int i = 0; i < T; i++) perm[i] = i;
            var prng = new System.Random(cycle * 1237 + 7);
            for (int i = T - 1; i > 0; i--) { int k = prng.Next(i + 1); (perm[i], perm[k]) = (perm[k], perm[i]); }
            int pick = info.hard ? (level / 10 + 6) % T : perm[level % T];
            int type = allowed[pick];
            if (level <= 7) type = new[] { 1, 0, 2, 3, 4, 5, 1 }[level - 1];   // 튜토리얼 구간(1~7)은 초반용 6종을 순서대로

            // 사거리: 구조물(받침대 포함)을 자식 루트에 짓고 통째로 뒤로 민다. 카메라·대포는 그대로라 멀수록 작게 보이고 포물선이 높아진다.
            info.rangeTier = Balance.RangeTier(level);
            info.rangeZ = Balance.RangeZ[info.rangeTier];
            var levelRoot = root;
            root = new GameObject("Structure").transform;
            root.SetParent(levelRoot);
            switch (type)
            {
                // ---- 새 규칙(상판 겹침 없음 · 블록 크기 고정 · 두 겹 채움) 카탈로그 ----
                case 0: BuildN_BrickFence(root, rng, p, info); break;
                case 1: BuildN_CylinderBundle(root, rng, p, info); break;
                case 2: BuildN_CrateShelf(root, rng, p, info); break;
                case 3: BuildN_LogTower(root, rng, p, info); break;
                case 4: BuildN_IceWall(root, rng, p, info); break;
                case 5: BuildN_TriplePedestal(root, rng, p, info); break;
                case 6: BuildN_Pyramid(root, rng, p, info); break;
                case 7: BuildN_Gate(root, rng, p, info); break;
                case 8: BuildN_TwinTowers(root, rng, p, info); break;
                case 9: BuildN_Staircase(root, rng, p, info); break;
                case 10: BuildN_Fortress(root, rng, p, info); break;
                case 11: BuildN_StoneRing(root, rng, p, info); break;
                case 12: BuildN_WindowWall(root, rng, p, info); break;
                case 13: BuildN_ArchGate(root, rng, p, info); break;
                case 14: BuildN_Temple(root, rng, p, info); break;
                case 15: BuildN_ThreeTowers(root, rng, p, info); break;
                case 16: BuildN_BrickTower(root, rng, p, info); break;
                case 17: BuildN_HWall(root, rng, p, info); break;
                case 18: BuildN_OffsetWall(root, rng, p, info); break;
                case 19: BuildN_RoundCastle(root, rng, p, info); break;
                case 20: BuildN_Bridge(root, rng, p, info); break;
                case 21: BuildN_StepCastle(root, rng, p, info); break;
                case 22: BuildN_Lattice(root, rng, p, info); break;
                case 23: BuildN_Mushroom(root, rng, p, info); break;
                case 24: BuildN_EaveWall(root, rng, p, info); break;
                case 25: BuildN_PatternWall(root, rng, p, info); break;
                case 26: BuildN_WindowTower(root, rng, p, info); break;
                case 27: BuildN_FourPillars(root, rng, p, info); break;
                case 28: BuildN_WallAndTower(root, rng, p, info); break;
                case 29: BuildN_LogWall(root, rng, p, info); break;
                case 30: BuildTerraceGate(root, rng, p, info); break;
                case 31: BuildFoldingWall(root, rng, p, info); break;
                case 32: BuildFanWall(root, rng, p, info); break;
                case 33: BuildBowTower(root, rng, p, info); break;
                case 34: BuildTwinWings(root, rng, p, info); break;
                case 35: BuildCrossFort(root, rng, p, info); break;
                case 36: BuildPinwheel(root, rng, p, info); break;
                case 37: BuildTriangleFort(root, rng, p, info); break;
                case 38: BuildFiveLeaves(root, rng, p, info); break;
                case 39: BuildStaggeredWalls(root, rng, p, info); break;
                case 40: BuildZigzag4(root, rng, p, info); break;
                case 41: BuildArrowFort(root, rng, p, info); break;
                case 42: BuildDiamondCross(root, rng, p, info); break;
                case 43: BuildDiagonalWall(root, rng, p, info); break;
                case 44: BuildN_CylinderHoneycomb(root, rng, p, info); break;
                case 45: BuildN_IceCastle(root, rng, p, info); break;
                case 46: BuildN_CandyForest(root, rng, p, info); break;
                case 47: BuildN_LogCabin(root, rng, p, info); break;
                case 48: BuildN_StoneArch(root, rng, p, info); break;
                case 49: BuildN_StepPyramid(root, rng, p, info); break;
                case 50: BuildN_TwinCylinderTowers(root, rng, p, info); break;
                case 51: BuildN_CrateRampart(root, rng, p, info); break;
                case 52: BuildN_XWall(root, rng, p, info); break;
                case 53: BuildN_LogRing(root, rng, p, info); break;
                case 54: BuildN_BellTower(root, rng, p, info); break;
                case 55: BuildN_ThreeRows(root, rng, p, info); break;
                case 56: BuildN_ConvexWall(root, rng, p, info); break;
                case 57: BuildN_WedgeWall(root, rng, p, info); break;
                case 58: BuildN_TWall(root, rng, p, info); break;
                case 59: BuildN_CylinderWall(root, rng, p, info); break;
                case 60: BuildN_IcePyramid(root, rng, p, info); break;
                case 61: BuildN_CrateTrio(root, rng, p, info); break;
                case 62: BuildN_PlankLattice(root, rng, p, info); break;
                case 63: BuildN_WallAndWatchtower(root, rng, p, info); break;
                case 64: BuildN_RainbowFence(root, rng, p, info); break;
                case 65: BuildN_LogBridge(root, rng, p, info); break;
                case 66: BuildN_DoubleRing(root, rng, p, info); break;
                case 67: BuildN_RoofHouse(root, rng, p, info); break;
                case 68: BuildN_Octagon(root, rng, p, info); break;
                case 69: BuildN_StairTower(root, rng, p, info); break;
                case 70: BuildN_ThreeWindows(root, rng, p, info); break;
                case 71: BuildN_CylinderArch(root, rng, p, info); break;
                case 72: BuildN_DoublePyramid(root, rng, p, info); break;
                case 73: BuildN_FiveColumnHall(root, rng, p, info); break;
                case 74: BuildN_TwinIceTowers(root, rng, p, info); break;
                default: BuildN_Citadel(root, rng, p, info); break;
            }
            Physics.SyncTransforms();
            SeparatePlates(root, info, level, type);
            FitPlatesToBlocks(info.blocks);
            float zShift = info.rangeZ;
            if (fixedFront)
            {
                // 맨 앞 블록의 앞면을 FrontZ에 맞춘다 (블록 크기·거리 통일)
                float minZ = float.MaxValue;
                foreach (var bl in info.blocks) { var col = bl.GetComponent<Collider>(); if (col != null) minZ = Mathf.Min(minZ, col.bounds.min.z); }
                if (minZ < float.MaxValue) zShift += FrontZ - minZ;
            }
            root.position = new Vector3(0f, 0f, zShift);
            Physics.SyncTransforms();
            info.fitScale = fixedFront ? 1f : FitToScreen(root, levelRoot, cam, info.blocks, zShift, p);
            // 규칙 ④ 검사: 세로 10칸 초과 블록이 있으면 경고 (구조물 설계 오류)
            {
                float limit = PedestalTop + MaxStackCells * DU + 0.02f, top = 0f;
                foreach (var bl in info.blocks) { var col = bl.GetComponent<Collider>(); if (col != null) top = Mathf.Max(top, col.bounds.max.y); }
                if (top > limit) Debug.LogWarning($"[LevelBuilder] L{level} {type}: 블록 꼭대기 {top:F2} > 세로 한계 {limit:F2} (10칸)");
            }
            ApplyPedestalMotion(level, type, info);

            // 강화 블록 — 레벨 61부터, 돌·상자·판자에만, 20% 이하
            if (level >= Balance.ReinforcedFromLevel)
            {
                // 강화 블록은 바닥 줄(받침대에 직접 닿는 블록)에만 둔다. 위에 얹히면 무게+마찰로 아래 블록을 눌러
                // 구조물 전체가 붙은 듯 굳어 버린다(플레이 로그로 확인). 바닥에 있으면 자기 자리만 지키는 "닻" 역할.
                var cand = info.blocks.FindAll(b =>
                    (b.kind == BlockKind.Stone || b.kind == BlockKind.Crate || b.kind == BlockKind.Cube)
                    && b.GetComponent<Renderer>().bounds.min.y < PedestalTop + 0.12f);
                int max = Mathf.FloorToInt(info.blocks.Count * Balance.ReinforcedRatioCap(level));
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
                int pairs = Balance.StickyPairs(level);
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
            float totalMass = 0f; foreach (var b in info.blocks) { var rb = b != null ? b.GetComponent<Rigidbody>() : null; if (rb != null) totalMass += rb.mass; }
            info.startBalls = Balance.StartBalls(level, info.hard, totalMass) + Balance.RangeExtraBalls(info.rangeTier) + Balance.StructureExtraBalls(type) + (info.motion != Balance.MotionKind.None ? Balance.MotionExtraBalls : 0);
            info.structureName = type switch
            {
                0 => "벽돌 담", 1 => "원통 다발", 2 => "상자 선반", 3 => "통나무 탑", 4 => "얼음 벽", 5 => "삼중 받침대",
                6 => "피라미드", 7 => "성문", 8 => "쌍둥이 탑", 9 => "계단", 10 => "요새", 11 => "돌기둥 원진", 12 => "창문 벽", 13 => "아치 문",
                14 => "신전", 15 => "세 탑", 16 => "벽돌 탑", 17 => "H자 벽", 18 => "엇갈린 겹 벽", 19 => "둥근 성", 20 => "다리", 21 => "계단 성",
                22 => "격자 탑", 23 => "버섯 탑", 24 => "처마 벽", 25 => "무늬 벽", 26 => "창문 탑", 27 => "네 기둥", 28 => "상자 벽과 곁탑", 29 => "통나무 벽",
                30 => "계단식 성문", 31 => "병풍 벽", 32 => "부채꼴 성벽", 33 => "뱃머리 탑", 34 => "쌍날개", 35 => "십자 성", 36 => "풍차", 37 => "삼각 요새",
                38 => "다섯 잎", 39 => "엇갈린 두 벽", 40 => "꺾인 벽", 41 => "화살촉 성", 42 => "다이아몬드 십자", 43 => "대각선 벽",
                44 => "원통 벌집", 45 => "얼음 성", 46 => "사탕 숲", 47 => "통나무 오두막", 48 => "돌 아치", 49 => "계단 피라미드", 50 => "쌍둥이 원통 탑", 51 => "상자 성벽",
                52 => "X자 벽", 53 => "통나무 원진", 54 => "종탑", 55 => "세 줄 벽", 56 => "볼록 성벽", 57 => "쐐기 벽", 58 => "T자 벽", 59 => "원통 벽",
                60 => "얼음 피라미드", 61 => "상자 탑 셋", 62 => "판자 격자", 63 => "성벽과 망루", 64 => "무지개 담", 65 => "통나무 다리", 66 => "이중 링", 67 => "지붕 집",
                68 => "팔각 성", 69 => "계단 탑", 70 => "창 셋 벽", 71 => "원통 아치", 72 => "겹 피라미드", 73 => "다섯 기둥 홀", 74 => "쌍둥이 얼음 탑", _ => "성채"
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

        // ---------------- 실루엣 × 무늬 × 소재 생성기 (레퍼런스처럼 "매 판 다른 모양") ----------------
        // 마스크 문자: '.' 빈칸 / 소문자 = 규격 블록 열(a 벽, b 기둥, c 지붕·성가퀴, d 바닥 줄) / 대문자 = 눕힌 부재(같은 대문자가 가로로 이어진 칸이 한 개의 1×1×n 블록, n≤3).
        // 눕힌 부재는 창문·문 위 상인방과 처마(1칸 돌출)에 쓴다. 규칙: 모든 블록은 단면 1칸, 길이 1~3칸 — 세우든 눕히든 같은 블록.
        // 'E'/'O' = 벽돌 줄(눕힌 2칸 부재를 나란히). E는 줄 시작에서, O는 양끝에 기둥 한 칸을 두고 한 칸 안쪽에서 시작해 줄마다 이음매가 어긋난다(레퍼런스의 엇갈려 쌓기).
        // 행은 위→아래 순서. 상판은 발자국보다 살짝 좁게(양끝 블록이 0.18 걸침) 두어 끝을 치면 통째로 기운다.

        /// <summary>구조물에 쓸 색 2벌·소재 세트를 레벨 시드로 고른다</summary>
        struct MaterialSet
        {
            public BlockKind wall; public Color wall1, wall2;     // 벽 소재와 무늬 색 2가지
            public BlockKind pillar; public Color pillarCol;      // 기둥
            public BlockKind roof; public Color roofCol;          // 지붕·성가퀴
            public BlockKind bar; public Color barCol;            // 눕힌 부재(상인방·처마)
            public BlockKind brick; public Color brickCol;        // 벽돌 줄(눕힌 2칸 부재)
            public int pattern;                                   // 무늬: 0 체크 1 가로 띠 2 세로 줄 3 액자 4 십자 5 마름모 6 단색
        }
        static readonly Color[] PatternCols = { PurpleCol, BlueCol, PinkCol, GoldCol, RedCol, GreenCol };

        static MaterialSet PickMaterials(System.Random rng, Palette p, LevelInfo info)
        {
            var m = new MaterialSet();
            int c1 = rng.Next(PatternCols.Length), c2 = (c1 + 1 + rng.Next(PatternCols.Length - 1)) % PatternCols.Length;
            m.wall1 = PatternCols[c1]; m.wall2 = PatternCols[c2];
            int w = rng.Next(10);
            m.wall = w < 6 ? BlockKind.Cube : w < 8 ? BlockKind.Ice : BlockKind.Crate;
            if (m.wall == BlockKind.Ice) { m.wall1 = IceCol; m.wall2 = PatternCols[c2]; }
            if (m.wall == BlockKind.Crate) { m.wall1 = CrateCol; m.wall2 = PatternCols[c1]; }
            int pk = rng.Next(4);
            m.pillar = pk == 0 ? BlockKind.Log : pk == 1 ? BlockKind.Stone : pk == 2 ? BlockKind.Cylinder : BlockKind.Cube;
            m.pillarCol = m.pillar == BlockKind.Log ? WoodCol : m.pillar == BlockKind.Stone ? MarbleCol : m.wall2;
            int rk = rng.Next(3);
            m.roof = rk == 0 ? BlockKind.Candy : rk == 1 ? BlockKind.Cylinder : BlockKind.Cube;
            m.roofCol = m.roof == BlockKind.Candy ? p.c : m.wall2;
            // 눕힌 부재 위에는 항상 블록이 얹히므로 둥근 통나무는 쓰지 않는다 (얹힌 블록이 기운다 — 실측 4~9°)
            int bk = rng.Next(3);
            m.bar = bk == 0 ? BlockKind.Plank : BlockKind.Cube;
            m.barCol = m.bar == BlockKind.Plank ? WoodCol : (m.wall == BlockKind.Cube ? GoldCol : m.wall2);
            m.pattern = rng.Next(7);
            int kk = rng.Next(3);
            m.brick = kk == 0 ? BlockKind.Plank : BlockKind.Cube;
            m.brickCol = m.brick == BlockKind.Plank ? WoodCol : (m.wall2 == m.wall1 ? BlueCol : m.wall2);
            return m;
        }

        /// <summary>벽 칸(x, y)의 무늬 색. W·H는 마스크 크기.</summary>
        static Color WallColor(MaterialSet m, int x, int y, int W, int H)
        {
            bool alt;
            switch (m.pattern)
            {
                case 0: alt = (x + y) % 2 == 1; break;
                case 1: alt = (y / 2) % 2 == 1; break;
                case 2: alt = (x / 2) % 2 == 1; break;
                case 3: alt = x == 0 || y == 0 || x == W - 1 || y == H - 1 || (x >= 2 && x <= W - 3 && y >= 2 && y <= H - 3 && (x + y) % 2 == 0); break;
                case 4: alt = x == W / 2 || x == (W - 1) / 2 || y == H / 2 || y == (H - 1) / 2; break;
                case 5: alt = Mathf.Abs(x - (W - 1) * 0.5f) + Mathf.Abs(y - (H - 1) * 0.5f) <= Mathf.Min(W, H) * 0.5f - 0.5f; break;
                default: alt = false; break;
            }
            return alt ? m.wall2 : m.wall1;
        }

        static (BlockKind, Color) MaskMaterial(char z, int x, int y, int W, int H, MaterialSet m, LevelInfo info)
        {
            switch (z)
            {
                case 'a': return (m.wall, WallColor(m, x, y, W, H));
                case 'b': return (m.pillar, m.pillarCol);
                case 'c': return (m.roof, m.roofCol);
                case 'd': return Base(info);
                default: return (BlockKind.Cube, m.wall1);
            }
        }

        /// <summary>마스크대로 구조물을 짓는다. 상판은 발자국(가장 넓은 행)보다 0.18 좁게.</summary>
        static void BuildMask(Transform root, System.Random rng, Palette p, LevelInfo info, string[] mask, int depth, float tallChance = 0.4f)
        {
            int H = mask.Length, W = 0;
            foreach (var r in mask) W = Mathf.Max(W, r.Length);
            char Cell(int x, int y) { string r = mask[H - 1 - y]; return x < r.Length ? r[x] : '.'; }   // y: 0 = 바닥
            int minX = W, maxX = -1;
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (Cell(x, y) != '.') { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); }
            float ox = -(W - 1) * 0.5f * DS;   // x 칸 → 월드
            float extent = Mathf.Max(Mathf.Abs(ox + minX * DS), Mathf.Abs(ox + maxX * DS)) + DU * 0.5f;
            var m = PickMaterials(rng, p, info);
            Pedestal(root, Vector3.zero, Mathf.Max(0.6f, extent - 0.18f), p, true, Balance.PedestalLegs(info.level), 0f, depth * DS + 0.5f);

            for (int d = 0; d < depth; d++)
            {
                float z = (d - (depth - 1) * 0.5f) * DS;
                // 세로 열: 같은 소문자가 이어진 구간마다 1~3칸 블록으로 채운다
                for (int x = 0; x < W; x++)
                {
                    int y = 0;
                    while (y < H)
                    {
                        char c = Cell(x, y);
                        if (!char.IsLower(c)) { y++; continue; }
                        int y0 = y; while (y < H && Cell(x, y) == c) y++;
                        int len = y - y0, xx = x, yy = y0; char zc = c;
                        FillColumn(root, rng, new Vector3(ox + x * DS, PedestalTop + y0 * DU, z), len, c == 'a' ? tallChance : 0.6f,
                            (j, cells) => MaskMaterial(zc, xx, yy + j, W, H, m, info), info.blocks, DU);
                    }
                }
                // 눕힌 부재: 같은 대문자가 가로로 이어진 구간을 길이 3 이하로 잘라 만든다
                for (int y = 0; y < H; y++)
                {
                    int x = 0;
                    while (x < W)
                    {
                        char c = Cell(x, y);
                        if (!char.IsUpper(c)) { x++; continue; }
                        int x0 = x; while (x < W && Cell(x, y) == c) x++;
                        int run = x - x0, at = x0;
                        if (c == 'E' || c == 'O')
                        {
                            // 벽돌 줄: O는 양끝 기둥 한 칸 + 안쪽, E는 전체를 2칸 부재로 (남는 한 칸은 마지막을 3칸으로)
                            if (c == 'O')
                            {
                                var (pk, pc) = MaskMaterial('b', x0, y, W, H, m, info);
                                MakeUnit(root, pk, new Vector3(ox + x0 * DS, PedestalTop + y * DU, z), 1, pc, info.blocks, DU);
                                MakeUnit(root, pk, new Vector3(ox + (x - 1) * DS, PedestalTop + y * DU, z), 1, pc, info.blocks, DU);
                                at = x0 + 1; run = run - 2;
                            }
                            while (run > 0)
                            {
                                int n = run == 3 || run == 1 ? run : 2;
                                MakeBar(root, new Vector3(ox + (at + (n - 1) * 0.5f) * DS, PedestalTop + y * DU, z), n, m.brick, m.brickCol, info.blocks);
                                at += n; run -= n;
                            }
                            continue;
                        }
                        while (run > 0)
                        {
                            int n = run >= 3 ? 3 : run; if (run == 4) n = 2;
                            MakeBar(root, new Vector3(ox + (at + (n - 1) * 0.5f) * DS, PedestalTop + y * DU, z), n, m.bar, m.barCol, info.blocks);
                            at += n; run -= n;
                        }
                    }
                }
            }
        }

        /// <summary>x 방향으로 눕힌 1×1×n 블록. basePos는 바닥 중심. 통나무면 진짜 원통(구름 저항).</summary>
        static Block MakeBar(Transform root, Vector3 basePos, int n, BlockKind kind, Color color, List<Block> list)
        {
            float len = n * DS - 0.01f;
            float mass = MassFor(kind) * n * UnitMass(DU);
            if (kind == BlockKind.Log)
            {
                var log = MakeBlock(root, PrimitiveType.Cylinder, kind, basePos + Vector3.up * DU * 0.5f, new Vector3(DU - 0.01f, len * 0.5f, DU - 0.01f), Quaternion.Euler(0, 0, 90), color, mass, list);
                log.SetRollingLog();
                return log;
            }
            return MakeBlock(root, PrimitiveType.Cube, kind, basePos + Vector3.up * DU * 0.5f, new Vector3(len, DU - 0.01f, DU - 0.01f), Quaternion.identity, color, mass, list);
        }

        // ---- 템플릿 (행: 위→아래) ----
        static readonly string[] MaskWindowWall = {
            "aaaaaaaaa",
            "aaaaaaaaa",
            "aAAAaBBBa",
            "aa.aaa.aa",
            "aa.aaa.aa",
            "aaaaaaaaa",
            "ddddddddd" };
        static readonly string[] MaskArchGate = {
            "c.c.c.c",
            "aaaaaaa",
            "aaAAAaa",
            "bbb.bbb",
            "bbb.bbb",
            "bbb.bbb",
            "ddddddd" };
        static readonly string[] MaskMushroom = {
            "cccccc",
            "aaaaaa",
            "aaaaaa",
            "AAABBB",
            ".bbbb.",
            ".bbbb.",
            ".bbbb.",
            ".dddd." };
        static readonly string[] MaskEaveWall = {
            "AAAcccBBB",
            ".aaaaaaa.",
            ".aaaaaaa.",
            ".aaaaaaa.",
            ".aaaaaaa.",
            ".aaaaaaa.",
            ".ddddddd." };
        static readonly string[] MaskStepCastle = {
            "...ccc...",
            "..aaaaa..",
            "..aaaaa..",
            ".aaaaaaa.",
            ".aaaaaaa.",
            "aaaaaaaaa",
            "aaaaaaaaa",
            "ddddddddd" };
        static readonly string[] MaskTwinCastle = {
            "c.c...c.c",
            "bbb...bbb",
            "bbb...bbb",
            "bbbAAAbbb",
            "aaaaaaaaa",
            "aaaaaaaaa",
            "aaaaaaaaa",
            "ddddddddd" };
        static readonly string[] MaskColumnHall = {
            "aaaaaaaa",
            "aaaaaaaa",
            "aAAABBBa",
            "bb.bb.bb",
            "bb.bb.bb",
            "bb.bb.bb",
            "dddddddd" };
        static readonly string[] MaskWindowTower = {
            "cccc",
            "aaaa",
            "AAAa",
            "a.aa",
            "aaaa",
            "AAAa",
            "a.aa",
            "aaaa",
            "dddd" };
        static readonly string[] MaskDoubleArch = {
            "c.c.c.c.c",
            "aaaaaaaaa",
            "aAAAaBBBa",
            "bb.bbb.bb",
            "bb.bbb.bb",
            "bb.bbb.bb",
            "ddddddddd" };
        static readonly string[] MaskPatternWall = {
            "aaaaaaaa",
            "aaaaaaaa",
            "aaaaaaaa",
            "aaaaaaaa",
            "aaaaaaaa",
            "aaaaaaaa",
            "aaaaaaaa",
            "dddddddd" };
        static readonly string[] MaskHWall = {
            "bbbb.bbbb",
            "bbbb.bbbb",
            "aaaAAAaaa",
            "aaaa.aaaa",
            "aaaa.aaaa",
            "dddd.dddd" };

        static readonly string[] MaskBrickWall = {
            "ccccccc",
            "OOOOOOO",
            "aaaaaaa",
            "EEEEEEE",
            "aaaaaaa",
            "OOOOOOO",
            "aaaaaaa",
            "EEEEEEE",
            "ddddddd" };
        static readonly string[] MaskBrickTower = {
            "ccccc",
            "bbbbb",
            "EEEEE",
            "OOOOO",
            "EEEEE",
            "OOOOO",
            "EEEEE",
            "OOOOO",
            "bbbbb",
            "ddddd" };
        static readonly string[] MaskBrickPyramid = {
            "...EEE...",
            "..OOOOO..",
            ".EEEEEEE.",
            "OOOOOOOOO",
            "bbbbbbbbb",
            "EEEEEEEEE",
            "bbbbbbbbb",
            "ddddddddd" };
        static readonly string[] MaskTwinBrickTowers = {
            "cccc.cccc",
            "bbbb.bbbb",
            "EEEE.EEEE",
            "OOOO.OOOO",
            "bbbb.bbbb",
            "EEEE.EEEE",
            "OOOO.OOOO",
            "bbbb.bbbb",
            "dddd.dddd" };

        static void BuildWindowWall(Transform root, System.Random rng, Palette p, LevelInfo info) => BuildMask(root, rng, p, info, MaskWindowWall, Mathf.Min(2, Depth(info)));
        static void BuildBrickWall(Transform root, System.Random rng, Palette p, LevelInfo info) => BuildMask(root, rng, p, info, MaskBrickWall, Mathf.Min(2, Depth(info)));
        static void BuildBrickTower(Transform root, System.Random rng, Palette p, LevelInfo info) => BuildMask(root, rng, p, info, MaskBrickTower, Depth(info));
        static void BuildBrickPyramid(Transform root, System.Random rng, Palette p, LevelInfo info) => BuildMask(root, rng, p, info, MaskBrickPyramid, Mathf.Min(2, Depth(info)));
        static void BuildTwinBrickTowers(Transform root, System.Random rng, Palette p, LevelInfo info) => BuildMask(root, rng, p, info, MaskTwinBrickTowers, Depth(info));
        static void BuildArchGate(Transform root, System.Random rng, Palette p, LevelInfo info) => BuildMask(root, rng, p, info, MaskArchGate, Depth(info));
        static void BuildMushroom(Transform root, System.Random rng, Palette p, LevelInfo info) => BuildMask(root, rng, p, info, MaskMushroom, Depth(info));
        static void BuildEaveWall(Transform root, System.Random rng, Palette p, LevelInfo info) => BuildMask(root, rng, p, info, MaskEaveWall, Mathf.Min(2, Depth(info)));
        static void BuildStepCastle(Transform root, System.Random rng, Palette p, LevelInfo info) => BuildMask(root, rng, p, info, MaskStepCastle, Mathf.Min(2, Depth(info)));
        static void BuildTwinCastle(Transform root, System.Random rng, Palette p, LevelInfo info) => BuildMask(root, rng, p, info, MaskTwinCastle, Mathf.Min(2, Depth(info)));
        static void BuildColumnHall(Transform root, System.Random rng, Palette p, LevelInfo info) => BuildMask(root, rng, p, info, MaskColumnHall, Depth(info));
        static void BuildWindowTower(Transform root, System.Random rng, Palette p, LevelInfo info) => BuildMask(root, rng, p, info, MaskWindowTower, Depth(info));
        static void BuildDoubleArch(Transform root, System.Random rng, Palette p, LevelInfo info) => BuildMask(root, rng, p, info, MaskDoubleArch, Depth(info));
        static void BuildPatternWall(Transform root, System.Random rng, Palette p, LevelInfo info) => BuildMask(root, rng, p, info, MaskPatternWall, Mathf.Min(2, Depth(info)));
        static void BuildHWall(Transform root, System.Random rng, Palette p, LevelInfo info) => BuildMask(root, rng, p, info, MaskHWall, Mathf.Min(2, Depth(info)));


        // ---------------- 3차원 엇갈림 (레퍼런스: 앞줄과 뒷줄이 반 칸씩 어긋나고, 부재가 ±45°로 놓인 맵) ----------------

        /// <summary>
        /// 지그재그 벽: 줄마다 큐브가 반 칸씩 어긋나고(홀수 줄은 한 개 적게 가운데 정렬 → 큐브마다 아래 두 개에 걸침),
        /// 뒷겹은 앞겹보다 반 칸 옆으로 밀려 있어 정면에서 앞뒤 블록이 엇갈려 보인다. 아래·위 줄은 원통, 꼭대기 사탕.
        /// </summary>
        static void BuildStaggerWall(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            var m = PickMaterials(rng, p, info);
            // 짝수 줄 = 눕힌 2칸 부재 4개(폭 8칸), 홀수 줄 = 큐브 7개를 반 칸 안쪽으로 → 이음매가 어긋나고 큐브마다 아래 부재 위에 온전히 얹힌다
            // (큐브만으로 반 칸씩 어긋나게 쌓으면 양끝 큐브가 반만 걸쳐 넘어진다). 줄 수는 짝수로 두어 꼭대기가 좁은 줄이 되게.
            int W = 8, rows = info.level >= 60 ? 10 : 8, depth = Mathf.Min(2, Depth(info));
            float sp = DS;
            float extent = (W - 1) * 0.5f * sp + 0.5f * sp + DU * 0.5f;   // 뒷겹 반 칸 밀림 포함
            Pedestal(root, Vector3.zero, extent - 0.18f, p, true, Balance.PedestalLegs(info.level), 0f, depth * sp + 0.5f);
            var pillarKind = m.pillar == BlockKind.Cube ? BlockKind.Cylinder : m.pillar;
            for (int d = 0; d < depth; d++)
            {
                float z = (d - (depth - 1) * 0.5f) * sp, xs = (d % 2) * 0.5f * sp;
                for (int y = 0; y < rows; y++)
                {
                    float yy = PedestalTop + y * DU;
                    if (y == 0)
                    {
                        for (int i = 0; i < W; i++) MakeUnit(root, pillarKind, new Vector3((i - (W - 1) * 0.5f) * sp + xs, yy, z), 1, m.pillarCol, info.blocks, DU);
                    }
                    else if (y % 2 == 0)
                    {
                        for (int i = 0; i < W / 2; i++)
                            MakeBar(root, new Vector3((i * 2 + 0.5f - (W - 1) * 0.5f) * sp + xs, yy, z), 2, m.wall == BlockKind.Ice ? BlockKind.Ice : BlockKind.Cube, WallColor(m, i * 2, y, W, rows), info.blocks);
                    }
                    else
                    {
                        int n = W - 1;
                        for (int i = 0; i < n; i++)
                        {
                            float x = (i - (n - 1) * 0.5f) * sp + xs;
                            var (kind, col) = y == rows - 1 ? (pillarKind, m.wall2) : (m.wall, WallColor(m, i, y, W, rows));
                            MakeUnit(root, kind, new Vector3(x, yy, z), 1, col, info.blocks, DU);
                            if (y == rows - 1 && d == 0) MakeUnit(root, BlockKind.Candy, new Vector3(x, yy + DU, z), 1, i % 2 == 0 ? p.c : PinkCol, info.blocks, DU);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 대각 젠가 탑: 원통 격자 위에 눕힌 3칸 부재 3개를 +45°로 한 층, −45°로 다음 층… 젠가처럼 엇갈려 쌓는다.
        /// 정면에서 부재가 비스듬히 보이고, 층마다 부재가 아래층 부재 2~3개에 걸쳐 안정적이다.
        /// </summary>
        static void BuildDiagonalJenga(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            var m = PickMaterials(rng, p, info);
            Pedestal(root, Vector3.zero, 1.25f, p, false, 1, 0f, 2.2f);
            // 받침: 원통 3×3, 2칸
            Grid(root, rng, 0f, 0f, 3, 3, 2, 0f, (i, d, j, cells) => (m.pillar == BlockKind.Cube ? BlockKind.Cylinder : m.pillar, (i + d) % 2 == 0 ? m.pillarCol : m.wall2), info.blocks);
            float y = PedestalTop + 2 * DU;
            int layers = Balance.Grow(info.level, 6, 40, 8);
            for (int l = 0; l < layers; l++)
            {
                float ang = l % 2 == 0 ? 45f : -45f;
                float rad = ang * Mathf.Deg2Rad;
                Vector3 perp = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));   // 부재 방향(cos, -sin)에 수직
                Color col = l % 2 == 0 ? m.brickCol : (m.brick == BlockKind.Plank ? WoodCol : m.wall1);
                for (int k = -1; k <= 1; k++)
                {
                    Vector3 c = perp * (k * 0.62f);
                    float len = 3 * DS - 0.01f;
                    var kind = m.brick;
                    MakeBlock(root, PrimitiveType.Cube, kind, new Vector3(c.x, y + DU * 0.5f, c.z), new Vector3(len, DU - 0.01f, DU - 0.01f), Quaternion.Euler(0f, ang, 0f), col, MassFor(kind) * 3f * UnitMass(DU), info.blocks);
                }
                y += DU;
            }
            MakeUnit(root, BlockKind.Candy, new Vector3(0f, y, 0f), 2, p.c, info.blocks, DU);
        }


        // ---------------- 화면을 채우는 입체 배치 5종 ----------------

        /// <summary>
        /// 다리: 떨어진 두 받침대 위 탑 꼭대기에서 3칸 부재를 한 칸씩 내밀고(2칸은 탑 위, 1칸 돌출), 그 끝을 2칸 부재가 이어 두 탑을 잇는다.
        /// 다리 위에 큐브·사탕. 한쪽 탑을 무너뜨리면 다리가 통째로 내려앉는다.
        /// </summary>
        static void BuildBridge(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            independentPedestals = false;   // 두 받침대가 다리로 이어져 있으니 같은 위상으로 움직여야 한다
            var m = PickMaterials(rng, p, info);
            int rows = Balance.Grow(info.level, 5, 40, 6), depth = Mathf.Min(2, Depth(info));
            float cxT = 1.15f;   // 탑 중심. 안쪽 열(±0.69)의 안쪽 면이 ±0.46 → 두 탑 사이 2칸
            foreach (float sx in new[] { -1f, 1f })
            {
                float cx = sx * cxT;
                Pedestal(root, new Vector3(cx, 0, 0), 0.78f, p, true, 1, 0f, depth * DS + 0.5f);
                Grid(root, rng, cx, 0f, 3, depth, rows, 0.4f, (i, d, j, cells) => j == 0 ? Base(info) : (m.wall, WallColor(m, i, j, 3, rows)), info.blocks);
                for (int d = 0; d < depth; d++)
                {
                    float z = (d - (depth - 1) * 0.5f) * DS;
                    // 탑 꼭대기: 바깥 열 큐브 + 안쪽으로 내민 3칸 부재(탑 위 2칸 + 돌출 1칸)
                    MakeUnit(root, m.pillar == BlockKind.Cube ? BlockKind.Cube : m.pillar, new Vector3(sx * (cxT + DS), PedestalTop + rows * DU, z), 1, m.pillarCol, info.blocks, DU);
                    MakeBar(root, new Vector3(sx * (cxT - DS), PedestalTop + rows * DU, z), 3, m.bar, m.barCol, info.blocks);
                    // 탑 지붕
                    MakeUnit(root, m.roof, new Vector3(cx, PedestalTop + (rows + 1) * DU, z), 1, m.roofCol, info.blocks, DU);
                }
            }
            // 다리: 돌출 끝(±0.23)을 잇는 2칸 부재 + 위에 큐브 2개와 사탕
            for (int d = 0; d < depth; d++)
            {
                float z = (d - (depth - 1) * 0.5f) * DS;
                MakeBar(root, new Vector3(0f, PedestalTop + (rows + 1) * DU, z), 2, m.bar, m.barCol, info.blocks);
                MakeUnit(root, BlockKind.Cube, new Vector3(-DS * 0.5f, PedestalTop + (rows + 2) * DU, z), 1, m.wall2, info.blocks, DU);
                MakeUnit(root, BlockKind.Cube, new Vector3(DS * 0.5f, PedestalTop + (rows + 2) * DU, z), 1, m.wall2, info.blocks, DU);
            }
            MakeUnit(root, BlockKind.Candy, new Vector3(0f, PedestalTop + (rows + 3) * DU, 0f), 1, p.c, info.blocks, DU);
        }

        /// <summary>부메랑 벽: 큐브 열을 ±30°로 꺾어 V자(평면)로 세운 벽 두 날개. 꼭짓점엔 원통 기둥. 정면에서 양 날개가 비스듬히 보인다.</summary>
        static void BuildBoomerangWall(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            var m = PickMaterials(rng, p, info);
            int wing = 5, rows = Balance.Grow(info.level, 6, 40, 7), depth = Mathf.Min(2, Depth(info));
            const float ang = 30f;
            Pedestal(root, Vector3.zero, 2.35f, p, true, Balance.PedestalLegs(info.level), 0f, 2.2f);   // 날개 끝(회전 큐브 모서리 2.3)까지 상판 위에
            // 꼭짓점 기둥 (z 앞쪽)
            float vz = -0.55f;
            FillColumn(root, rng, new Vector3(0f, PedestalTop, vz), rows, 0.6f, (j, cells) => (m.pillar == BlockKind.Cube ? BlockKind.Cylinder : m.pillar, m.pillarCol), info.blocks, DU);
            foreach (float sx in new[] { -1f, 1f })
            {
                float a = sx * ang * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(sx * Mathf.Cos(a), 0f, Mathf.Sin(Mathf.Abs(a)));   // 꼭짓점에서 바깥·뒤쪽으로
                Vector3 nrm = new Vector3(-dir.z, 0f, dir.x);   // 날개에 수직
                var rot = Quaternion.Euler(0f, -sx * ang, 0f);
                for (int i = 1; i <= wing; i++)
                    for (int d = 0; d < depth; d++)
                    {
                        Vector3 pos = new Vector3(0f, 0f, vz) + dir * (i * DS) + nrm * ((d - (depth - 1) * 0.5f) * DS);
                        int ii = i, dd = d;
                        int y = 0;
                        // 열마다 1~3칸 블록 (회전된 큐브)
                        while (y < rows)
                        {
                            int cells = 1;
                            if (rows - y >= 2 && rng.NextDouble() < 0.4) cells = (rows - y >= 3 && rng.Next(2) == 0) ? 3 : 2;
                            bool top = y + cells >= rows;
                            var (kind, col) = top && i == wing ? (m.roof, m.roofCol) : (m.wall, WallColor(m, ii, y, wing, rows));
                            bool cyl = IsCylinderKind(kind);
                            float h = DU * cells;
                            Vector3 scale = cyl ? new Vector3(DU - 0.01f, h * 0.5f, DU - 0.01f) : new Vector3(DU - 0.01f, h, DU - 0.01f);
                            MakeBlock(root, cyl ? PrimitiveType.Cylinder : PrimitiveType.Cube, kind, new Vector3(pos.x, PedestalTop + y * DU + h * 0.5f, pos.z), scale, rot, col, MassFor(kind) * cells * UnitMass(DU), info.blocks, cells > 1);
                            y += cells;
                        }
                    }
            }
        }

        /// <summary>둥근 성: 큐브 기둥 12개 원 + 안쪽 원통 6개 원 + 가운데 3×3 탑. 원형이라 뒤쪽은 앞쪽에 가려진다.</summary>
        static void BuildRoundCastle(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            var m = PickMaterials(rng, p, info);
            Pedestal(root, Vector3.zero, 1.85f, p, false, 1, 0f, 3.7f);
            int outerRows = Balance.Grow(info.level, 5, 40, 6), n = 12; float r = 1.6f;
            for (int k = 0; k < n; k++)
            {
                float a = (k + 0.5f) / n * Mathf.PI * 2f;
                var pos = new Vector3(Mathf.Cos(a) * r, PedestalTop, Mathf.Sin(a) * r);
                int kk = k;
                FillColumn(root, rng, pos, outerRows, 0.4f, (j, cells) => (m.wall, WallColor(m, kk, j, n, outerRows)), info.blocks, DU);
                if (k % 2 == 0) MakeUnit(root, m.roof, pos + Vector3.up * (outerRows * DU), 1, m.roofCol, info.blocks, DU);
            }
            for (int k = 0; k < 6; k++)
            {
                float a = k / 6f * Mathf.PI * 2f;
                FillColumn(root, rng, new Vector3(Mathf.Cos(a) * 0.95f, PedestalTop, Mathf.Sin(a) * 0.95f), outerRows + 2, 0.6f, (j, cells) => (m.pillar == BlockKind.Cube ? BlockKind.Cylinder : m.pillar, m.pillarCol), info.blocks, DU);
            }
            int keep = outerRows + 4;
            Grid(root, rng, 0f, 0f, 1, 1, keep, 0.5f, (i, d, j, cells) => (m.wall, m.wall2), info.blocks);
            MakeUnit(root, BlockKind.Candy, new Vector3(0f, PedestalTop + keep * DU, 0f), 2, p.c, info.blocks, DU);
        }

        /// <summary>나선 계단: 가운데 탑을 중심으로 앞·오른쪽·뒤·왼쪽 날개가 2·4·6·8칸으로 높아진다. 정면에서 좌우 높이가 다르고 뒤가 보인다.</summary>
        static void BuildSpiralStairs(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            var m = PickMaterials(rng, p, info);
            Pedestal(root, Vector3.zero, 1.9f, p, false, 1, 0f, 3.4f);
            int[] hs = { 2, 4, 6, 8 };
            Vector3[] dirs = { new Vector3(0, 0, -1), new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(-1, 0, 0) };
            int center = 9;
            Grid(root, rng, 0f, 0f, 1, 1, center, 0.5f, (i, d, j, cells) => (m.pillar == BlockKind.Cube ? BlockKind.Cylinder : m.pillar, m.pillarCol), info.blocks);
            MakeUnit(root, BlockKind.Candy, new Vector3(0f, PedestalTop + center * DU, 0f), 1, p.c, info.blocks, DU);
            for (int w = 0; w < 4; w++)
            {
                Vector3 dir = dirs[w], side = new Vector3(dir.z, 0f, -dir.x);
                int h = hs[w];
                for (int i = 1; i <= 3; i++)
                    for (int k = -1; k <= 1; k++)
                    {
                        if (i == 1 && k != 0) continue;   // 탑 바로 옆 칸은 이웃 날개와 겹치므로 가운데만
                        Vector3 pos = dir * (i * DS) + side * (k * DS);
                        int ww = w, ii = i, kk = k;
                        FillColumn(root, rng, new Vector3(pos.x, PedestalTop, pos.z), h, 0.4f, (j, cells) => (m.wall, (ww + j / 2 + ii + kk) % 2 == 0 ? m.wall1 : m.wall2), info.blocks, DU);
                    }
                MakeUnit(root, m.roof, dir * (2 * DS) + Vector3.up * (PedestalTop + h * DU), 1, m.roofCol, info.blocks, DU);
            }
        }

        /// <summary>입체 성: 앞쪽 낮은 성벽(9열 × 3칸) 뒤에 모서리 탑 두 개(2×2 × 8칸)와 가운데 본성(3×3 × 6칸). 앞이 뒤를 가리고 탑은 높다.</summary>
        static void BuildCastle3D(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            var m = PickMaterials(rng, p, info);
            Pedestal(root, Vector3.zero, 2.15f, p, true, Balance.PedestalLegs(info.level), 0f, 2.3f);
            int towerRows = Balance.Grow(info.level, 7, 40, 8);
            GridAt(root, rng, new Vector3(0f, PedestalTop, -0.7f), 9, 1, 3, 0.3f, (i, d, j, cells) => j == 0 ? Base(info) : (m.wall, WallColor(m, i, j, 9, 3)), info.blocks);
            for (int i = 0; i < 9; i += 2) MakeUnit(root, BlockKind.Cube, new Vector3((i - 4) * DS, PedestalTop + 3 * DU, -0.7f), 1, m.wall2, info.blocks, DU);   // 성가퀴
            foreach (float cx in new[] { -1.6f, 1.6f })
            {
                Grid(root, rng, cx, 0.45f, 2, 2, towerRows, 0.5f, (i, d, j, cells) => (m.pillar == BlockKind.Cube ? BlockKind.Cylinder : m.pillar, (j / 2) % 2 == 0 ? m.pillarCol : m.wall2), info.blocks);
                Row(root, new Vector3(cx, PedestalTop + towerRows * DU, 0.45f), 2, 2, (i, d) => (m.roof, m.roofCol), info.blocks);
            }
            Grid(root, rng, 0f, 0.45f, 3, 3, towerRows - 2, 0.4f, (i, d, j, cells) => (m.wall, WallColor(m, i, j, 3, towerRows)), info.blocks);
            MakeUnit(root, BlockKind.Candy, new Vector3(0f, PedestalTop + (towerRows - 2) * DU, 0.45f), 2, p.c, info.blocks, DU);
        }


        // ---------------- 레퍼런스 3종 그대로: 원통 피라미드 · 삼중 성문 · 기둥 격자 (11칸 격자, 받침대 3개) ----------------

        /// <summary>11칸 격자(x 0~10, 가운데 5)와 받침대 3개(x 1·5·9)를 쓰는 구조물의 공통 준비. 받침대 폭 0.78, 사이 틈 0.28.</summary>
        static float G11(int i) => (i - 5) * DS;
        static void ThreePlates(Transform root, Palette p, int depth)
        {
            independentPedestals = false;   // 위가 이어져 있으니 같은 위상으로 움직인다
            foreach (int i in new[] { 1, 5, 9 }) Pedestal(root, new Vector3(G11(i), 0, 0), 0.78f, p, true, 1, 0f, depth * DS + 0.5f);
        }
        static void U11(Transform root, int x, int y, int cells, BlockKind kind, Color col, float z, List<Block> list)
            => MakeUnit(root, kind, new Vector3(G11(x), PedestalTop + y * DU, z), cells, col, list, DU);
        static void B11(Transform root, int x0, int y, int n, BlockKind kind, Color col, float z, List<Block> list)
            => MakeBar(root, new Vector3(G11(x0) + (n - 1) * 0.5f * DS, PedestalTop + y * DU, z), n, kind, col, list);

        /// <summary>z 방향(앞뒤)으로 눕힌 1×1×n 블록. basePos는 바닥 중심.</summary>
        static Block MakeBarZ(Transform root, Vector3 basePos, int n, BlockKind kind, Color color, List<Block> list)
        {
            float len = n * DS - 0.01f;
            return MakeBlock(root, PrimitiveType.Cube, kind, basePos + Vector3.up * DU * 0.5f, new Vector3(DU - 0.01f, DU - 0.01f, len), Quaternion.identity, color, MassFor(kind) * n * UnitMass(DU), list);
        }

        /// <summary>
        /// 원통 피라미드 (레퍼런스): 작은 둥근 받침대 위에 호(弧)를 따라 벽돌식으로 쌓은 삼각형 벽. 줄마다 원통이 하나씩 줄고 반 칸씩
        /// 어긋나 위 원통이 아래 두 개 사이에 얹힌다. 호의 중심이 카메라 쪽에 있고 반지름이 작아 양 날개가 ±95°까지 말려 들어온다.
        /// 색은 줄 가장자리부터 하늘색·보라·진파랑(겹친 삼각형 테두리처럼 보임). 안쪽 칸 뒤에는 한 칸 큰 호를 따라 진파랑 뒷줄이 겹쳐 두께를 준다.
        /// </summary>
        static void BuildCylinderPyramid(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            int N = Balance.Grow(info.level, 8, 20, 12);     // 바닥 줄 원통 수 = 줄 수
            const float CH = 0.40f;                           // 원통 높이: 레퍼런스는 지름(0.45)보다 살짝 납작(높이/지름 ≈ 0.9)
            float r = 1.0f, cz = -0.44f;                      // 앞 호 반지름 · 호 중심(카메라 쪽)
            Pedestal(root, Vector3.zero, 1.45f, p, false, 1, 0f, 2.9f);
            keepPlateShape = true;
            Color dark = new Color(0.2f, 0.32f, 0.72f);
            System.Action<Vector3, Color> can = (pos, c) =>
                MakeBlock(root, PrimitiveType.Cylinder, BlockKind.Cylinder, pos + Vector3.up * CH * 0.5f, new Vector3(DU - 0.01f, CH * 0.5f, DU - 0.01f),
                    Quaternion.identity, c, MassFor(BlockKind.Cylinder) * UnitMass(DU) * CH / DU, info.blocks);
            for (int j = 0; j < N; j++)
            {
                int n = N - j;
                float y = PedestalTop + j * CH;
                // 앞 호: 줄 가장자리부터 하늘색 · 보라 · 진파랑
                float dTh = DS / r;
                for (int i = 0; i < n; i++)
                {
                    float a = i - (n - 1) * 0.5f;             // 홀수 줄 정수, 짝수 줄 반정수 → 벽돌식 어긋남
                    int d = Mathf.Min(i, n - 1 - i);
                    Color c = d == 0 ? IceCol : d == 1 ? PurpleCol : dark;
                    float th = a * dTh;
                    can(new Vector3(r * Mathf.Sin(th), y, cz + r * Mathf.Cos(th)), c);
                }
                // 뒷 호(한 칸 큰 반지름): 안쪽 칸 뒤를 채우는 진파랑
                int n2 = n - 2;
                if (n2 <= 0) continue;
                float r2 = r + DS, dTh2 = DS / r2;
                for (int i = 0; i < n2; i++)
                {
                    float a = i - (n2 - 1) * 0.5f, th = a * dTh2;
                    can(new Vector3(r2 * Mathf.Sin(th), y, cz + r2 * Mathf.Cos(th)), dark);
                }
            }
        }

        /// <summary>
        /// 삼중 성문 (레퍼런스): 11칸 격자·받침대 셋. 탑 = 머릿돌(하늘색 큐브 · 한 칸 물러난 진파랑 큐브 · 하늘색 큐브) 위아래,
        /// 사이에 파랑 큐브 4단 두 열과 물러난 가운데 열(큐브·사탕 2칸·큐브). 가운데 받침대 = 빨강 부재 · 빨강 세로 2칸 두 개 ·
        /// 사탕 2칸 · 빨강 부재(머릿돌 높이). 지붕 = 3칸 빨강 부재 아래 1줄(가운데) · 2줄(어긋남) · 3줄. 바깥 머릿돌 위 금색 원통.
        /// </summary>
        static void BuildTripleGate(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            ThreePlates(root, p, 3);
            var L = info.blocks;
            float zF = -0.23f, zB = 0.23f;   // 앞(카메라 쪽) / 뒤
            Color sky = new Color(0.45f, 0.8f, 1f), dark = new Color(0.2f, 0.32f, 0.72f);
            foreach (int x0 in new[] { 0, 8 })
            {
                foreach (int y in new[] { 0, 5 })
                {
                    U11(root, x0, y, 1, BlockKind.Cube, sky, zF, L);
                    U11(root, x0 + 1, y, 1, BlockKind.Cube, dark, zB, L);
                    U11(root, x0 + 2, y, 1, BlockKind.Cube, sky, zF, L);
                }
                for (int y = 1; y <= 4; y++) { U11(root, x0, y, 1, BlockKind.Cube, BlueCol, zF, L); U11(root, x0 + 2, y, 1, BlockKind.Cube, BlueCol, zF, L); }
                U11(root, x0 + 1, 1, 1, BlockKind.Cube, BlueCol, zB, L);
                U11(root, x0 + 1, 2, 2, BlockKind.Candy, PinkCol, zB, L);
                U11(root, x0 + 1, 4, 1, BlockKind.Cube, BlueCol, zB, L);
                U11(root, x0 == 0 ? 0 : 10, 6, 1, BlockKind.Cylinder, GoldCol, zF, L);   // 금색 원통 (바깥 머릿돌 위)
            }
            // 가운데 받침대
            B11(root, 4, 0, 3, BlockKind.Cube, RedCol, 0f, L);
            MakeUnit(root, BlockKind.Cube, new Vector3(-0.5f * DS, PedestalTop + DU, 0f), 2, RedCol, L, DU);
            MakeUnit(root, BlockKind.Cube, new Vector3(0.5f * DS, PedestalTop + DU, 0f), 2, RedCol, L, DU);
            U11(root, 5, 3, 2, BlockKind.Candy, PinkCol, 0f, L);
            B11(root, 4, 5, 3, BlockKind.Cube, RedCol, 0f, L);   // 머릿돌 높이의 가운데 부재 (지붕 1줄)
            // 지붕 2줄: 머릿돌 안쪽 끝과 가운데 부재에 걸친 두 부재, 3줄: 금색 원통·2줄 부재 위 세 부재
            MakeBar(root, new Vector3(-1.5f * DS, PedestalTop + 6 * DU, 0f), 3, BlockKind.Cube, RedCol, L);
            MakeBar(root, new Vector3(1.5f * DS, PedestalTop + 6 * DU, 0f), 3, BlockKind.Cube, RedCol, L);
            B11(root, 0, 7, 3, BlockKind.Cube, RedCol, 0f, L);
            B11(root, 4, 7, 3, BlockKind.Cube, RedCol, 0f, L);
            B11(root, 8, 7, 3, BlockKind.Cube, RedCol, 0f, L);
        }

        /// <summary>
        /// 기둥 격자 (레퍼런스): 받침대마다 대리석 기둥이 앞 가운데, 진파랑 세로 블록 둘이 뒤 양 모서리(깊이 방향 삼각형), 그 위 3칸 부재.
        /// 이 층을 세 번 반복하되 바깥 받침대는 첫 층이 3칸이라 가운데보다 한 단 높게 엇갈려 부재가 서로 짜여 든 격자로 보인다.
        /// 꼭대기는 가운데 부재 위 큐브 둘과 받침대 사이 틈을 잇는 부재 둘.
        /// </summary>
        static void BuildColumnLattice(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            ThreePlates(root, p, 2);
            var L = info.blocks;
            float zF = -0.23f, zB = 0.23f;
            System.Action<int, int, int> layer = (x0, y, h) =>
            {
                U11(root, x0 + 1, y, h, BlockKind.Stone, MarbleCol, zF, L);   // 앞 가운데 대리석 기둥
                U11(root, x0, y, h, BlockKind.Cube, BlueCol, zB, L);          // 뒤 왼쪽
                U11(root, x0 + 2, y, h, BlockKind.Cube, BlueCol, zB, L);      // 뒤 오른쪽
                B11(root, x0, y + h, 3, BlockKind.Cube, BlueCol, 0f, L);
            };
            foreach (int x0 in new[] { 0, 8 }) { layer(x0, 0, 3); layer(x0, 4, 2); layer(x0, 7, 2); }   // 부재 y3·y6·y9
            layer(4, 0, 2); layer(4, 3, 2); layer(4, 6, 2);                                          // 부재 y2·y5·y8
            U11(root, 4, 9, 1, BlockKind.Cube, BlueCol, 0f, L);
            U11(root, 6, 9, 1, BlockKind.Cube, BlueCol, 0f, L);
            B11(root, 2, 10, 3, BlockKind.Cube, BlueCol, 0f, L);   // 틈을 잇는 부재 (바깥 부재 y9 위 · 가운데 큐브 위)
            B11(root, 6, 10, 3, BlockKind.Cube, BlueCol, 0f, L);
        }

        // ==================== 회전 받침대 구조물 (47~60) ====================
        // 규칙: ① 상판끼리 겹치지 않는다 ② 화면 맞춤 확대·축소 없이(fitScale 1) 맨 앞 블록의 앞면을 FrontZ에 맞춰 블록 크기가 항상 같다
        //       ③ 상판마다 앞뒤 두 겹으로 블록을 최대한 채운다. 앞쪽(FrontZ)에서 보이는 반폭은 약 1.8, 뒤로 갈수록 넓어진다.
        //       ④ 세로는 상판 위 10칸까지(1×3 셋 + 1×1 하나). 더 높으면 화면 위로 벗어난다. Build 끝에서 검사해 경고한다.

        /// <summary>맨 앞 블록 앞면의 z. 이 값에 맞춰 구조물 전체를 앞뒤로 옮긴다 (fixedFront 구조물)</summary>
        public const float FrontZ = -0.75f;
        /// <summary>규칙 ④ 세로 최대 10칸: 상판 위 블록 꼭대기가 상판 + 10칸(4.5)을 넘지 않는다 (1×3 블록 셋 위에 1×1 하나까지). 넘으면 화면 위로 벗어난다.</summary>
        public const int MaxStackCells = 10;
        /// <summary>true면 화면 맞춤 배율을 1로 고정하고 맨 앞 블록을 FrontZ에 맞춘다</summary>
        static bool fixedFront;

        /// <summary>규칙 ⑤ 상판 사이 최소 간격 1.2칸(0.55). 더 좁으면 떨어지는 블록이 상판 틈에 끼인다 (L224 풍차에서 확인).</summary>
        public const float MinPlateGap = 1.2f * DS;

        /// <summary>
        /// 규칙 ⑤: 상판끼리 MinPlateGap보다 가까우면 두 받침대(상판 묶음 + 그 위 블록)를 중심 연결선 방향으로 밀어 벌린다.
        /// 블록은 바닥 중심이 어느 상판 위에 있는지로 소속을 정한다. 상판 사이에 걸친 블록(다리)은 움직이지 않는다.
        /// 여러 쌍이 얽힌 배치(풍차·다섯 잎)는 몇 번 반복하면 수렴한다. 이후 FitPlatesToBlocks·앞면 정렬은 벌린 뒤 위치를 쓴다.
        /// </summary>
        static void SeparatePlates(Transform root, LevelInfo info, int level, int type)
        {
            var groups = pedestalGroups.Where(g => g != null && g.transform.IsChildOf(root)).ToList();
            if (groups.Count < 2) return;
            var tops = new List<Collider>();
            foreach (var g in groups) { var t = g.transform.Find("PedestalTop"); tops.Add(t != null ? t.GetComponent<Collider>() : null); }
            if (tops.Any(t => t == null)) return;

            // 블록 소속
            var owner = new List<int>();
            foreach (var b in info.blocks)
            {
                var col = b != null ? b.GetComponent<Collider>() : null; int best = -1; float bestD = 0.3f;
                if (col != null)
                {
                    Vector3 c = col.bounds.center;
                    for (int i = 0; i < tops.Count; i++)
                    {
                        Vector3 q = ClosestOnPlate(tops[i], new Vector3(c.x, tops[i].bounds.max.y, c.z));
                        float d = Vector2.Distance(new Vector2(q.x, q.z), new Vector2(c.x, c.z));
                        if (d < bestD) { bestD = d; best = i; }
                    }
                }
                owner.Add(best);
            }

            bool any = false;
            for (int iter = 0; iter < 16; iter++)
            {
                bool moved = false;
                for (int i = 0; i < tops.Count; i++)
                    for (int k = i + 1; k < tops.Count; k++)
                    {
                        float gap = PlateGap(tops[i], tops[k]);
                        if (gap >= MinPlateGap - 0.005f) continue;
                        Vector3 dir = tops[k].bounds.center - tops[i].bounds.center; dir.y = 0f;
                        if (dir.sqrMagnitude < 1e-4f) dir = Vector3.right;
                        dir.Normalize();
                        float push = (MinPlateGap - gap) * 0.5f + 0.005f;
                        ShiftPedestal(groups, i, -dir * push, info, owner);
                        ShiftPedestal(groups, k, dir * push, info, owner);
                        Physics.SyncTransforms();
                        moved = true; any = true;
                    }
                if (!moved) break;
            }
            if (any) Debug.Log($"[LevelBuilder] L{level} {type}: 상판 간격 {MinPlateGap:F2} 확보를 위해 받침대를 벌렸다");
        }

        static void ShiftPedestal(List<GameObject> groups, int i, Vector3 delta, LevelInfo info, List<int> owner)
        {
            groups[i].transform.position += delta;
            for (int b = 0; b < info.blocks.Count; b++) if (owner[b] == i && info.blocks[b] != null) info.blocks[b].transform.position += delta;
        }

        /// <summary>두 상판 윗면 둘레 사이의 최단 수평 거리 (둘레를 촘촘히 샘플해 상대 콜라이더까지 ClosestPoint). 겹치면 0.</summary>
        public static float PlateGap(Collider a, Collider b)
        {
            float g = float.MaxValue;
            foreach (var pr in new[] { (a, b), (b, a) })
            {
                var A = pr.Item1; var B = pr.Item2; float y = A.bounds.max.y; bool round = A is MeshCollider;
                for (int s = 0; s < 96; s++)
                {
                    float t = s / 96f; Vector3 lp;
                    if (round) { float ang = t * Mathf.PI * 2f; lp = new Vector3(Mathf.Cos(ang) * 0.5f, 0f, Mathf.Sin(ang) * 0.5f); }
                    else { float u = t * 4f; int side = (int)u; float f = u - side; lp = side == 0 ? new Vector3(-0.5f + f, 0f, -0.5f) : side == 1 ? new Vector3(0.5f, 0f, -0.5f + f) : side == 2 ? new Vector3(0.5f - f, 0f, 0.5f) : new Vector3(-0.5f, 0f, 0.5f - f); }
                    Vector3 wp = A.transform.TransformPoint(lp); wp.y = y;
                    Vector3 cp = ClosestOnPlate(B, wp); cp.y = wp.y;
                    g = Mathf.Min(g, Vector3.Distance(wp, cp));
                }
            }
            return g;
        }

        /// <summary>상판 콜라이더 위의 최근접점. 둥근 상판은 볼록하지 않은 메시 콜라이더라 ClosestPoint를 못 쓰므로 로컬 타원으로 계산한다.</summary>
        static Vector3 ClosestOnPlate(Collider c, Vector3 wp)
        {
            var mc = c as MeshCollider;
            if (mc == null || mc.convex) return c.ClosestPoint(wp);
            Vector3 lp = c.transform.InverseTransformPoint(wp);
            var xz = new Vector2(lp.x, lp.z); float r = xz.magnitude;
            if (r <= 0.5f) return wp;
            xz *= 0.5f / r;
            return c.transform.TransformPoint(new Vector3(xz.x, lp.y, xz.y));
        }

        /// <summary>yaw로 돌린 규격 블록(cells칸). basePos는 바닥 중심.</summary>
        static Block RUnit(Transform root, Vector3 basePos, int cells, BlockKind kind, Color c, float yaw, List<Block> list)
        {
            float h = DU * cells; bool cyl = IsCylinderKind(kind);
            var sc = cyl ? new Vector3(DU - 0.01f, h * 0.5f, DU - 0.01f) : new Vector3(DU - 0.01f, h, DU - 0.01f);
            return MakeBlock(root, cyl ? PrimitiveType.Cylinder : PrimitiveType.Cube, kind, basePos + Vector3.up * h * 0.5f, sc, Quaternion.Euler(0f, yaw, 0f), c, MassFor(kind) * cells * UnitMass(DU), list, cells > 1);
        }
        /// <summary>h칸 기둥을 3칸 단위로 쌓는다 (unitEach면 한 칸씩).</summary>
        static void RCol(Transform root, Vector3 basePos, int h, BlockKind kind, Color c, float yaw, List<Block> list, bool unitEach = false)
        {
            int y = 0;
            while (y < h) { int n = unitEach ? 1 : Mathf.Min(3, h - y); RUnit(root, basePos + Vector3.up * y * DU, n, kind, c, yaw, list); y += n; }
        }
        /// <summary>yaw로 돌린 n칸 눕힌 부재(로컬 x축 방향). basePos는 바닥 중심.</summary>
        static Block RBar(Transform root, Vector3 basePos, int n, BlockKind kind, Color c, float yaw, List<Block> list)
            => MakeBlock(root, PrimitiveType.Cube, kind, basePos + Vector3.up * DU * 0.5f, new Vector3(n * DS - 0.01f, DU - 0.01f, DU - 0.01f), Quaternion.Euler(0f, yaw, 0f), c, MassFor(kind) * n * UnitMass(DU), list);
        /// <summary>yaw로 돌린 상판의 로컬 x축 단위 벡터</summary>
        static Vector3 YawDir(float yaw) => Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
        /// <summary>상판 축(yaw)에 수직인 단위 벡터 (yaw 0이면 +z = 뒤)</summary>
        static Vector3 YawBack(float yaw) => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        /// <summary>yaw로 돌린 상판 하나 (다리 하나, 사각). halfLen: 축 방향 반길이, depth: 앞뒤 전체 폭</summary>
        static void RPlate(Transform root, Palette p, Vector3 center, float yaw, float halfLen, float depth, float raise = 0f)
            => Pedestal(root, center, halfLen, p, true, 1, raise, depth, yaw);
        static Vector3 Top(Vector3 center, float raise = 0f) => center + Vector3.up * (PedestalTop + raise);
        /// <summary>상판 축 위 k칸·수직 j칸 위치, row단 높이. b는 상판 윗면 중심</summary>
        static Vector3 At(Vector3 b, float yaw, float k, float j = 0f, int row = 0) => b + YawDir(yaw) * (k * DS) + YawBack(yaw) * (j * DS) + Vector3.up * (row * DU);
        static void RBarAt(Transform root, Vector3 b, float yaw, float k, int row, int n, BlockKind kind, Color col, List<Block> L, float j = 0f)
            => RBar(root, At(b, yaw, k, j, row), n, kind, col, yaw, L);
        static void RUnitAt(Transform root, Vector3 b, float yaw, float k, int row, int cells, BlockKind kind, Color col, List<Block> L, float j = 0f)
            => RUnit(root, At(b, yaw, k, j, row), cells, kind, col, yaw, L);
        static void RColAt(Transform root, Vector3 b, float yaw, float k, float j, int h, BlockKind kind, Color col, List<Block> L, bool each = false)
            => RCol(root, At(b, yaw, k, j), h, kind, col, yaw, L, each);
        /// <summary>상판 축을 따라 열 기둥들. cols[i]는 k = i-(n-1)/2, 수직 오프셋 j</summary>
        static void RCols(Transform root, Vector3 b, float yaw, (BlockKind kind, Color col, int h, bool each)[] cols, List<Block> L, float j = 0f, int row0 = 0)
        {
            int n = cols.Length;
            for (int i = 0; i < n; i++) { float k = i - (n - 1) * 0.5f; var cs = cols[i]; if (cs.h > 0) RCol(root, At(b, yaw, k, j, row0), cs.h, cs.kind, cs.col, yaw, L, cs.each); }
        }
        /// <summary>
        /// 벽돌식 벽: 짝수 줄은 큐브 nCols개, 홀수 줄은 2칸 부재(+남는 칸 큐브)를 줄마다 좌우 번갈아. depth겹(수직 방향 반 칸씩 앞뒤).
        /// </summary>
        static void RBrickWall(Transform root, Vector3 b, float yaw, int nCols, int rows, int depth, BlockKind cubeKind, Color cubeCol, Color barCol, List<Block> L)
        {
            for (int d = 0; d < depth; d++)
            {
                float j = d - (depth - 1) * 0.5f;
                float k0 = -(nCols - 1) * 0.5f;
                for (int row = 0; row < rows; row++)
                {
                    if (row % 2 == 0) { for (int i = 0; i < nCols; i++) RUnitAt(root, b, yaw, k0 + i, row, 1, cubeKind, cubeCol, L, j); continue; }
                    bool left = ((row / 2) + d) % 2 == 0;
                    int c = 0;
                    if (!left) { RUnitAt(root, b, yaw, k0, row, 1, cubeKind, cubeCol, L, j); c = 1; }
                    for (; c + 1 < nCols; c += 2) RBarAt(root, b, yaw, k0 + c + 0.5f, row, 2, BlockKind.Cube, barCol, L, j);
                    if (c < nCols) RUnitAt(root, b, yaw, k0 + c, row, 1, cubeKind, cubeCol, L, j);
                }
            }
        }
        static readonly Color SlateCol = new Color(0.27f, 0.36f, 0.62f), OrangeCol = new Color(1f, 0.55f, 0.12f), SkyCol = new Color(0.75f, 0.93f, 1f);

        /// <summary>
        /// 47 계단식 성문 (레퍼런스): 옆 상판 둘은 ±45°로 꺾여 앞이 벌어진 V, 가운데 상판은 정면으로 뒤. 옆 벽은 상판 방향으로
        /// 앞겹 [왕관 큐브+돌기둥 6 · 주황 통 6 · 사탕 6]+부재, 뒷겹 [주황 4 · 사탕 4 · 주황 4]+부재. 가운데는 사탕·왕관 기둥·사탕과 뒷줄 주황 통.
        /// </summary>
        static void BuildTerraceGate(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            var L = info.blocks; keepPlateShape = true; fixedFront = true;
            foreach (int side in new[] { -1, 1 })
            {
                float yaw = side * 45f; Vector3 u = new Vector3(-side * 0.7071f, 0f, 0.7071f);   // 바깥 앞 → 안쪽 뒤
                Vector3 P0 = new Vector3(side * 1.3f, 0f, -1.0f);
                Vector3 c = P0 + u * DS; RPlate(root, p, c, yaw, 1.5f * DS, 1.1f);           // k -0.5 ~ 2.5
                Vector3 b = Top(c);                                                            // 열 k = -1, 0, 1
                float jb = Vector3.Dot(YawBack(yaw), Vector3.forward) > 0 ? 0.5f : -0.5f;     // 카메라에서 먼 겹
                float jf = -jb;
                RUnitAt(root, b, yaw, -1, 0, 1, BlockKind.Cube, BlueCol, L, jf); RCol(root, At(b, yaw, -1, jf, 1), 5, BlockKind.Cube, SlateCol, yaw, L);
                RColAt(root, b, yaw, 0, jf, 6, BlockKind.Cylinder, OrangeCol, L, true);
                RColAt(root, b, yaw, 1, jf, 6, BlockKind.Candy, PinkCol, L);
                RBarAt(root, b, yaw, 0, 6, 3, BlockKind.Cube, BlueCol, L, jf);
                RColAt(root, b, yaw, -1, jb, 4, BlockKind.Cylinder, OrangeCol, L, true);
                RColAt(root, b, yaw, 0, jb, 4, BlockKind.Candy, PinkCol, L);
                RColAt(root, b, yaw, 1, jb, 4, BlockKind.Cylinder, OrangeCol, L, true);
                RBarAt(root, b, yaw, 0, 4, 3, BlockKind.Cube, BlueCol, L, jb);
                RUnitAt(root, b, yaw, 0, 5, 1, BlockKind.Cube, GoldCol, L, jb);
            }
            Vector3 cc = new Vector3(0f, 0f, 1.5f); RPlate(root, p, cc, 0f, 1.5f * DS + 0.15f, 1.1f); var bc = Top(cc);
            RColAt(root, bc, 0f, -1, -0.5f, 4, BlockKind.Candy, PinkCol, L); RColAt(root, bc, 0f, 1, -0.5f, 4, BlockKind.Candy, PinkCol, L);
            RUnitAt(root, bc, 0f, 0, 0, 1, BlockKind.Cube, BlueCol, L, -0.5f); RUnitAt(root, bc, 0f, 0, 1, 3, BlockKind.Cube, SlateCol, L, -0.5f);
            RBarAt(root, bc, 0f, 0, 4, 3, BlockKind.Cube, BlueCol, L, -0.5f); RUnitAt(root, bc, 0f, 0, 5, 1, BlockKind.Cube, BlueCol, L, -0.5f);
            RCols(root, bc, 0f, new[] { (BlockKind.Cylinder, OrangeCol, 3, true), (BlockKind.Cylinder, OrangeCol, 3, true), (BlockKind.Cylinder, OrangeCol, 3, true) }, L, 0.5f);
            RBarAt(root, bc, 0f, 0, 3, 3, BlockKind.Cube, RedCol, L, 0.5f);
        }

        /// <summary>48 병풍 벽 (상판 3, ±60°로 앞뒤 지그재그): 상판마다 얼음 벽돌 벽 3열 7단 + 부재·금색 큐브. 정면에서 벽면이 번갈아 좌우를 향한다.</summary>
        static void BuildFoldingWall(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            var L = info.blocks; keepPlateShape = true; fixedFront = true;
            for (int i = 0; i < 3; i++)
            {
                float yaw = (i % 2 == 0) ? 60f : -60f;
                Vector3 c = new Vector3((i - 1) * 1.3f, 0f, (i % 2 == 0) ? -0.3f : 0.35f);
                RPlate(root, p, c, yaw, 1.5f * DS + 0.1f, 0.75f); var b = Top(c);
                RBrickWall(root, b, yaw, 3, 7, 1, BlockKind.Ice, IceCol, BlueCol, L);
                RBarAt(root, b, yaw, 0, 7, 3, BlockKind.Cube, BlueCol, L);
                RUnitAt(root, b, yaw, 0, 8, 1, BlockKind.Cube, GoldCol, L);
            }
        }

        /// <summary>49 부채꼴 성벽 (상판 3, 오목한 호 위 −35°·0°·+35°): 옆은 상자·통나무 성벽 두 겹, 가운데 뒤는 대리석·사탕 천수각과 원통 뒷줄.</summary>
        static void BuildFanWall(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            var L = info.blocks; keepPlateShape = true; fixedFront = true;
            foreach (int side in new[] { -1, 1 })
            {
                float yaw = side * 35f; Vector3 c = new Vector3(side * 1.2f, 0f, 0f);
                RPlate(root, p, c, yaw, 1.5f * DS + 0.1f, 1.1f); var b = Top(c);
                RCols(root, b, yaw, new[] { (BlockKind.Crate, CrateCol, 3, true), (BlockKind.Log, WoodCol, 3, false), (BlockKind.Crate, CrateCol, 3, true) }, L, -0.5f);
                RBarAt(root, b, yaw, 0, 3, 3, BlockKind.Plank, WoodCol, L, -0.5f);
                RCol(root, At(b, yaw, -1, -0.5f, 4), 2, BlockKind.Crate, CrateCol, yaw, L, true); RCol(root, At(b, yaw, 1, -0.5f, 4), 2, BlockKind.Crate, CrateCol, yaw, L, true);
                RUnitAt(root, b, yaw, 0, 4, 2, BlockKind.Candy, PinkCol, L, -0.5f);
                RBarAt(root, b, yaw, 0, 6, 3, BlockKind.Plank, WoodCol, L, -0.5f);
                RCols(root, b, yaw, new[] { (BlockKind.Crate, CrateCol, 4, true), (BlockKind.Crate, CrateCol, 4, true), (BlockKind.Crate, CrateCol, 4, true) }, L, 0.5f);
                RBarAt(root, b, yaw, 0, 4, 3, BlockKind.Plank, WoodCol, L, 0.5f);
            }
            Vector3 cc = new Vector3(0f, 0f, 1.4f); RPlate(root, p, cc, 0f, 1.5f * DS + 0.1f, 1.1f); var bc = Top(cc);
            RCols(root, bc, 0f, new[] { (BlockKind.Stone, MarbleCol, 5, false), (BlockKind.Candy, PinkCol, 5, false), (BlockKind.Stone, MarbleCol, 5, false) }, L, -0.5f);
            RBarAt(root, bc, 0f, 0, 5, 3, BlockKind.Cube, RedCol, L, -0.5f);
            RUnitAt(root, bc, 0f, -1, 6, 1, BlockKind.Cube, RedCol, L, -0.5f); RUnitAt(root, bc, 0f, 1, 6, 1, BlockKind.Cube, RedCol, L, -0.5f);
            RUnitAt(root, bc, 0f, 0, 6, 2, BlockKind.Cube, GoldCol, L, -0.5f);
            RCols(root, bc, 0f, new[] { (BlockKind.Cylinder, BlueCol, 4, true), (BlockKind.Cylinder, BlueCol, 4, true), (BlockKind.Cylinder, BlueCol, 4, true) }, L, 0.5f);
            RBarAt(root, bc, 0f, 0, 4, 3, BlockKind.Cube, RedCol, L, 0.5f);
        }

        /// <summary>50 뱃머리 탑 (상판 2가 앞 꼭짓점에서 뒤로 벌어지는 V): 앞쪽 끝이 가장 높은 사탕·보라·원통 기둥 두 겹 벽.</summary>
        static void BuildBowTower(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            var L = info.blocks; keepPlateShape = true; fixedFront = true;
            Vector3 A = new Vector3(0f, 0f, -0.9f);
            foreach (int side in new[] { -1, 1 })
            {
                float yaw = -side * 45f; Vector3 u = new Vector3(side * 0.7071f, 0f, 0.7071f);
                Vector3 c = A + u * (3f * DS); RPlate(root, p, c, yaw, 1.6f * DS, 1.1f); var b = Top(c);   // k 1.4 ~ 4.6 (k는 A 기준)
                float jo = Vector3.Dot(YawBack(yaw), new Vector3(side, 0f, 0f)) > 0 ? 0.5f : -0.5f;      // 바깥쪽 겹
                float ji = -jo;
                RColAt(root, b, yaw, -1, ji, 6, BlockKind.Candy, PinkCol, L);
                RColAt(root, b, yaw, 0, ji, 5, BlockKind.Cube, PurpleCol, L, true);
                RColAt(root, b, yaw, 1, ji, 5, BlockKind.Cylinder, BlueCol, L);
                RBarAt(root, b, yaw, 0.5f, 5, 2, BlockKind.Cube, RedCol, L, ji);
                RUnitAt(root, b, yaw, -1, 6, 1, BlockKind.Cylinder, GoldCol, L, ji);
                RColAt(root, b, yaw, -1, jo, 4, BlockKind.Cube, BlueCol, L, true);
                RColAt(root, b, yaw, 0, jo, 4, BlockKind.Cube, BlueCol, L, true);
                RColAt(root, b, yaw, 1, jo, 4, BlockKind.Cube, BlueCol, L, true);
                RBarAt(root, b, yaw, 0, 4, 3, BlockKind.Cube, RedCol, L, jo);
                RUnitAt(root, b, yaw, 0, 5, 1, BlockKind.Cube, GoldCol, L, jo);
            }
        }

        /// <summary>51 쌍날개 (상판 2, ∓20°): 살짝 안쪽으로 굽은 두 날개, 통나무·상자·사탕 두 겹.</summary>
        static void BuildTwinWings(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            var L = info.blocks; keepPlateShape = true; fixedFront = true;
            foreach (int side in new[] { -1, 1 })
            {
                float yaw = side * 20f; Vector3 c = new Vector3(side * 1.15f, 0f, 0f);
                RPlate(root, p, c, yaw, 1.5f * DS + 0.1f, 1.1f); var b = Top(c);
                RCols(root, b, yaw, new[] { (BlockKind.Log, WoodCol, 3, false), (BlockKind.Crate, CrateCol, 3, true), (BlockKind.Log, WoodCol, 3, false) }, L, -0.5f);
                RBarAt(root, b, yaw, 0, 3, 3, BlockKind.Plank, WoodCol, L, -0.5f);
                RCol(root, At(b, yaw, -1, -0.5f, 4), 2, BlockKind.Crate, CrateCol, yaw, L, true); RCol(root, At(b, yaw, 1, -0.5f, 4), 2, BlockKind.Crate, CrateCol, yaw, L, true);
                RUnitAt(root, b, yaw, 0, 4, 2, BlockKind.Candy, PinkCol, L, -0.5f);
                RBarAt(root, b, yaw, 0, 6, 3, BlockKind.Plank, WoodCol, L, -0.5f);
                RUnitAt(root, b, yaw, 0, 7, 1, BlockKind.Cube, GoldCol, L, -0.5f);
                RCols(root, b, yaw, new[] { (BlockKind.Crate, CrateCol, 4, true), (BlockKind.Crate, CrateCol, 4, true), (BlockKind.Crate, CrateCol, 4, true) }, L, 0.5f);
                RBarAt(root, b, yaw, 0, 4, 3, BlockKind.Plank, WoodCol, L, 0.5f);
            }
        }

        /// <summary>52 십자 성 (상판 5: 앞 0°·양옆 90°·뒤 0° + 안뜰 가운데 작은 탑): 상자 요새.</summary>
        static void BuildCrossFort(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            var L = info.blocks; keepPlateShape = true; fixedFront = true;
            var front = new Vector3(0f, 0f, -1.0f); RPlate(root, p, front, 0f, 1.5f * DS + 0.1f, 0.75f); var bf = Top(front);
            RCols(root, bf, 0f, new[] { (BlockKind.Cube, PurpleCol, 2, true), (BlockKind.Cube, PurpleCol, 2, true), (BlockKind.Cube, PurpleCol, 2, true) }, L);
            RBarAt(root, bf, 0f, 0, 2, 3, BlockKind.Cube, BlueCol, L);
            for (int k = -1; k <= 1; k++) RUnitAt(root, bf, 0f, k, 3, 1, BlockKind.Cube, PurpleCol, L);
            foreach (int side in new[] { -1, 1 })
            {
                var c = new Vector3(side * 1.3f, 0f, 0.3f); RPlate(root, p, c, 90f, 1.5f * DS + 0.1f, 0.75f); var b = Top(c);
                RCols(root, b, 90f, new[] { (BlockKind.Crate, CrateCol, 3, true), (BlockKind.Crate, CrateCol, 3, true), (BlockKind.Crate, CrateCol, 3, true) }, L);
                RBarAt(root, b, 90f, 0, 3, 3, BlockKind.Plank, WoodCol, L);
                RCol(root, At(b, 90f, -1, 0f, 4), 2, BlockKind.Crate, CrateCol, 90f, L, true); RCol(root, At(b, 90f, 1, 0f, 4), 2, BlockKind.Crate, CrateCol, 90f, L, true);
                RUnitAt(root, b, 90f, 0, 4, 2, BlockKind.Candy, PinkCol, L);
                RBarAt(root, b, 90f, 0, 6, 3, BlockKind.Plank, WoodCol, L);
            }
            var back = new Vector3(0f, 0f, 1.6f); RPlate(root, p, back, 0f, 1.5f * DS + 0.1f, 0.75f); var bb = Top(back);
            RCols(root, bb, 0f, new[] { (BlockKind.Stone, MarbleCol, 5, false), (BlockKind.Candy, PinkCol, 5, false), (BlockKind.Stone, MarbleCol, 5, false) }, L);
            RBarAt(root, bb, 0f, 0, 5, 3, BlockKind.Cube, RedCol, L);
            RUnitAt(root, bb, 0f, -1, 6, 1, BlockKind.Cube, RedCol, L); RUnitAt(root, bb, 0f, 1, 6, 1, BlockKind.Cube, RedCol, L);
            RUnitAt(root, bb, 0f, 0, 6, 2, BlockKind.Cube, GoldCol, L);
            var mid = new Vector3(0f, 0f, 0.3f); RPlate(root, p, mid, 0f, 0.5f * DS + 0.2f, 0.85f); var bm = Top(mid);
            RCol(root, bm, 6, BlockKind.Cylinder, BlueCol, 0f, L, true); RUnit(root, bm + Vector3.up * 6 * DU, 1, BlockKind.Cylinder, GoldCol, 0f, L);
        }

        /// <summary>53 풍차 (상판 4가 접선 방향으로 사각 링 + 가운데 탑): 원통·사탕 기둥 벽 넷.</summary>
        static void BuildPinwheel(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            var L = info.blocks; keepPlateShape = true; fixedFront = true;
            for (int i = 0; i < 4; i++)
            {
                float a = 45f + 90f * i; float ar = a * Mathf.Deg2Rad;
                Vector3 c = new Vector3(Mathf.Cos(ar), 0f, Mathf.Sin(ar)) * 1.2f;
                float yaw = -a - 90f;
                RPlate(root, p, c, yaw, 1.5f * DS + 0.1f, 0.75f); var b = Top(c);
                RCols(root, b, yaw, new[] { (BlockKind.Cylinder, BlueCol, 4, true), (BlockKind.Candy, PinkCol, 4, false), (BlockKind.Cylinder, BlueCol, 4, true) }, L);
                RBarAt(root, b, yaw, 0, 4, 3, BlockKind.Cube, RedCol, L);
                RUnitAt(root, b, yaw, 0, 5, 1, BlockKind.Cube, GoldCol, L);
            }
            RPlate(root, p, Vector3.zero, 45f, 0.5f * DS + 0.2f, 0.85f); var bm = Top(Vector3.zero);
            RCol(root, bm, 6, BlockKind.Stone, MarbleCol, 45f, L); RUnit(root, bm + Vector3.up * 6 * DU, 1, BlockKind.Cube, GoldCol, 45f, L);
        }

        /// <summary>54 삼각 요새 (상판 6: 세 변 + 세 꼭짓점 기둥 받침): 얼음·큐브 벽돌 벽 세 변, 꼭짓점 대리석 기둥.</summary>
        static void BuildTriangleFort(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            var L = info.blocks; keepPlateShape = true; fixedFront = true;
            Vector3 A = new Vector3(-1.45f, 0f, -0.9f), B = new Vector3(1.45f, 0f, -0.9f), C = new Vector3(0f, 0f, 1.8f);
            var edges = new[] { (A, B, 0f, 0.75f, 3, 0.75f), (A, C, -61.8f, 0.9f, 4, 0.6f), (B, C, 61.8f, 0.9f, 4, 0.6f) };
            foreach (var (P, Q, yaw, half, cols, depth) in edges)
            {
                Vector3 c = (P + Q) * 0.5f;
                RPlate(root, p, c, yaw, half, depth); var b = Top(c);
                RBrickWall(root, b, yaw, cols, 5, 1, BlockKind.Ice, IceCol, BlueCol, L);
                RBarAt(root, b, yaw, 0, 5, 3, BlockKind.Cube, BlueCol, L);
                RUnitAt(root, b, yaw, 0, 6, 1, BlockKind.Cube, GoldCol, L);
            }
            foreach (var V in new[] { A, B, C })
            {
                RPlate(root, p, V, 0f, 0.28f, 0.56f); var bv = Top(V);
                RCol(root, bv, 6, BlockKind.Stone, MarbleCol, 0f, L); RUnit(root, bv + Vector3.up * 6 * DU, 1, BlockKind.Cube, RedCol, 0f, L);
            }
        }

        /// <summary>55 다섯 잎 (작은 상판 5가 오목한 호 위 25° 간격, 상판마다 기둥 하나 6단): 기둥 0-1·2-3 꼭대기를 잇는 3칸 부재와 금색 큐브. 상판 사이 간격 0.55 (규칙 ⑤).</summary>
        static void BuildFiveLeaves(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            var L = info.blocks; keepPlateShape = true; fixedFront = true;
            Vector3 O = new Vector3(0f, 0f, -1.0f); float r = 2.1f; const float step = 25f;   // 25° 간격, 반지름 2.1: 폭 2.1 안, 상판 간격은 SeparatePlates가 0.55로 맞춘다
            var kinds = new[] { (BlockKind.Cylinder, BlueCol), (BlockKind.Crate, CrateCol), (BlockKind.Stone, MarbleCol), (BlockKind.Crate, CrateCol), (BlockKind.Cylinder, BlueCol) };
            System.Func<float, float, Vector3> arc = (ang, rad) => O + new Vector3(Mathf.Sin(ang * Mathf.Deg2Rad), 0f, Mathf.Cos(ang * Mathf.Deg2Rad)) * rad;
            for (int i = 0; i < 5; i++)
            {
                float a = -2f * step + step * i; Vector3 c = arc(a, r);
                RPlate(root, p, c, a, 0.5f * DS + 0.05f, 0.56f); var b = Top(c);
                RColAt(root, b, a, 0f, 0f, 6, kinds[i].Item1, kinds[i].Item2, L, true);   // 한 칸씩 6개 (블록 수 35)
                if (i < 4 && i % 2 == 0)
                {
                    // 이웃 기둥(0-1, 2-3) 꼭대기를 잇는 3칸 부재(현의 중점, 상판 사이 틈 위에 걸침) + 그 위 금색 큐브.
                    // 3칸 부재는 기둥 중심 너머까지 닿아 이웃 부재와 겹치므로 한 칸 건너 하나씩만 놓는다
                    float am = a + step * 0.5f; Vector3 m = arc(am, r * Mathf.Cos(step * 0.5f * Mathf.Deg2Rad));
                    RBar(root, m + Vector3.up * (PedestalTop + 6 * DU), 3, BlockKind.Cube, RedCol, am, L);
                    RUnit(root, m + Vector3.up * (PedestalTop + 7 * DU), 1, BlockKind.Cube, GoldCol, am, L);
                }
                else if (i == 4) RUnitAt(root, b, a, 0f, 6, 1, BlockKind.Cube, GoldCol, L);   // 마지막 기둥 꼭대기 금색
            }
        }

        /// <summary>56 엇갈린 두 벽 (상판 2가 25°로 나란히, 앞뒤·좌우로 어긋남): 보라·상자 벽돌 벽 두 겹씩.</summary>
        static void BuildStaggeredWalls(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            var L = info.blocks; keepPlateShape = true; fixedFront = true;
            float yaw = 25f; Vector3 u = YawDir(yaw), n = YawBack(yaw);
            foreach (int s in new[] { -1, 1 })
            {
                Vector3 c = u * (s * 0.6f) + n * (s * 0.65f);
                RPlate(root, p, c, yaw, 2f * DS + 0.1f, 1.1f); var b = Top(c);
                RBrickWall(root, b, yaw, 4, 6, 2, BlockKind.Cube, s < 0 ? PurpleCol : BlueCol, s < 0 ? BlueCol : PurpleCol, L);
                RBarAt(root, b, yaw, -1f, 6, 2, BlockKind.Cube, RedCol, L, -0.5f); RBarAt(root, b, yaw, 1f, 6, 2, BlockKind.Cube, RedCol, L, 0.5f);
                RUnitAt(root, b, yaw, 0, 7, 1, BlockKind.Cube, GoldCol, L, 0f);
            }
        }

        /// <summary>57 꺾인 벽 (상판 4가 ±65°로 앞뒤 지그재그): 큐브·사탕 벽돌 벽 네 장.</summary>
        static void BuildZigzag4(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            var L = info.blocks; keepPlateShape = true; fixedFront = true;
            float[] xs = { -1.35f, -0.45f, 0.45f, 1.35f };
            for (int i = 0; i < 4; i++)
            {
                float yaw = (i % 2 == 0) ? 65f : -65f;
                Vector3 c = new Vector3(xs[i], 0f, (i % 2 == 0) ? -0.55f : 0.55f);
                RPlate(root, p, c, yaw, 1.5f * DS + 0.1f, 0.75f); var b = Top(c);
                RBrickWall(root, b, yaw, 3, 6, 1, BlockKind.Cube, (i % 2 == 0) ? BlueCol : PurpleCol, RedCol, L);
                RBarAt(root, b, yaw, 0, 6, 3, BlockKind.Cube, RedCol, L);
                RUnitAt(root, b, yaw, 0, 7, 1, BlockKind.Candy, PinkCol, L);
            }
        }

        /// <summary>58 화살촉 성 (상판 3: 앞 꼭짓점에서 뒤로 벌어지는 V 둘 + 뒤 정면 상판): 원통·큐브 두 겹 날개와 뒤의 대리석·사탕 탑.</summary>
        static void BuildArrowFort(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            var L = info.blocks; keepPlateShape = true; fixedFront = true;
            Vector3 A = new Vector3(0f, 0f, -0.8f);
            foreach (int side in new[] { -1, 1 })
            {
                float yaw = -side * 45f; Vector3 u = new Vector3(side * 0.7071f, 0f, 0.7071f);
                Vector3 c = A + u * (3f * DS); RPlate(root, p, c, yaw, 1.6f * DS, 1.1f); var b = Top(c);
                float jo = Vector3.Dot(YawBack(yaw), new Vector3(side, 0f, 0f)) > 0 ? 0.5f : -0.5f, ji = -jo;
                RCols(root, b, yaw, new[] { (BlockKind.Cylinder, BlueCol, 3, true), (BlockKind.Cylinder, IceCol, 3, true), (BlockKind.Cylinder, BlueCol, 3, true) }, L, ji);
                RBarAt(root, b, yaw, 0, 3, 3, BlockKind.Cube, RedCol, L, ji);
                RUnitAt(root, b, yaw, 0, 4, 2, BlockKind.Cylinder, GoldCol, L, ji);
                RCols(root, b, yaw, new[] { (BlockKind.Cube, BlueCol, 4, true), (BlockKind.Cube, BlueCol, 4, true), (BlockKind.Cube, BlueCol, 4, true) }, L, jo);
                RBarAt(root, b, yaw, 0, 4, 3, BlockKind.Cube, RedCol, L, jo);
            }
            Vector3 back = new Vector3(0f, 0f, 1.4f); RPlate(root, p, back, 0f, 1.5f * DS + 0.1f, 1.1f); var bb = Top(back);
            RCols(root, bb, 0f, new[] { (BlockKind.Stone, MarbleCol, 5, false), (BlockKind.Candy, PinkCol, 5, false), (BlockKind.Stone, MarbleCol, 5, false) }, L, -0.5f);
            RBarAt(root, bb, 0f, 0, 5, 3, BlockKind.Cube, RedCol, L, -0.5f);
            RUnitAt(root, bb, 0f, 0, 6, 1, BlockKind.Cube, GoldCol, L, -0.5f);
            RCols(root, bb, 0f, new[] { (BlockKind.Cylinder, BlueCol, 4, true), (BlockKind.Cylinder, BlueCol, 4, true), (BlockKind.Cylinder, BlueCol, 4, true) }, L, 0.5f);
            RBarAt(root, bb, 0f, 0, 4, 3, BlockKind.Cube, RedCol, L, 0.5f);
        }

        /// <summary>59 다이아몬드 십자 (큰 상판 1을 45° 돌린 마름모): 가운데 대리석 탑, 두 대각선 팔과 사분면을 채운 큐브·상자.</summary>
        static void BuildDiamondCross(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            var L = info.blocks; keepPlateShape = true; fixedFront = true;
            Vector3 c = Vector3.zero; float half = 2.5f * DS + 0.2f;
            RPlate(root, p, c, 45f, half, 2f * half); var b = Top(c);
            RCol(root, b, 6, BlockKind.Stone, MarbleCol, 45f, L); RUnit(root, b + Vector3.up * 6 * DU, 1, BlockKind.Cube, GoldCol, 45f, L);
            Vector3 u = YawDir(45f), v = YawBack(45f);
            foreach (int k in new[] { -2, -1, 1, 2 })
            {
                RCol(root, b + u * (k * DS), 3, BlockKind.Cube, BlueCol, 45f, L, true);
                RCol(root, b + v * (k * DS), 3, BlockKind.Crate, CrateCol, 45f, L, true);
            }
            foreach (float k in new[] { -1.5f, 1.5f })
            {
                RBar(root, b + u * (k * DS) + Vector3.up * 3 * DU, 2, BlockKind.Cube, RedCol, 45f, L); RUnit(root, b + u * (k * DS) + Vector3.up * 4 * DU, 1, BlockKind.Cube, PurpleCol, 45f, L);
                RBar(root, b + v * (k * DS) + Vector3.up * 3 * DU, 2, BlockKind.Cube, RedCol, -45f, L); RUnit(root, b + v * (k * DS) + Vector3.up * 4 * DU, 1, BlockKind.Cube, PurpleCol, 45f, L);
            }
            foreach (int a in new[] { -1, 1 }) foreach (int d in new[] { -1, 1 })
            {
                RCol(root, b + u * (a * DS) + v * (d * DS), 2, BlockKind.Ice, IceCol, 45f, L, true);
                RCol(root, b + u * (2 * a * DS) + v * (d * DS), 1, BlockKind.Ice, IceCol, 45f, L, true);
                RCol(root, b + u * (a * DS) + v * (2 * d * DS), 1, BlockKind.Ice, IceCol, 45f, L, true);
            }
        }

        /// <summary>60 대각선 벽 (상판 1을 30° 돌린 긴 벽, 7열 두 겹): 얼음·파랑 벽돌 벽 5단, 양 끝 대리석 기둥, 꼭대기 부재·금색 큐브.</summary>
        static void BuildDiagonalWall(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            var L = info.blocks; keepPlateShape = true; fixedFront = true;
            float yaw = 30f; Vector3 c = Vector3.zero; RPlate(root, p, c, yaw, 4f * DS + 0.15f, 1.1f); var b = Top(c);
            RBrickWall(root, b, yaw, 7, 5, 2, BlockKind.Ice, IceCol, BlueCol, L);
            RBarAt(root, b, yaw, 0, 5, 3, BlockKind.Cube, BlueCol, L, -0.5f); RBarAt(root, b, yaw, 0, 5, 3, BlockKind.Cube, BlueCol, L, 0.5f);
            RUnitAt(root, b, yaw, 0, 6, 1, BlockKind.Cube, GoldCol, L, -0.5f); RUnitAt(root, b, yaw, 0, 6, 1, BlockKind.Cube, GoldCol, L, 0.5f);
            RColAt(root, b, yaw, 4, 0f, 5, BlockKind.Stone, MarbleCol, L); RColAt(root, b, yaw, -4, 0f, 5, BlockKind.Stone, MarbleCol, L);
        }

        // ==================== 새 규칙 카탈로그 0~29 (정면 상판) ====================
        // 모든 구조물: keepPlateShape + fixedFront (배율 1, 앞면 z = FrontZ). 앞쪽 반폭 1.8 안에 들어오게 7열 이하, 높이 9단 이하.

        static Vector3 FrontPlate(Transform root, Palette p, float x, float z, float halfLen, float depth = 1.1f)
        { var c = new Vector3(x, 0f, z); RPlate(root, p, c, 0f, halfLen, depth); return Top(c); }
        static void Begin(LevelInfo info) { keepPlateShape = true; fixedFront = true; }
        static int Rows(LevelInfo info, int baseRows, int cap) => Balance.Grow(info.level, baseRows, 40, cap);

        /// <summary>0 벽돌 담: 6열 벽돌 벽 두 겹(4~6단) + 위 3칸 부재. 초반용.</summary>
        static void BuildN_BrickFence(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks; int rows = Rows(info, 4, 6);
            var b = FrontPlate(root, p, 0f, 0f, 3f * DS + 0.15f);
            RBrickWall(root, b, 0f, 6, rows, 2, BlockKind.Cube, PurpleCol, BlueCol, L);
            foreach (float j in new[] { -0.5f, 0.5f }) { RBarAt(root, b, 0f, -1.5f, rows, 3, BlockKind.Cube, RedCol, L, j); RBarAt(root, b, 0f, 1.5f, rows, 3, BlockKind.Cube, RedCol, L, j); }
        }

        /// <summary>1 원통 다발: 5열 원통(한 칸씩) 두 겹 3단 + 부재 + 큐브. 튜토리얼용.</summary>
        static void BuildN_CylinderBundle(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks; int h = Rows(info, 3, 4);
            var b = FrontPlate(root, p, 0f, 0f, 2.5f * DS + 0.15f);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                for (int k = -2; k <= 2; k++) RColAt(root, b, 0f, k, j, h, BlockKind.Cylinder, (k + 2) % 2 == 0 ? BlueCol : IceCol, L, true);
                RBarAt(root, b, 0f, -1.5f, h, 2, BlockKind.Cube, RedCol, L, j); RBarAt(root, b, 0f, 0.5f, h, 2, BlockKind.Cube, RedCol, L, j);
                RUnitAt(root, b, 0f, 2, h, 1, BlockKind.Cube, GoldCol, L, j);
                RUnitAt(root, b, 0f, -1, h + 1, 1, BlockKind.Cube, PurpleCol, L, j); RUnitAt(root, b, 0f, 1, h + 1, 1, BlockKind.Cube, PurpleCol, L, j);
            }
        }

        /// <summary>2 상자 선반: 상자 두 줄 사이에 판자 선반, 맨 위 사탕. 튜토리얼용.</summary>
        static void BuildN_CrateShelf(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 2.5f * DS + 0.15f);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                for (int k = -2; k <= 2; k++) RColAt(root, b, 0f, k, j, 2, BlockKind.Crate, CrateCol, L, true);
                RBarAt(root, b, 0f, -1f, 2, 3, BlockKind.Plank, WoodCol, L, j); RBarAt(root, b, 0f, 1.5f, 2, 2, BlockKind.Plank, WoodCol, L, j);
                for (int k = -2; k <= 2; k++) RCol(root, At(b, 0f, k, j, 3), 2, BlockKind.Crate, CrateCol, 0f, L, true);   // 3·4단
            }
            foreach (float j in new[] { -0.5f, 0.5f }) { RBarAt(root, b, 0f, -1f, 5, 3, BlockKind.Plank, WoodCol, L, j); RBarAt(root, b, 0f, 1.5f, 5, 2, BlockKind.Plank, WoodCol, L, j); RUnitAt(root, b, 0f, 0, 6, 2, BlockKind.Candy, PinkCol, L, j); }
        }

        /// <summary>3 통나무 탑: 세워 둔 통나무 5열 두 겹, 판자 선반, 상자, 위층 통나무. 초반용.</summary>
        static void BuildN_LogTower(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 2.5f * DS + 0.15f);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                for (int k = -2; k <= 2; k++) RColAt(root, b, 0f, k, j, 3, BlockKind.Log, WoodCol, L);
                RBarAt(root, b, 0f, -1f, 3, 3, BlockKind.Plank, WoodCol, L, j); RBarAt(root, b, 0f, 1.5f, 3, 2, BlockKind.Plank, WoodCol, L, j);
                for (int k = -2; k <= 2; k++) RUnitAt(root, b, 0f, k, 4, 1, BlockKind.Crate, CrateCol, L, j);
                RBarAt(root, b, 0f, -0.5f, 5, 2, BlockKind.Plank, WoodCol, L, j); RBarAt(root, b, 0f, 1.5f, 5, 2, BlockKind.Plank, WoodCol, L, j); RUnitAt(root, b, 0f, -2, 5, 1, BlockKind.Crate, CrateCol, L, j);
                for (float k = -1.5f; k <= 1.5f; k += 1f) RUnitAt(root, b, 0f, k, 6, 2, BlockKind.Log, WoodCol, L, j);
            }
        }

        /// <summary>4 얼음 벽: 7열 얼음 벽돌 벽 두 겹(4~6단) + 위 부재. 초반용.</summary>
        static void BuildN_IceWall(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks; int rows = Rows(info, 4, 6);
            var b = FrontPlate(root, p, 0f, 0f, 3.5f * DS + 0.15f);
            RBrickWall(root, b, 0f, 7, rows, 2, BlockKind.Ice, IceCol, BlueCol, L);
            foreach (float j in new[] { -0.5f, 0.5f }) { RBarAt(root, b, 0f, -2f, rows, 3, BlockKind.Cube, BlueCol, L, j); RBarAt(root, b, 0f, 2f, rows, 3, BlockKind.Cube, BlueCol, L, j); RUnitAt(root, b, 0f, 0, rows, 1, BlockKind.Ice, IceCol, L, j); }
        }

        /// <summary>5 삼중 받침대: 작은 상판 셋, 각각 두 열 두 겹 3단 기둥 + 부재 + 큐브. 초반용.</summary>
        static void BuildN_TriplePedestal(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks; independentPedestals = true;
            var kinds = new[] { (BlockKind.Cylinder, BlueCol), (BlockKind.Candy, PinkCol), (BlockKind.Crate, CrateCol) };
            for (int i = 0; i < 3; i++)
            {
                var b = FrontPlate(root, p, (i - 1) * 1.2f, 0f, 0.5f * DS + 0.25f);
                foreach (float j in new[] { -0.5f, 0.5f })
                {
                    RColAt(root, b, 0f, -0.5f, j, 3, kinds[i].Item1, kinds[i].Item2, L, kinds[i].Item1 != BlockKind.Candy);
                    RColAt(root, b, 0f, 0.5f, j, 3, kinds[i].Item1, kinds[i].Item2, L, kinds[i].Item1 != BlockKind.Candy);
                    RBarAt(root, b, 0f, 0f, 3, 2, BlockKind.Cube, RedCol, L, j);
                    RUnitAt(root, b, 0f, 0, 4, 1, BlockKind.Cube, GoldCol, L, j);
                }
            }
        }

        /// <summary>6 피라미드: 7열에서 1열까지 줄마다 하나씩 줄어드는 벽돌식 피라미드 두 겹.</summary>
        static void BuildN_Pyramid(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 3.5f * DS + 0.15f);
            foreach (float j in new[] { -0.5f, 0.5f })
                for (int row = 0; row < 7; row++)
                {
                    int n = 7 - row; var col = row % 3 == 0 ? BlueCol : row % 3 == 1 ? PurpleCol : IceCol; var kind = row % 3 == 2 ? BlockKind.Ice : BlockKind.Cube;
                    for (int i = 0; i < n; i++) RUnitAt(root, b, 0f, i - (n - 1) * 0.5f, row, 1, kind, col, L, j);
                }
        }

        /// <summary>7 성문: 탑 둘(3열 벽돌 5단 두 겹) 사이를 3칸 인방으로 잇고 위에 큐브.</summary>
        static void BuildN_Gate(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            foreach (int side in new[] { -1, 1 })
            {
                var b = FrontPlate(root, p, side * 1.15f, 0f, 1.5f * DS + 0.1f);
                RBrickWall(root, b, 0f, 3, 5, 2, BlockKind.Cube, SlateCol, BlueCol, L);
            }
            var c = Top(Vector3.zero);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                RBarAt(root, c, 0f, 0f, 5, 3, BlockKind.Cube, RedCol, L, j);
                for (int k = -3; k <= 3; k += 3) RBarAt(root, c, 0f, k, 6, 3, BlockKind.Cube, RedCol, L, j);
                RUnitAt(root, c, 0f, 0, 7, 1, BlockKind.Cube, GoldCol, L, j);
            }
        }

        /// <summary>8 쌍둥이 탑: 3열 벽돌 7단 두 겹 탑 둘, 꼭대기 금색 큐브.</summary>
        static void BuildN_TwinTowers(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks; independentPedestals = true; int rows = Rows(info, 6, 8);
            foreach (int side in new[] { -1, 1 })
            {
                var b = FrontPlate(root, p, side * 1.0f, 0f, 1.5f * DS + 0.1f);
                RBrickWall(root, b, 0f, 3, rows, 2, BlockKind.Cube, side < 0 ? BlueCol : PurpleCol, RedCol, L);
                foreach (float j in new[] { -0.5f, 0.5f }) { RBarAt(root, b, 0f, 0f, rows, 3, BlockKind.Cube, RedCol, L, j); RUnitAt(root, b, 0f, 0, rows + 1, 1, BlockKind.Cube, GoldCol, L, j); }
            }
        }

        /// <summary>9 계단: 7열이 왼쪽 2단에서 오른쪽 8단까지 한 칸씩 높아지는 계단 두 겹.</summary>
        static void BuildN_Staircase(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 3.5f * DS + 0.15f);
            foreach (float j in new[] { -0.5f, 0.5f })
                for (int k = -3; k <= 3; k++) RColAt(root, b, 0f, k, j, k + 5, k % 2 == 0 ? BlockKind.Cube : BlockKind.Crate, k % 2 == 0 ? BlueCol : CrateCol, L, true);
        }

        /// <summary>10 요새: 5열 상자·판자 벽 두 겹 가운데, 양 끝 대리석 모서리 탑(6단)과 빨강 큐브, 가운데 사탕.</summary>
        static void BuildN_Fortress(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 3.5f * DS + 0.15f);
            RBrickWall(root, b, 0f, 5, 5, 2, BlockKind.Crate, CrateCol, WoodCol, L);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                foreach (int k in new[] { -3, 3 }) { RColAt(root, b, 0f, k, j, 6, BlockKind.Stone, MarbleCol, L); RUnitAt(root, b, 0f, k, 6, 1, BlockKind.Cube, RedCol, L, j); }
                RBarAt(root, b, 0f, 0f, 5, 3, BlockKind.Plank, WoodCol, L, j);
                RUnitAt(root, b, 0f, 0, 6, 2, BlockKind.Candy, PinkCol, L, j);
            }
        }

        /// <summary>11 돌기둥 원진: 둥근 상판 위 반지름 1.25 원에 대리석 기둥 8·파랑 원통 8이 번갈아, 안쪽 사탕 넷, 가운데 원통 탑.</summary>
        static void BuildN_StoneRing(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            Pedestal(root, Vector3.zero, 1.58f, p, false, 1, 0f, 3.16f); var b = Top(Vector3.zero);
            for (int i = 0; i < 16; i++)
            {
                float a = i * 22.5f * Mathf.Deg2Rad; Vector3 pos = b + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 1.25f;
                if (i % 2 == 0) { RCol(root, pos, 4, BlockKind.Stone, MarbleCol, 0f, L); RUnit(root, pos + Vector3.up * 4 * DU, 1, BlockKind.Cube, RedCol, 0f, L); }
                else RCol(root, pos, 3, BlockKind.Cylinder, BlueCol, 0f, L, true);
            }
            for (int i = 0; i < 4; i++) { float a = (45f + 90f * i) * Mathf.Deg2Rad; RCol(root, b + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.62f, 3, BlockKind.Candy, PinkCol, 0f, L); }
            RCol(root, b, 6, BlockKind.Cylinder, BlueCol, 0f, L); RUnit(root, b + Vector3.up * 6 * DU, 1, BlockKind.Cylinder, GoldCol, 0f, L);
        }

        /// <summary>12 창문 벽: 7열 벽 두 겹에 창 둘(2·3단, k ±2)이 뚫리고 위에 인방 부재.</summary>
        static void BuildN_WindowWall(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 3.5f * DS + 0.15f);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                for (int k = -3; k <= 3; k++) RUnitAt(root, b, 0f, k, 0, 1, BlockKind.Cube, BlueCol, L, j);
                RUnitAt(root, b, 0f, -3, 1, 1, BlockKind.Cube, BlueCol, L, j); RBarAt(root, b, 0f, -1.5f, 1, 2, BlockKind.Cube, PurpleCol, L, j); RBarAt(root, b, 0f, 0.5f, 1, 2, BlockKind.Cube, PurpleCol, L, j); RBarAt(root, b, 0f, 2.5f, 1, 2, BlockKind.Cube, PurpleCol, L, j);
                foreach (int k in new[] { -3, -1, 0, 1, 3 }) RCol(root, At(b, 0f, k, j, 2), 2, BlockKind.Cube, BlueCol, 0f, L, true);   // 2·3단 (창은 k ±2)
                RBarAt(root, b, 0f, -2f, 4, 2, BlockKind.Cube, PurpleCol, L, j); RBarAt(root, b, 0f, 2f, 4, 2, BlockKind.Cube, PurpleCol, L, j); RUnitAt(root, b, 0f, 0, 4, 1, BlockKind.Cube, BlueCol, L, j);
                for (int k = -3; k <= 3; k++) RUnitAt(root, b, 0f, k, 5, 1, BlockKind.Cube, BlueCol, L, j);
                RBarAt(root, b, 0f, -2f, 6, 3, BlockKind.Cube, RedCol, L, j); RBarAt(root, b, 0f, 2f, 6, 3, BlockKind.Cube, RedCol, L, j);
            }
        }

        /// <summary>13 아치 문: 2열 벽돌 6단 탑 둘, 위를 3칸 부재로 이어 아치, 그 위 큐브와 사탕.</summary>
        static void BuildN_ArchGate(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            foreach (int side in new[] { -1, 1 }) { var b = FrontPlate(root, p, side * 0.9f, 0f, 1f * DS + 0.1f); RBrickWall(root, b, 0f, 2, 6, 2, BlockKind.Ice, IceCol, BlueCol, L); }
            var c = Top(Vector3.zero);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                RBarAt(root, c, 0f, 0f, 6, 3, BlockKind.Cube, RedCol, L, j);
                for (int k = -1; k <= 1; k++) RUnitAt(root, c, 0f, k, 7, 1, BlockKind.Cube, BlueCol, L, j);
                RUnitAt(root, c, 0f, 0, 8, 2, BlockKind.Candy, PinkCol, L, j);
            }
        }

        /// <summary>14 신전: 대리석 기둥 넷과 사이의 보라 큐브 기둥 셋(4단) 두 겹, 판자 지붕 두 줄과 큐브.</summary>
        static void BuildN_Temple(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 3.5f * DS + 0.15f);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                foreach (int k in new[] { -3, -1, 1, 3 }) RColAt(root, b, 0f, k, j, 4, BlockKind.Stone, MarbleCol, L);
                foreach (int k in new[] { -2, 0, 2 }) RColAt(root, b, 0f, k, j, 4, BlockKind.Cube, PurpleCol, L, true);
                RBarAt(root, b, 0f, -2f, 4, 3, BlockKind.Plank, WoodCol, L, j); RBarAt(root, b, 0f, 2f, 4, 3, BlockKind.Plank, WoodCol, L, j); RUnitAt(root, b, 0f, 0, 4, 1, BlockKind.Crate, CrateCol, L, j);
                RBarAt(root, b, 0f, -1.5f, 5, 2, BlockKind.Plank, WoodCol, L, j); RBarAt(root, b, 0f, 0.5f, 5, 2, BlockKind.Plank, WoodCol, L, j); RUnitAt(root, b, 0f, 2, 5, 1, BlockKind.Crate, CrateCol, L, j); RUnitAt(root, b, 0f, -3, 5, 1, BlockKind.Crate, CrateCol, L, j);
                for (int k = -1; k <= 1; k++) RUnitAt(root, b, 0f, k, 6, 1, BlockKind.Cube, RedCol, L, j);
            }
        }

        /// <summary>15 세 탑: 작은 상판 셋에 상자 기둥(두 열 두 겹) 5·7·5단, 부재와 금색 큐브.</summary>
        static void BuildN_ThreeTowers(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks; independentPedestals = true;
            int[] hs = { 5, 7, 5 };
            for (int i = 0; i < 3; i++)
            {
                var b = FrontPlate(root, p, (i - 1) * 1.25f, 0f, 0.5f * DS + 0.25f);
                foreach (float j in new[] { -0.5f, 0.5f })
                {
                    RColAt(root, b, 0f, -0.5f, j, hs[i], BlockKind.Crate, CrateCol, L, true); RColAt(root, b, 0f, 0.5f, j, hs[i], BlockKind.Crate, CrateCol, L, true);
                    RBarAt(root, b, 0f, 0f, hs[i], 2, BlockKind.Plank, WoodCol, L, j); RUnitAt(root, b, 0f, 0, hs[i] + 1, 1, BlockKind.Cube, GoldCol, L, j);
                }
            }
        }

        /// <summary>16 벽돌 탑: 4열 벽돌 8단 두 겹 탑, 위 부재와 금색 큐브.</summary>
        static void BuildN_BrickTower(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks; int rows = Rows(info, 7, 8);
            var b = FrontPlate(root, p, 0f, 0f, 2f * DS + 0.1f);
            RBrickWall(root, b, 0f, 4, rows, 2, BlockKind.Cube, RedCol, PurpleCol, L);
            foreach (float j in new[] { -0.5f, 0.5f }) { RBarAt(root, b, 0f, -0.5f, rows, 3, BlockKind.Cube, BlueCol, L, j); RUnitAt(root, b, 0f, 1.5f, rows, 1, BlockKind.Cube, BlueCol, L, j); RUnitAt(root, b, 0f, 0, rows + 1, 1, BlockKind.Cube, GoldCol, L, j); }
        }

        /// <summary>17 H자 벽: 2열 탑 둘(6단 두 겹, 안쪽 열은 3·4단이 비어 있음)과 가운데 큐브 기둥 위에 3·4단 3칸 부재를 걸쳐 H자. 위 사탕·부재·금색.</summary>
        static void BuildN_HWall(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            foreach (int side in new[] { -1, 1 })
            {
                // 상판 사이 간격 0.88 (규칙 ⑤ ≥ 1.2칸). 안쪽 열은 x ±0.77
                var b = FrontPlate(root, p, side * 1.0f, 0f, 1f * DS + 0.1f);
                foreach (float j in new[] { -0.5f, 0.5f })
                    for (int row = 0; row < 6; row++)
                    {
                        RUnitAt(root, b, 0f, side * 0.5f, row, 1, BlockKind.Cube, BlueCol, L, j);                       // 바깥 열
                        if (row < 2 || row > 3) RUnitAt(root, b, 0f, -side * 0.5f, row, 1, BlockKind.Cube, PurpleCol, L, j);   // 안쪽 열 (3·4단 비움)
                    }
            }
            var c = Top(Vector3.zero);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                // 가로대: 4칸 부재(±0.915)가 양쪽 안쪽 열(±0.77) 위에 걸친다. 상판 사이 틈 위에는 기둥을 세우지 않는다
                RBarAt(root, c, 0f, 0f, 2, 4, BlockKind.Cube, RedCol, L, j); RBarAt(root, c, 0f, 0f, 3, 4, BlockKind.Cube, RedCol, L, j);
                RUnitAt(root, c, 0f, 0, 4, 2, BlockKind.Candy, PinkCol, L, j);
                RBarAt(root, c, 0f, 0f, 6, 4, BlockKind.Cube, RedCol, L, j); RUnitAt(root, c, 0f, 0, 7, 1, BlockKind.Cube, GoldCol, L, j);
            }
        }

        /// <summary>18 엇갈린 겹 벽: 6열 벽돌 벽 두 겹인데 뒷겹이 반 칸 옆으로 어긋나 정면에서 틈이 엇갈려 보인다.</summary>
        static void BuildN_OffsetWall(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks; int rows = Rows(info, 5, 6);
            var b = FrontPlate(root, p, 0f, 0f, 3.5f * DS + 0.15f);
            RBrickWall(root, b + YawBack(0f) * (-0.5f * DS) - Vector3.right * (0.5f * DS), 0f, 6, rows, 1, BlockKind.Cube, BlueCol, PurpleCol, L);
            RBrickWall(root, b + YawBack(0f) * (0.5f * DS) + Vector3.right * (0.5f * DS), 0f, 6, rows, 1, BlockKind.Ice, IceCol, BlueCol, L);
            for (int k = -2; k <= 2; k += 2) RUnitAt(root, b, 0f, k, rows, 1, BlockKind.Cube, RedCol, L, -0.5f);
            for (int k = -2; k <= 2; k += 2) RUnitAt(root, b, 0f, k + 1, rows, 1, BlockKind.Cube, RedCol, L, 0.5f);
        }

        /// <summary>19 둥근 성: 둥근 상판 위 큐브 12개 링 3단(가운데를 향해 돌림), 위에 빨강 큐브, 안쪽 사탕 넷과 가운데 대리석 탑.</summary>
        static void BuildN_RoundCastle(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            Pedestal(root, Vector3.zero, 1.45f, p, false, 1, 0f, 2.9f); var b = Top(Vector3.zero);
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f; float ar = a * Mathf.Deg2Rad; Vector3 pos = b + new Vector3(Mathf.Sin(ar), 0f, Mathf.Cos(ar)) * 1.05f;
                RCol(root, pos, 3, BlockKind.Cube, i % 2 == 0 ? BlueCol : PurpleCol, a, L, true);
                RUnit(root, pos + Vector3.up * 3 * DU, 1, BlockKind.Cube, RedCol, a, L);
            }
            for (int i = 0; i < 4; i++) { float a = (45f + 90f * i) * Mathf.Deg2Rad; RCol(root, b + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.55f, 3, BlockKind.Candy, PinkCol, 0f, L); }
            RCol(root, b, 6, BlockKind.Stone, MarbleCol, 0f, L); RUnit(root, b + Vector3.up * 6 * DU, 1, BlockKind.Cube, GoldCol, 0f, L);
        }

        /// <summary>20 다리: 2열 탑 둘(5단 두 겹) 위를 3칸 부재로 잇고 그 위 큐브 셋과 사탕.</summary>
        static void BuildN_Bridge(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            foreach (int side in new[] { -1, 1 }) { var b = FrontPlate(root, p, side * 1.1f, 0f, 1f * DS + 0.1f); RBrickWall(root, b, 0f, 2, 5, 2, BlockKind.Crate, CrateCol, WoodCol, L); }
            var c = Top(Vector3.zero);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                RBarAt(root, c, 0f, 0f, 5, 3, BlockKind.Plank, WoodCol, L, j);
                for (int k = -1; k <= 1; k++) RUnitAt(root, c, 0f, k, 6, 1, BlockKind.Crate, CrateCol, L, j);
                RBarAt(root, c, 0f, 0f, 7, 3, BlockKind.Plank, WoodCol, L, j);
                RUnitAt(root, c, 0f, 0, 8, 1, BlockKind.Candy, PinkCol, L, j);
            }
        }

        /// <summary>21 계단 성: 작은 상판 셋에 2열 벽돌 탑 4·8·4단 두 겹, 부재와 금색 큐브.</summary>
        static void BuildN_StepCastle(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks; independentPedestals = true;
            int[] hs = { 4, 8, 4 };
            for (int i = 0; i < 3; i++)
            {
                var b = FrontPlate(root, p, (i - 1) * 1.25f, 0f, 0.5f * DS + 0.25f);
                RBrickWall(root, b, 0f, 2, hs[i], 2, BlockKind.Cube, i == 1 ? RedCol : BlueCol, i == 1 ? GoldCol : PurpleCol, L);
                foreach (float j in new[] { -0.5f, 0.5f }) { RBarAt(root, b, 0f, 0f, hs[i], 2, BlockKind.Cube, PurpleCol, L, j); RUnitAt(root, b, 0f, 0, hs[i] + 1, 1, BlockKind.Cube, GoldCol, L, j); }
            }
        }

        /// <summary>22 격자 탑: 큐브 기둥 셋(k −2.5·0·2.5)과 그 사이를 잇는 3칸 부재가 줄마다 번갈아 격자를 이룬다. 두 겹.</summary>
        static void BuildN_Lattice(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks; int rows = Rows(info, 7, 8);
            var b = FrontPlate(root, p, 0f, 0f, 3.5f * DS + 0.15f);
            foreach (float j in new[] { -0.5f, 0.5f })
                for (int row = 0; row < rows; row++)
                {
                    // 짝수 줄: 큐브 셋(k −3·0·3), 홀수 줄: 3칸 부재 둘이 큐브 위에 걸쳐 만난다 (k −3~0, 0~3)
                    if (row % 2 == 0) { foreach (float k in new[] { -2.5f, 0f, 2.5f }) RUnitAt(root, b, 0f, k, row, 1, BlockKind.Cube, BlueCol, L, j); }   // 바깥 큐브는 부재 끝에 온전히 얹힌다
                    else { RBarAt(root, b, 0f, -1.5f, row, 3, BlockKind.Cube, RedCol, L, j); RBarAt(root, b, 0f, 1.5f, row, 3, BlockKind.Cube, RedCol, L, j); }
                }
        }

        /// <summary>23 버섯 탑: 사탕 줄기 둘(5단) 위 부재 갓, 그 위 큐브·사탕·금색. 양옆 상자 더미.</summary>
        static void BuildN_Mushroom(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 2.5f * DS + 0.15f);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                RColAt(root, b, 0f, -1, j, 5, BlockKind.Candy, PinkCol, L); RColAt(root, b, 0f, 1, j, 5, BlockKind.Candy, PinkCol, L);
                RColAt(root, b, 0f, -2, j, 4, BlockKind.Crate, CrateCol, L, true); RColAt(root, b, 0f, 2, j, 4, BlockKind.Crate, CrateCol, L, true);
                RBarAt(root, b, 0f, 0f, 5, 3, BlockKind.Cube, RedCol, L, j);
                for (int k = -1; k <= 1; k++) RUnitAt(root, b, 0f, k, 6, 1, BlockKind.Cube, RedCol, L, j);
                RUnitAt(root, b, 0f, 0, 7, 2, BlockKind.Candy, PinkCol, L, j);
            }
        }

        /// <summary>24 처마 벽: 6열 벽돌 벽 4단 두 겹 위에 3칸 부재 처마 두 줄과 큐브.</summary>
        static void BuildN_EaveWall(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 3f * DS + 0.15f);
            RBrickWall(root, b, 0f, 6, 4, 2, BlockKind.Cube, PurpleCol, BlueCol, L);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                RBarAt(root, b, 0f, -1.5f, 4, 3, BlockKind.Cube, RedCol, L, j); RBarAt(root, b, 0f, 1.5f, 4, 3, BlockKind.Cube, RedCol, L, j);
                RUnitAt(root, b, 0f, -2.5f, 5, 1, BlockKind.Cube, BlueCol, L, j); RBarAt(root, b, 0f, -0.5f, 5, 3, BlockKind.Cube, RedCol, L, j); RUnitAt(root, b, 0f, 1.5f, 5, 1, BlockKind.Cube, BlueCol, L, j); RUnitAt(root, b, 0f, 2.5f, 5, 1, BlockKind.Cube, BlueCol, L, j);
                for (float k = -1.5f; k <= 1.5f; k += 1f) RUnitAt(root, b, 0f, k, 6, 1, BlockKind.Cube, PurpleCol, L, j);
                RBarAt(root, b, 0f, -0.5f, 7, 2, BlockKind.Cube, RedCol, L, j); RUnitAt(root, b, 0f, 1.5f, 7, 1, BlockKind.Cube, GoldCol, L, j);
            }
        }

        /// <summary>25 무늬 벽: 7열 5단 한 칸 큐브를 얼음·보라 체크무늬로 두 겹, 위 부재.</summary>
        static void BuildN_PatternWall(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks; int rows = Rows(info, 5, 6);
            var b = FrontPlate(root, p, 0f, 0f, 3.5f * DS + 0.15f);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                for (int row = 0; row < rows; row++) for (int k = -3; k <= 3; k++)
                    { bool ice = (k + row + (j > 0 ? 1 : 0)) % 2 == 0; RUnitAt(root, b, 0f, k, row, 1, ice ? BlockKind.Ice : BlockKind.Cube, ice ? IceCol : PurpleCol, L, j); }
                RBarAt(root, b, 0f, -2f, rows, 3, BlockKind.Cube, BlueCol, L, j); RBarAt(root, b, 0f, 2f, rows, 3, BlockKind.Cube, BlueCol, L, j); RUnitAt(root, b, 0f, 0, rows, 1, BlockKind.Cube, GoldCol, L, j);
            }
        }

        /// <summary>26 창문 탑: 3열 벽돌 9단 두 겹 탑에 가운데 창(3·4단, 6·7단), 꼭대기 부재와 금색.</summary>
        static void BuildN_WindowTower(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 1.5f * DS + 0.1f);
            foreach (float j in new[] { -0.5f, 0.5f })
                for (int row = 0; row < 8; row++)
                {
                    bool window = row == 3 || row == 4 || row == 6;
                    if (window) { RUnitAt(root, b, 0f, -1, row, 1, BlockKind.Cube, SlateCol, L, j); RUnitAt(root, b, 0f, 1, row, 1, BlockKind.Cube, SlateCol, L, j); }
                    else if (row % 2 == 0) for (int k = -1; k <= 1; k++) RUnitAt(root, b, 0f, k, row, 1, BlockKind.Cube, BlueCol, L, j);
                    else { bool left = (row / 2) % 2 == 0; RBarAt(root, b, 0f, left ? -0.5f : 0.5f, row, 2, BlockKind.Cube, RedCol, L, j); RUnitAt(root, b, 0f, left ? 1 : -1, row, 1, BlockKind.Cube, BlueCol, L, j); }
                }
            foreach (float j in new[] { -0.5f, 0.5f }) { RBarAt(root, b, 0f, 0f, 8, 3, BlockKind.Cube, RedCol, L, j); RUnitAt(root, b, 0f, 0, 9, 1, BlockKind.Cube, GoldCol, L, j); }
        }

        /// <summary>27 네 기둥: 작은 상판 넷에 원통·사탕이 번갈아 선 6단 기둥 두 겹, 위 빨강 큐브.</summary>
        static void BuildN_FourPillars(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks; independentPedestals = true;
            for (int i = 0; i < 4; i++)
            {
                var b = FrontPlate(root, p, (i - 1.5f) * 1.15f, 0f, 0.3f, 1.1f);   // 상판 폭 0.6, 간격 0.55 (규칙 ⑤), 전체 폭 ±1.95
                bool cyl = i % 2 == 0;
                foreach (float j in new[] { -0.5f, 0.5f }) { RColAt(root, b, 0f, 0, j, 6, cyl ? BlockKind.Cylinder : BlockKind.Candy, cyl ? BlueCol : PinkCol, L, cyl); RUnitAt(root, b, 0f, 0, 6, 1, BlockKind.Cube, RedCol, L, j); }
            }
        }

        /// <summary>28 상자 벽과 곁탑: 5열 상자·보라 벽돌 벽 두 겹 옆에 작은 상판의 사탕 탑.</summary>
        static void BuildN_WallAndTower(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks; independentPedestals = true;
            var b = FrontPlate(root, p, -0.55f, 0f, 2.5f * DS + 0.1f);
            RBrickWall(root, b, 0f, 5, 5, 2, BlockKind.Crate, CrateCol, PurpleCol, L);
            foreach (float j in new[] { -0.5f, 0.5f }) { RBarAt(root, b, 0f, -1f, 5, 3, BlockKind.Plank, WoodCol, L, j); RBarAt(root, b, 0f, 1.5f, 5, 2, BlockKind.Plank, WoodCol, L, j); }
            var t = FrontPlate(root, p, 1.4f, 0f, 0.43f, 1.1f);
            foreach (float j in new[] { -0.5f, 0.5f }) { RColAt(root, t, 0f, 0, j, 6, BlockKind.Candy, PinkCol, L); RUnitAt(root, t, 0f, 0, 6, 1, BlockKind.Cube, GoldCol, L, j); }
        }

        /// <summary>29 통나무 벽: 세운 통나무 6열 두 겹 위 판자, 상자 줄, 다시 판자, 위층 통나무 넷.</summary>
        static void BuildN_LogWall(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 3f * DS + 0.15f);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                for (float k = -2.5f; k <= 2.5f; k += 1f) RColAt(root, b, 0f, k, j, 3, BlockKind.Log, WoodCol, L);
                RBarAt(root, b, 0f, -1.5f, 3, 3, BlockKind.Plank, WoodCol, L, j); RBarAt(root, b, 0f, 1.5f, 3, 3, BlockKind.Plank, WoodCol, L, j);
                for (float k = -2.5f; k <= 2.5f; k += 1f) RUnitAt(root, b, 0f, k, 4, 1, BlockKind.Crate, CrateCol, L, j);
                RBarAt(root, b, 0f, -1.5f, 5, 3, BlockKind.Plank, WoodCol, L, j); RBarAt(root, b, 0f, 1.5f, 5, 3, BlockKind.Plank, WoodCol, L, j);
                for (float k = -1.5f; k <= 1.5f; k += 1f) RUnitAt(root, b, 0f, k, 6, 2, BlockKind.Log, WoodCol, L, j);
                RBarAt(root, b, 0f, -0.5f, 8, 2, BlockKind.Plank, WoodCol, L, j); RUnitAt(root, b, 0f, 1.5f, 8, 1, BlockKind.Cube, GoldCol, L, j);
            }
        }

        // ==================== 새 규칙 카탈로그 44~75 (20레벨 이후 추가 32종) ====================

        /// <summary>44 원통 벌집: 원통이 줄마다 7·6개로 반 칸씩 어긋나 벌집처럼 쌓인다(5단, 두 겹). 위 부재.</summary>
        static void BuildN_CylinderHoneycomb(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 3.5f * DS + 0.15f);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                for (int row = 0; row < 5; row++) { int n = row % 2 == 0 ? 7 : 6; for (int i = 0; i < n; i++) RUnitAt(root, b, 0f, i - (n - 1) * 0.5f, row, 1, BlockKind.Cylinder, row % 2 == 0 ? BlueCol : IceCol, L, j); }
                RBarAt(root, b, 0f, -2f, 5, 3, BlockKind.Cube, RedCol, L, j); RBarAt(root, b, 0f, 2f, 5, 3, BlockKind.Cube, RedCol, L, j); RUnitAt(root, b, 0f, 0, 5, 1, BlockKind.Cube, GoldCol, L, j);
            }
        }

        /// <summary>45 얼음 성: 탑 둘(2열 얼음 벽돌 7단) 사이 낮은 담(2열 3단), 위에 큐브 성가퀴. 상판 셋.</summary>
        static void BuildN_IceCastle(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks; independentPedestals = true;
            foreach (int side in new[] { -1, 1 })
            {
                var b = FrontPlate(root, p, side * 1.62f, 0f, 1f * DS + 0.1f);
                RBrickWall(root, b, 0f, 2, 7, 2, BlockKind.Ice, IceCol, BlueCol, L);
                foreach (float j in new[] { -0.5f, 0.5f }) { RUnitAt(root, b, 0f, -0.5f, 7, 1, BlockKind.Cube, BlueCol, L, j); RUnitAt(root, b, 0f, 0.5f, 7, 1, BlockKind.Cube, RedCol, L, j); }
            }
            // 가운데 담은 2열(폭 1.12): 옆 탑과 간격 0.55를 두고 전체 폭 ±2.15 안
            var c = FrontPlate(root, p, 0f, 0f, 1f * DS + 0.05f);
            RBrickWall(root, c, 0f, 2, 3, 2, BlockKind.Ice, IceCol, BlueCol, L);
            foreach (float j in new[] { -0.5f, 0.5f }) { RBarAt(root, c, 0f, 0f, 3, 2, BlockKind.Cube, RedCol, L, j); RUnitAt(root, c, 0f, -0.5f, 4, 1, BlockKind.Ice, IceCol, L, j); RUnitAt(root, c, 0f, 0.5f, 4, 1, BlockKind.Ice, IceCol, L, j); }
        }

        /// <summary>46 사탕 숲: 사탕 기둥 7열이 5·5·3·7·3·5·5단으로 들쭉날쭉, 두 겹, 바깥 짝은 부재로 잇는다.</summary>
        static void BuildN_CandyForest(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 3.5f * DS + 0.15f);
            int[] hs = { 5, 5, 3, 7, 3, 5, 5 };
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                for (int k = -3; k <= 3; k++) RColAt(root, b, 0f, k, j, hs[k + 3], BlockKind.Candy, (k + 3) % 2 == 0 ? PinkCol : RedCol, L);
                RBarAt(root, b, 0f, -2.5f, 5, 2, BlockKind.Cube, BlueCol, L, j); RBarAt(root, b, 0f, 2.5f, 5, 2, BlockKind.Cube, BlueCol, L, j);
                RUnitAt(root, b, 0f, -1, 3, 1, BlockKind.Cube, BlueCol, L, j); RUnitAt(root, b, 0f, 1, 3, 1, BlockKind.Cube, BlueCol, L, j);
                RUnitAt(root, b, 0f, 0, 7, 1, BlockKind.Cube, GoldCol, L, j);
            }
        }

        /// <summary>47 통나무 오두막: 통나무 벽 5열 두 겹 3단, 판자 처마, 상자 다락, 위로 좁아지는 지붕 큐브.</summary>
        static void BuildN_LogCabin(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 2.5f * DS + 0.15f);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                for (int k = -2; k <= 2; k++) RColAt(root, b, 0f, k, j, 3, BlockKind.Log, WoodCol, L);
                RBarAt(root, b, 0f, -1f, 3, 3, BlockKind.Plank, WoodCol, L, j); RBarAt(root, b, 0f, 1.5f, 3, 2, BlockKind.Plank, WoodCol, L, j);
                for (int k = -2; k <= 2; k++) RUnitAt(root, b, 0f, k, 4, 1, BlockKind.Crate, CrateCol, L, j);
                for (int k = -1; k <= 1; k++) RUnitAt(root, b, 0f, k, 5, 1, BlockKind.Cube, RedCol, L, j);
                RUnitAt(root, b, 0f, 0, 6, 1, BlockKind.Cube, RedCol, L, j);
            }
        }

        /// <summary>48 돌 아치: 대리석 기둥 둘(6단) 위 인방 부재, 그 위 큐브 셋, 안쪽에 사탕·큐브 더미. 두 겹.</summary>
        static void BuildN_StoneArch(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 2.5f * DS + 0.15f);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                RColAt(root, b, 0f, -2, j, 6, BlockKind.Stone, MarbleCol, L); RColAt(root, b, 0f, 2, j, 6, BlockKind.Stone, MarbleCol, L);
                RColAt(root, b, 0f, -1, j, 6, BlockKind.Stone, MarbleCol, L); RColAt(root, b, 0f, 1, j, 6, BlockKind.Stone, MarbleCol, L);
                RColAt(root, b, 0f, 0, j, 3, BlockKind.Candy, PinkCol, L);
                RBarAt(root, b, 0f, 0f, 6, 3, BlockKind.Cube, RedCol, L, j);
                RUnitAt(root, b, 0f, -2, 6, 1, BlockKind.Cube, RedCol, L, j); RUnitAt(root, b, 0f, 2, 6, 1, BlockKind.Cube, RedCol, L, j);
                for (int k = -1; k <= 1; k++) RUnitAt(root, b, 0f, k, 7, 1, BlockKind.Cube, BlueCol, L, j);
                RUnitAt(root, b, 0f, 0, 8, 1, BlockKind.Cube, GoldCol, L, j);
            }
        }

        /// <summary>49 계단 피라미드: 큐브 7·5·3·1개 줄 사이에 3칸 부재 층을 끼운 피라미드 두 겹.</summary>
        static void BuildN_StepPyramid(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 3.5f * DS + 0.15f);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                for (int k = -3; k <= 3; k++) RUnitAt(root, b, 0f, k, 0, 1, BlockKind.Cube, PurpleCol, L, j);
                RBarAt(root, b, 0f, -2f, 1, 3, BlockKind.Cube, BlueCol, L, j); RBarAt(root, b, 0f, 2f, 1, 3, BlockKind.Cube, BlueCol, L, j); RUnitAt(root, b, 0f, 0, 1, 1, BlockKind.Cube, PurpleCol, L, j);
                for (int k = -2; k <= 2; k++) RUnitAt(root, b, 0f, k, 2, 1, BlockKind.Cube, PurpleCol, L, j);
                RBarAt(root, b, 0f, -1f, 3, 3, BlockKind.Cube, BlueCol, L, j); RBarAt(root, b, 0f, 1.5f, 3, 2, BlockKind.Cube, BlueCol, L, j);
                for (int k = -1; k <= 1; k++) RUnitAt(root, b, 0f, k, 4, 1, BlockKind.Cube, PurpleCol, L, j);
                RBarAt(root, b, 0f, 0f, 5, 3, BlockKind.Cube, BlueCol, L, j);
                RUnitAt(root, b, 0f, 0, 6, 1, BlockKind.Cube, GoldCol, L, j);
            }
        }

        /// <summary>50 쌍둥이 원통 탑: 상판 둘에 원통 2열 두 겹 7단, 부재와 금색 원통.</summary>
        static void BuildN_TwinCylinderTowers(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks; independentPedestals = true;
            foreach (int side in new[] { -1, 1 })
            {
                var b = FrontPlate(root, p, side * 1.0f, 0f, 1f * DS + 0.1f);
                foreach (float j in new[] { -0.5f, 0.5f })
                {
                    RColAt(root, b, 0f, -0.5f, j, 7, BlockKind.Cylinder, side < 0 ? BlueCol : IceCol, L, true); RColAt(root, b, 0f, 0.5f, j, 7, BlockKind.Cylinder, side < 0 ? BlueCol : IceCol, L, true);
                    RBarAt(root, b, 0f, 0f, 7, 2, BlockKind.Cube, RedCol, L, j); RUnitAt(root, b, 0f, 0, 8, 1, BlockKind.Cylinder, GoldCol, L, j);
                }
            }
        }

        /// <summary>51 상자 성벽: 상자 7열 3단 두 겹, 판자 부재 줄, 성가퀴(짝수 열 상자 2단), 가운데 사탕.</summary>
        static void BuildN_CrateRampart(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 3.5f * DS + 0.15f);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                for (int k = -3; k <= 3; k++) RColAt(root, b, 0f, k, j, 3, BlockKind.Crate, CrateCol, L, true);
                RBarAt(root, b, 0f, -2f, 3, 3, BlockKind.Plank, WoodCol, L, j); RBarAt(root, b, 0f, 2f, 3, 3, BlockKind.Plank, WoodCol, L, j); RUnitAt(root, b, 0f, 0, 3, 1, BlockKind.Crate, CrateCol, L, j);
                foreach (int k in new[] { -3, -1, 1, 3 }) RCol(root, At(b, 0f, k, j, 4), 2, BlockKind.Crate, CrateCol, 0f, L, true);
                RUnitAt(root, b, 0f, 0, 4, 2, BlockKind.Candy, PinkCol, L, j);
            }
        }

        /// <summary>52 X자 벽: 7열 6단 큐브 벽 두 겹에 X자 대각선 색무늬, 위 부재.</summary>
        static void BuildN_XWall(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 3.5f * DS + 0.15f);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                for (int row = 0; row < 6; row++) for (int k = -3; k <= 3; k++)
                    { bool x = Mathf.Abs(k) == Mathf.Abs(row - 2.5f) - 0.5f || Mathf.Abs(k) == Mathf.Abs(row - 2.5f) + 0.5f; RUnitAt(root, b, 0f, k, row, 1, x ? BlockKind.Cube : BlockKind.Ice, x ? RedCol : IceCol, L, j); }
                RBarAt(root, b, 0f, -2f, 6, 3, BlockKind.Cube, BlueCol, L, j); RBarAt(root, b, 0f, 2f, 6, 3, BlockKind.Cube, BlueCol, L, j); RUnitAt(root, b, 0f, 0, 6, 1, BlockKind.Cube, GoldCol, L, j);
            }
        }

        /// <summary>53 통나무 원진: 둥근 상판 위 통나무 10개 링(4단), 안쪽 상자 링, 가운데 사탕 탑과 금색.</summary>
        static void BuildN_LogRing(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            Pedestal(root, Vector3.zero, 1.5f, p, false, 1, 0f, 3.0f); var b = Top(Vector3.zero);
            for (int i = 0; i < 10; i++)
            {
                float a = i * 36f * Mathf.Deg2Rad; Vector3 pos = b + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 1.15f;
                RCol(root, pos, 4, BlockKind.Log, WoodCol, 0f, L); RUnit(root, pos + Vector3.up * 4 * DU, 1, BlockKind.Crate, CrateCol, 0f, L);
            }
            for (int i = 0; i < 5; i++) { float a = (36f + 72f * i) * Mathf.Deg2Rad; RCol(root, b + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.6f, 3, BlockKind.Crate, CrateCol, 0f, L, true); }
            RCol(root, b, 6, BlockKind.Candy, PinkCol, 0f, L); RUnit(root, b + Vector3.up * 6 * DU, 1, BlockKind.Cube, GoldCol, 0f, L);
        }

        /// <summary>54 종탑: 3열 벽돌 탑 두 겹 8단, 5·6단이 열려 있고 그 안에 금색 원통(종), 꼭대기 부재·큐브.</summary>
        static void BuildN_BellTower(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 1.5f * DS + 0.1f);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                for (int row = 0; row < 8; row++)
                {
                    if (row == 5 || row == 6) { RUnitAt(root, b, 0f, -1, row, 1, BlockKind.Cube, SlateCol, L, j); RUnitAt(root, b, 0f, 1, row, 1, BlockKind.Cube, SlateCol, L, j); if (row == 5) RUnitAt(root, b, 0f, 0, row, 2, BlockKind.Cylinder, GoldCol, L, j); }
                    else if (row % 2 == 0) for (int k = -1; k <= 1; k++) RUnitAt(root, b, 0f, k, row, 1, BlockKind.Cube, BlueCol, L, j);
                    else { bool left = (row / 2) % 2 == 0; RBarAt(root, b, 0f, left ? -0.5f : 0.5f, row, 2, BlockKind.Cube, RedCol, L, j); RUnitAt(root, b, 0f, left ? 1 : -1, row, 1, BlockKind.Cube, BlueCol, L, j); }
                }
                RBarAt(root, b, 0f, 0f, 8, 3, BlockKind.Cube, RedCol, L, j); RUnitAt(root, b, 0f, 0, 9, 1, BlockKind.Cube, GoldCol, L, j);
            }
        }

        /// <summary>55 세 줄 벽: 깊은 상판 하나에 앞 3단·가운데 5단·뒤 7단 벽 세 줄이 계단처럼 서 있다 (5열).</summary>
        static void BuildN_ThreeRows(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 2.5f * DS + 0.15f, 1.55f);
            int[] hs = { 3, 5, 7 }; Color[] cs = { IceCol, BlueCol, PurpleCol };
            for (int d = 0; d < 3; d++)
            {
                float j = d - 1f;
                for (int k = -2; k <= 2; k++) RColAt(root, b, 0f, k, j, hs[d], d == 0 ? BlockKind.Ice : BlockKind.Cube, cs[d], L, true);
                RBarAt(root, b, 0f, -1f, hs[d], 3, BlockKind.Cube, RedCol, L, j); RBarAt(root, b, 0f, 1.5f, hs[d], 2, BlockKind.Cube, RedCol, L, j);
            }
        }

        /// <summary>56 볼록 성벽: 가운데 상판이 앞, 양옆 2열 상판이 ∓25°로 뒤로 꺾인 볼록한 성벽. 상자·큐브 두 겹.</summary>
        static void BuildN_ConvexWall(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var c = FrontPlate(root, p, 0f, -0.45f, 1.5f * DS + 0.1f);
            RBrickWall(root, c, 0f, 3, 5, 2, BlockKind.Cube, RedCol, BlueCol, L);
            foreach (float j in new[] { -0.5f, 0.5f }) { RBarAt(root, c, 0f, 0f, 5, 3, BlockKind.Cube, BlueCol, L, j); RUnitAt(root, c, 0f, 0, 6, 1, BlockKind.Cube, GoldCol, L, j); }
            foreach (int side in new[] { -1, 1 })
            {
                // 옆 상판은 2열(폭 1.12)로 좁혀 상판 간격 0.55를 두고도 전체 폭이 2.2 안에 들게 한다
                float yaw = -side * 25f; Vector3 cc = new Vector3(side * 1.4f, 0f, 0.5f);
                RPlate(root, p, cc, yaw, 1f * DS + 0.1f, 1.1f); var b = Top(cc);
                RBrickWall(root, b, yaw, 2, 5, 2, BlockKind.Crate, CrateCol, WoodCol, L);
                foreach (float j in new[] { -0.5f, 0.5f }) { RBarAt(root, b, yaw, 0f, 5, 2, BlockKind.Plank, WoodCol, L, j); RUnitAt(root, b, yaw, -side * 0.5f, 6, 1, BlockKind.Cube, RedCol, L, j); }
            }
        }

        /// <summary>57 쐐기 벽: 상판 둘이 앞 꼭짓점 뒤에서 ±35°로 벌어지는 쐐기. 얼음 벽돌 두 겹 6단, 꼭짓점 대리석 기둥 상판.</summary>
        static void BuildN_WedgeWall(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            Vector3 A = new Vector3(0f, 0f, -0.9f);
            var ap = FrontPlate(root, p, 0f, -0.9f, 0.3f, 0.6f);
            RCol(root, ap, 7, BlockKind.Stone, MarbleCol, 0f, L); RUnit(root, ap + Vector3.up * 7 * DU, 1, BlockKind.Cube, GoldCol, 0f, L);
            foreach (int side in new[] { -1, 1 })
            {
                float yaw = -side * 35f; Vector3 u = new Vector3(side * Mathf.Sin(35f * Mathf.Deg2Rad), 0f, Mathf.Cos(35f * Mathf.Deg2Rad));
                Vector3 c = A + u * 1.85f; RPlate(root, p, c, yaw, 1.5f * DS + 0.1f, 1.1f); var b = Top(c);
                RBrickWall(root, b, yaw, 3, 6, 2, BlockKind.Ice, IceCol, BlueCol, L);
                foreach (float j in new[] { -0.5f, 0.5f }) RBarAt(root, b, yaw, 0f, 6, 3, BlockKind.Cube, RedCol, L, j);
            }
        }

        /// <summary>58 T자 벽: 앞 가로 벽(7열 4단 두 겹)과 그 뒤 가운데에서 뒤로 뻗는 세로 벽(90°, 3열 6단).</summary>
        static void BuildN_TWall(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var f = FrontPlate(root, p, 0f, -0.55f, 3.5f * DS + 0.15f, 1.1f);
            RBrickWall(root, f, 0f, 7, 4, 2, BlockKind.Cube, PurpleCol, BlueCol, L);
            foreach (float j in new[] { -0.5f, 0.5f }) { RBarAt(root, f, 0f, -2f, 4, 3, BlockKind.Cube, RedCol, L, j); RBarAt(root, f, 0f, 2f, 4, 3, BlockKind.Cube, RedCol, L, j); }
            Vector3 bc = new Vector3(0f, 0f, 0.9f); RPlate(root, p, bc, 90f, 1.5f * DS + 0.1f, 1.1f); var b = Top(bc);
            RBrickWall(root, b, 90f, 3, 6, 2, BlockKind.Ice, IceCol, BlueCol, L);
            foreach (float j in new[] { -0.5f, 0.5f }) { RBarAt(root, b, 90f, 0f, 6, 3, BlockKind.Cube, RedCol, L, j); RUnitAt(root, b, 90f, 0, 7, 1, BlockKind.Cube, GoldCol, L, j); }
        }

        /// <summary>59 원통 벽: 원통 7열 5단 두 겹, 위 부재 세 개, 큐브 셋.</summary>
        static void BuildN_CylinderWall(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 3.5f * DS + 0.15f);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                for (int k = -3; k <= 3; k++) RColAt(root, b, 0f, k, j, 5, BlockKind.Cylinder, k % 2 == 0 ? BlueCol : PurpleCol, L, true);
                RBarAt(root, b, 0f, -2f, 5, 3, BlockKind.Cube, RedCol, L, j); RBarAt(root, b, 0f, 2f, 5, 3, BlockKind.Cube, RedCol, L, j); RUnitAt(root, b, 0f, 0, 5, 1, BlockKind.Cylinder, GoldCol, L, j);
                for (int k = -2; k <= 2; k += 2) RUnitAt(root, b, 0f, k, 6, 1, BlockKind.Cube, BlueCol, L, j);
            }
        }

        /// <summary>60 얼음 피라미드: 얼음 7·6·5·4·3·2·1 벽돌식 피라미드 두 겹, 가운데 줄은 사탕.</summary>
        static void BuildN_IcePyramid(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 3.5f * DS + 0.15f);
            foreach (float j in new[] { -0.5f, 0.5f })
                for (int row = 0; row < 7; row++)
                {
                    int n = 7 - row;
                    for (int i = 0; i < n; i++) { float k = i - (n - 1) * 0.5f; bool core = Mathf.Abs(k) < 0.6f && row < 5; RUnitAt(root, b, 0f, k, row, 1, core ? BlockKind.Candy : BlockKind.Ice, core ? PinkCol : IceCol, L, j); }
                }
        }

        /// <summary>61 상자 탑 셋: 상판 셋에 상자 2열 두 겹 6·8·6단, 판자 부재와 금색 큐브.</summary>
        static void BuildN_CrateTrio(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks; independentPedestals = true;
            int[] hs = { 6, 8, 6 };
            for (int i = 0; i < 3; i++)
            {
                var b = FrontPlate(root, p, (i - 1) * 1.2f, 0f, 0.5f * DS + 0.25f);
                foreach (float j in new[] { -0.5f, 0.5f })
                {
                    RColAt(root, b, 0f, -0.5f, j, hs[i], BlockKind.Crate, CrateCol, L, true); RColAt(root, b, 0f, 0.5f, j, hs[i], BlockKind.Crate, CrateCol, L, true);
                    RBarAt(root, b, 0f, 0f, hs[i], 2, BlockKind.Plank, WoodCol, L, j); RUnitAt(root, b, 0f, 0, hs[i] + 1, 1, BlockKind.Cube, GoldCol, L, j);
                }
            }
        }

        /// <summary>62 판자 격자: 상자 기둥 넷(k ±1·±3) 7단과 사이를 잇는 판자 부재가 줄마다 번갈아 격자를 이룬다. 두 겹.</summary>
        static void BuildN_PlankLattice(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 3.5f * DS + 0.15f);
            foreach (float j in new[] { -0.5f, 0.5f })
                for (int row = 0; row < 7; row++)
                {
                    if (row % 2 == 0) { foreach (float k in new[] { -3f, -1f, 1f, 3f }) RUnitAt(root, b, 0f, k, row, 1, BlockKind.Crate, CrateCol, L, j); }
                    else { RBarAt(root, b, 0f, -2f, row, 3, BlockKind.Plank, WoodCol, L, j); RBarAt(root, b, 0f, 2f, row, 3, BlockKind.Plank, WoodCol, L, j); }
                }
        }

        /// <summary>63 성벽과 망루: 앞 가로 벽(6열 3단 두 겹)과 오른쪽 뒤 상판의 높은 망루(2열 벽돌 8단).</summary>
        static void BuildN_WallAndWatchtower(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks; independentPedestals = true;
            var f = FrontPlate(root, p, -0.35f, -0.5f, 3f * DS + 0.15f);
            RBrickWall(root, f, 0f, 6, 3, 2, BlockKind.Crate, CrateCol, WoodCol, L);
            foreach (float j in new[] { -0.5f, 0.5f }) { RBarAt(root, f, 0f, -1.5f, 3, 3, BlockKind.Plank, WoodCol, L, j); RBarAt(root, f, 0f, 1.5f, 3, 3, BlockKind.Plank, WoodCol, L, j); for (int k = -2; k <= 2; k += 2) RUnitAt(root, f, 0f, k + 0.5f, 4, 1, BlockKind.Crate, CrateCol, L, j); }
            var t = FrontPlate(root, p, 1.35f, 0.95f, 1f * DS + 0.1f);
            RBrickWall(root, t, 0f, 2, 8, 2, BlockKind.Cube, SlateCol, RedCol, L);
            foreach (float j in new[] { -0.5f, 0.5f }) { RBarAt(root, t, 0f, 0f, 8, 2, BlockKind.Cube, RedCol, L, j); RUnitAt(root, t, 0f, 0, 9, 1, BlockKind.Cube, GoldCol, L, j); }
        }

        /// <summary>64 무지개 담: 7열 5단 큐브 두 겹, 열마다 색이 다르고 위에 부재.</summary>
        static void BuildN_RainbowFence(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 3.5f * DS + 0.15f);
            Color[] cs = { RedCol, OrangeCol, GoldCol, GreenCol, BlueCol, PurpleCol, PinkCol };
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                for (int k = -3; k <= 3; k++) RColAt(root, b, 0f, k, j, 5, BlockKind.Cube, cs[k + 3], L, true);
                RBarAt(root, b, 0f, -2f, 5, 3, BlockKind.Cube, IceCol, L, j); RBarAt(root, b, 0f, 2f, 5, 3, BlockKind.Cube, IceCol, L, j); RUnitAt(root, b, 0f, 0, 5, 1, BlockKind.Cube, IceCol, L, j);
            }
        }

        /// <summary>65 통나무 다리: 상자 탑 둘(2열 4단 두 겹) 위 판자 두 줄, 그 위 세워 둔 통나무 넷.</summary>
        static void BuildN_LogBridge(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            foreach (int side in new[] { -1, 1 }) { var b = FrontPlate(root, p, side * 1.1f, 0f, 1f * DS + 0.1f); RBrickWall(root, b, 0f, 2, 4, 2, BlockKind.Crate, CrateCol, WoodCol, L); }
            var c = Top(Vector3.zero);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                RBarAt(root, c, 0f, -1.5f, 4, 3, BlockKind.Plank, WoodCol, L, j); RBarAt(root, c, 0f, 1.5f, 4, 3, BlockKind.Plank, WoodCol, L, j);
                for (float k = -1.5f; k <= 1.5f; k += 1f) RUnitAt(root, c, 0f, k, 5, 2, BlockKind.Log, WoodCol, L, j);
                RBarAt(root, c, 0f, 0f, 7, 3, BlockKind.Plank, WoodCol, L, j); RUnitAt(root, c, 0f, 0, 8, 1, BlockKind.Cube, GoldCol, L, j);
            }
        }

        /// <summary>66 이중 링: 둥근 상판 위 바깥 큐브 링 12개(3단, 접선 방향), 안쪽 사탕 링 6개(4단), 가운데 대리석 탑.</summary>
        static void BuildN_DoubleRing(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            Pedestal(root, Vector3.zero, 1.5f, p, false, 1, 0f, 3.0f); var b = Top(Vector3.zero);
            for (int i = 0; i < 12; i++) { float a = i * 30f; float ar = a * Mathf.Deg2Rad; Vector3 pos = b + new Vector3(Mathf.Sin(ar), 0f, Mathf.Cos(ar)) * 1.15f; RCol(root, pos, 3, BlockKind.Cube, i % 2 == 0 ? RedCol : BlueCol, a, L, true); }
            for (int i = 0; i < 6; i++) { float a = (30f + 60f * i) * Mathf.Deg2Rad; RCol(root, b + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.62f, 4, BlockKind.Candy, PinkCol, 0f, L); }
            RCol(root, b, 6, BlockKind.Stone, MarbleCol, 0f, L); RUnit(root, b + Vector3.up * 6 * DU, 1, BlockKind.Cube, GoldCol, 0f, L);
        }

        /// <summary>67 지붕 집: 5열 벽돌 벽 4단 두 겹 위에 큐브 5·3·1 지붕, 가운데 사탕 굴뚝.</summary>
        static void BuildN_RoofHouse(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 2.5f * DS + 0.15f);
            RBrickWall(root, b, 0f, 5, 4, 2, BlockKind.Crate, CrateCol, WoodCol, L);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                for (int k = -2; k <= 2; k++) RUnitAt(root, b, 0f, k, 4, 1, BlockKind.Cube, RedCol, L, j);
                for (int k = -1; k <= 1; k++) RUnitAt(root, b, 0f, k, 5, 1, BlockKind.Cube, RedCol, L, j);
                RUnitAt(root, b, 0f, 0, 6, 1, BlockKind.Cube, RedCol, L, j);
                RUnitAt(root, b, 0f, 1.5f, 6, 1, BlockKind.Candy, PinkCol, L, j);
            }
        }

        /// <summary>68 팔각 성: 둥근 상판 위 큐브 기둥 8개(4단)가 팔각으로 서고 이웃끼리 2칸 부재로 엮인다(4·5단 번갈아). 가운데 원통 탑.</summary>
        static void BuildN_Octagon(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            Pedestal(root, Vector3.zero, 1.45f, p, false, 1, 0f, 2.9f); var b = Top(Vector3.zero);
            float r = 1.1f;
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f; float ar = a * Mathf.Deg2Rad;
                RCol(root, b + new Vector3(Mathf.Sin(ar), 0f, Mathf.Cos(ar)) * r, 4, BlockKind.Cube, i % 2 == 0 ? BlueCol : PurpleCol, a, L, true);
                // 이웃 기둥 사이 변의 부재: 2칸 부재(0.91)가 변(0.84)보다 길어 끝이 기둥 중심에서 만나므로, 짝수 변은 4단·홀수 변은 5단에 엮는다 (통나무집처럼)
                float am = a + 22.5f; float amr = am * Mathf.Deg2Rad; Vector3 m = b + new Vector3(Mathf.Sin(amr), 0f, Mathf.Cos(amr)) * (r * Mathf.Cos(22.5f * Mathf.Deg2Rad));
                RBar(root, m + Vector3.up * (i % 2 == 0 ? 4 : 5) * DU, 2, BlockKind.Cube, RedCol, am, L);
            }
            RCol(root, b, 6, BlockKind.Cylinder, BlueCol, 0f, L, true); RUnit(root, b + Vector3.up * 6 * DU, 1, BlockKind.Cylinder, GoldCol, 0f, L);
        }

        /// <summary>69 계단 탑: 4열 벽돌 탑 두 겹인데 열마다 8·7·6·5단으로 낮아진다.</summary>
        static void BuildN_StairTower(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 2f * DS + 0.1f);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                for (int i = 0; i < 4; i++) { float k = i - 1.5f; RColAt(root, b, 0f, k, j, 8 - i, BlockKind.Cube, i % 2 == 0 ? BlueCol : PurpleCol, L, true); RUnitAt(root, b, 0f, k, 8 - i, 1, BlockKind.Cube, RedCol, L, j); }
            }
        }

        /// <summary>70 창 셋 벽: 7열 벽 두 겹에 창 셋(2·3단, k −2·0·2)이 뚫리고 인방·성가퀴.</summary>
        static void BuildN_ThreeWindows(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 3.5f * DS + 0.15f);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                for (int k = -3; k <= 3; k++) RUnitAt(root, b, 0f, k, 0, 1, BlockKind.Cube, SlateCol, L, j);
                RBarAt(root, b, 0f, -2f, 1, 3, BlockKind.Cube, BlueCol, L, j); RBarAt(root, b, 0f, 2f, 1, 3, BlockKind.Cube, BlueCol, L, j); RUnitAt(root, b, 0f, 0, 1, 1, BlockKind.Cube, SlateCol, L, j);
                foreach (int k in new[] { -3, -1, 1, 3 }) RCol(root, At(b, 0f, k, j, 2), 2, BlockKind.Cube, BlueCol, 0f, L, true);
                RBarAt(root, b, 0f, -2f, 4, 3, BlockKind.Cube, RedCol, L, j); RBarAt(root, b, 0f, 2f, 4, 3, BlockKind.Cube, RedCol, L, j); RUnitAt(root, b, 0f, 0, 4, 1, BlockKind.Cube, SlateCol, L, j);
                for (int k = -3; k <= 3; k += 2) RUnitAt(root, b, 0f, k, 5, 1, BlockKind.Cube, BlueCol, L, j);
            }
        }

        /// <summary>71 원통 아치: 원통 2열 탑 둘(5단 두 겹) 위 부재, 그 위 원통 셋과 금색.</summary>
        static void BuildN_CylinderArch(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            foreach (int side in new[] { -1, 1 }) { var b = FrontPlate(root, p, side * 1.0f, 0f, 1f * DS + 0.1f); foreach (float j in new[] { -0.5f, 0.5f }) { RColAt(root, b, 0f, -0.5f, j, 5, BlockKind.Cylinder, BlueCol, L, true); RColAt(root, b, 0f, 0.5f, j, 5, BlockKind.Cylinder, IceCol, L, true); } }
            var c = Top(Vector3.zero);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                RBarAt(root, c, 0f, -1.5f, 5, 3, BlockKind.Cube, RedCol, L, j); RBarAt(root, c, 0f, 1.5f, 5, 3, BlockKind.Cube, RedCol, L, j);
                for (int k = -1; k <= 1; k++) RUnitAt(root, c, 0f, k, 6, 1, BlockKind.Cylinder, PurpleCol, L, j);
                RBarAt(root, c, 0f, 0f, 7, 3, BlockKind.Cube, RedCol, L, j); RUnitAt(root, c, 0f, 0, 8, 1, BlockKind.Cylinder, GoldCol, L, j);
            }
        }

        /// <summary>72 겹 피라미드: 앞겹은 파랑, 뒷겹은 빨강 피라미드(7단)가 반 칸 어긋나 겹쳐 보인다.</summary>
        static void BuildN_DoublePyramid(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 3.5f * DS + 0.2f);
            foreach (float j in new[] { -0.5f, 0.5f })
                for (int row = 0; row < 7; row++)
                {
                    int n = 7 - row; float shift = j < 0 ? -0.25f : 0.25f;
                    for (int i = 0; i < n; i++) RUnitAt(root, b, 0f, i - (n - 1) * 0.5f + shift, row, 1, BlockKind.Cube, j < 0 ? BlueCol : RedCol, L, j);
                }
        }

        /// <summary>73 다섯 기둥 홀: 대리석 기둥 다섯(5단) 두 겹, 위에 부재 두 줄과 큐브 지붕.</summary>
        static void BuildN_FiveColumnHall(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks;
            var b = FrontPlate(root, p, 0f, 0f, 3f * DS + 0.15f);
            foreach (float j in new[] { -0.5f, 0.5f })
            {
                for (float k = -2.5f; k <= 2.5f; k += 1.25f) RColAt(root, b, 0f, k, j, 5, BlockKind.Stone, MarbleCol, L);
                RBarAt(root, b, 0f, -1.5f, 5, 3, BlockKind.Cube, RedCol, L, j); RBarAt(root, b, 0f, 1.5f, 5, 3, BlockKind.Cube, RedCol, L, j);
                RBarAt(root, b, 0f, 0f, 6, 3, BlockKind.Cube, RedCol, L, j); RUnitAt(root, b, 0f, -2.5f, 6, 1, BlockKind.Cube, BlueCol, L, j); RUnitAt(root, b, 0f, 2.5f, 6, 1, BlockKind.Cube, BlueCol, L, j);
                for (int k = -1; k <= 1; k++) RUnitAt(root, b, 0f, k, 7, 1, BlockKind.Cube, BlueCol, L, j);
                RUnitAt(root, b, 0f, 0, 8, 1, BlockKind.Cube, GoldCol, L, j);
            }
        }

        /// <summary>74 쌍둥이 얼음 탑: ∓20°로 살짝 안쪽을 향한 상판 둘에 얼음 벽돌 탑(2열 8단 두 겹)과 사탕 꼭대기.</summary>
        static void BuildN_TwinIceTowers(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks; independentPedestals = true;
            foreach (int side in new[] { -1, 1 })
            {
                float yaw = side * 20f; Vector3 c = new Vector3(side * 1.05f, 0f, 0f);
                RPlate(root, p, c, yaw, 1f * DS + 0.1f, 1.1f); var b = Top(c);
                RBrickWall(root, b, yaw, 2, 8, 2, BlockKind.Ice, IceCol, BlueCol, L);
                foreach (float j in new[] { -0.5f, 0.5f }) { RBarAt(root, b, yaw, 0f, 8, 2, BlockKind.Cube, RedCol, L, j); RUnitAt(root, b, yaw, 0, 9, 1, BlockKind.Candy, PinkCol, L, j); }
            }
        }

        /// <summary>75 성채: 가운데 상판의 높은 본탑(3열 벽돌 8단 두 겹)과 양옆 작은 상판의 기둥(1열 5단 두 겹), 빨강 꼭대기.</summary>
        static void BuildN_Citadel(Transform root, System.Random rng, Palette p, LevelInfo info)
        {
            Begin(info); var L = info.blocks; independentPedestals = true;
            var c = FrontPlate(root, p, 0f, 0.2f, 1.5f * DS + 0.1f);
            RBrickWall(root, c, 0f, 3, 8, 2, BlockKind.Cube, SlateCol, RedCol, L);
            foreach (float j in new[] { -0.5f, 0.5f }) { RBarAt(root, c, 0f, 0f, 8, 3, BlockKind.Cube, RedCol, L, j); RUnitAt(root, c, 0f, 0, 9, 1, BlockKind.Cube, GoldCol, L, j); }
            foreach (int side in new[] { -1, 1 })
            {
                // 옆 상판은 1열(폭 0.6)로 좁혀 상판 간격 0.55를 두고도 전체 폭 ±1.9 안에 든다
                var b = FrontPlate(root, p, side * 1.62f, -0.3f, 0.3f);
                foreach (float j in new[] { -0.5f, 0.5f }) { RColAt(root, b, 0f, 0, j, 5, BlockKind.Cube, side < 0 ? BlueCol : PurpleCol, L, true); RUnitAt(root, b, 0f, 0, 5, 1, BlockKind.Cube, RedCol, L, j); }
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
