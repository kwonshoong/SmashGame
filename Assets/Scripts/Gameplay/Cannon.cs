using UnityEngine;
using UnityEngine.EventSystems;

namespace SmashGame
{
    /// <summary>
    /// 화면 하단 고정 대포. 탭한 지점을 향해 공을 쏜다. 조준선 없음(레퍼런스와 동일).
    /// </summary>
    public class Cannon : MonoBehaviour
    {
        public LevelController controller;   // null이면 로비/대장간 시험 발사 모드
        public BallStats stats;
        public Camera cam;
        public bool inputEnabled = true;

        Transform barrel;
        Transform muzzle;
        Transform previewBall;
        float cooldown;
        float recoil;

        public static readonly Vector3 DefaultPos = new Vector3(0f, 1.15f, -7.6f); // 카메라 눈높이(3.7)를 낮춘 만큼 대포도 낮춰 화면 아래에 반쯤만 보이게

        public static Cannon Create(Transform parent, Camera cam, BallStats stats, LevelController controller)
        {
            var root = new GameObject("Cannon");
            root.transform.SetParent(parent);
            root.transform.position = DefaultPos;
            var c = root.AddComponent<Cannon>();
            c.cam = cam;
            c.stats = stats;
            c.controller = controller;
            c.BuildVisual();
            return c;
        }

        // ---------- 비주얼 (코드 조립: 나무 수레 + 바퀴살 바퀴 + 테이퍼 포신 + 금속 띠) ----------

        static readonly Color WoodDark = new Color(0.45f, 0.28f, 0.16f);
        static readonly Color WoodLight = new Color(0.62f, 0.42f, 0.24f);
        static readonly Color IronRed = new Color(0.78f, 0.14f, 0.18f);
        static readonly Color Brass = new Color(1f, 0.78f, 0.25f);
        static readonly Color WheelBlue = new Color(0.2f, 0.42f, 0.88f);

        GameObject Part(PrimitiveType prim, Transform parent, string name, Vector3 pos, Vector3 scale, Quaternion rot, Material mat, float bevel)
        {
            var go = GameObject.CreatePrimitive(prim);
            go.name = name;
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().material = mat;
            if (bevel > 0f) RoundedMesh.Apply(go, bevel);
            return go;
        }

        void BuildVisual()
        {
            var plank = Materials.GetBlock(BlockKind.Plank, WoodLight);
            var plankDark = Materials.GetBlock(BlockKind.Plank, WoodDark);
            var iron = Materials.Get(IronRed, true);
            var brass = Materials.Get(Brass, true, true);
            var blue = Materials.Get(WheelBlue, true);
            var darkMetal = Materials.Get(new Color(0.25f, 0.25f, 0.3f), false, true);

            // 수레: 바닥 판 + 양옆 볼(cheek) 판 + 가로 보강대
            var carriage = new GameObject("Carriage").transform;
            carriage.SetParent(transform, false);
            Part(PrimitiveType.Cube, carriage, "Bed", new Vector3(0, -0.5f, -0.05f), new Vector3(1.0f, 0.14f, 1.5f), Quaternion.identity, plank, 0.03f);
            for (int side = -1; side <= 1; side += 2)
            {
                // 뒤가 높고 앞이 낮은 볼: 두 조각(뒤 두꺼운 블록 + 앞 낮은 블록)으로 경사 표현
                Part(PrimitiveType.Cube, carriage, "Cheek", new Vector3(side * 0.42f, -0.2f, -0.35f), new Vector3(0.14f, 0.6f, 0.8f), Quaternion.identity, plankDark, 0.03f);
                Part(PrimitiveType.Cube, carriage, "CheekFront", new Vector3(side * 0.42f, -0.36f, 0.35f), new Vector3(0.14f, 0.28f, 0.7f), Quaternion.identity, plankDark, 0.03f);
                // 금속 보강 띠
                Part(PrimitiveType.Cube, carriage, "Band", new Vector3(side * 0.42f, -0.2f, -0.35f), new Vector3(0.16f, 0.06f, 0.82f), Quaternion.identity, darkMetal, 0.01f);
            }
            Part(PrimitiveType.Cube, carriage, "CrossBar", new Vector3(0, -0.28f, -0.7f), new Vector3(1.0f, 0.12f, 0.12f), Quaternion.identity, plankDark, 0.02f);
            // 차축
            Part(PrimitiveType.Cylinder, carriage, "Axle", new Vector3(0, -0.45f, 0.05f), new Vector3(0.1f, 0.95f, 0.1f), Quaternion.Euler(0, 0, 90), darkMetal, 0.02f);

            // 바퀴: 림 + 허브 + 바퀴살 6개
            for (int side = -1; side <= 1; side += 2)
            {
                var wheel = new GameObject("Wheel").transform;
                wheel.SetParent(transform, false);
                wheel.localPosition = new Vector3(side * 0.92f, -0.45f, 0.05f);
                wheel.localRotation = Quaternion.Euler(0, 0, 90);
                Part(PrimitiveType.Cylinder, wheel, "Rim", Vector3.zero, new Vector3(0.78f, 0.07f, 0.78f), Quaternion.identity, blue, 0.03f);
                Part(PrimitiveType.Cylinder, wheel, "RimInner", Vector3.zero, new Vector3(0.66f, 0.075f, 0.66f), Quaternion.identity, Materials.Get(WoodLight), 0.02f);
                Part(PrimitiveType.Cylinder, wheel, "Hub", Vector3.zero, new Vector3(0.22f, 0.09f, 0.22f), Quaternion.identity, brass, 0.02f);
                for (int k = 0; k < 6; k++)
                {
                    float a = k * 60f;
                    var q = Quaternion.Euler(0, a, 0);
                    Part(PrimitiveType.Cube, wheel, "Spoke", q * new Vector3(0.17f, 0, 0), new Vector3(0.34f, 0.05f, 0.06f), q, Materials.Get(WoodDark), 0.01f);
                }
            }

            // 포신(회전 축) — 위치·길이는 조준/발사 계산과 맞물려 있으므로 유지
            barrel = new GameObject("Barrel").transform;
            barrel.SetParent(transform, false);
            barrel.localPosition = new Vector3(0, 0.1f, 0);

            // 본체: 뒤(약실)가 굵고 앞이 가는 테이퍼 느낌 — 세 구간으로
            Part(PrimitiveType.Cylinder, barrel, "Breech", new Vector3(0, 0, 0.15f), new Vector3(0.66f, 0.35f, 0.66f), Quaternion.Euler(90, 0, 0), iron, 0.08f);
            Part(PrimitiveType.Cylinder, barrel, "Tube", new Vector3(0, 0, 0.75f), new Vector3(0.58f, 0.45f, 0.58f), Quaternion.Euler(90, 0, 0), iron, 0.05f);
            Part(PrimitiveType.Cylinder, barrel, "Neck", new Vector3(0, 0, 1.3f), new Vector3(0.52f, 0.2f, 0.52f), Quaternion.Euler(90, 0, 0), iron, 0.04f);
            // 포구 링(나팔) + 띠 2개 + 뒤쪽 둥근 꼬리
            Part(PrimitiveType.Cylinder, barrel, "MuzzleRing", new Vector3(0, 0, 1.47f), new Vector3(0.68f, 0.07f, 0.68f), Quaternion.Euler(90, 0, 0), brass, 0.025f);
            Part(PrimitiveType.Cylinder, barrel, "Band1", new Vector3(0, 0, 0.5f), new Vector3(0.64f, 0.045f, 0.64f), Quaternion.Euler(90, 0, 0), brass, 0.015f);
            Part(PrimitiveType.Cylinder, barrel, "Band2", new Vector3(0, 0, 1.05f), new Vector3(0.6f, 0.045f, 0.6f), Quaternion.Euler(90, 0, 0), brass, 0.015f);
            Part(PrimitiveType.Sphere, barrel, "Cascabel", new Vector3(0, 0, -0.28f), Vector3.one * 0.3f, Quaternion.identity, brass, 0f);
            Part(PrimitiveType.Sphere, barrel, "BreechCap", new Vector3(0, 0, -0.05f), Vector3.one * 0.62f, Quaternion.identity, iron, 0f);
            // 포이(회전축 핀) 좌우
            for (int side = -1; side <= 1; side += 2)
                Part(PrimitiveType.Cylinder, barrel, "Trunnion", new Vector3(side * 0.38f, 0, 0.15f), new Vector3(0.16f, 0.1f, 0.16f), Quaternion.Euler(0, 0, 90), darkMetal, 0.02f);
            // 포구 안쪽(검은 구멍)
            Part(PrimitiveType.Cylinder, barrel, "Bore", new Vector3(0, 0, 1.5f), new Vector3(0.4f, 0.02f, 0.4f), Quaternion.Euler(90, 0, 0), Materials.Get(new Color(0.08f, 0.06f, 0.08f)), 0f);

            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(barrel, false);
            muzzle.localPosition = new Vector3(0, 0, 1.55f);

            // 장전된 공 미리보기(스탯에 따라 크기·색이 바뀜)
            var pv = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pv.name = "PreviewBall";
            DestroyImmediate(pv.GetComponent<Collider>());
            pv.transform.SetParent(barrel, false);
            previewBall = pv.transform;
            RefreshPreview();
        }

        public void SetStats(BallStats s) { stats = s; RefreshPreview(); }

        void RefreshPreview()
        {
            if (previewBall == null) return;
            previewBall.localPosition = new Vector3(0, 0, 1.45f);
            previewBall.localScale = Vector3.one * 0.44f * stats.size;
            previewBall.GetComponent<Renderer>().material = Materials.Get(Ball.BallColor(stats.star), true);
        }

        void Update()
        {
            cooldown -= Time.unscaledDeltaTime;
            recoil = Mathf.MoveTowards(recoil, 0f, Time.unscaledDeltaTime * 3f);
            if (barrel != null) barrel.localPosition = new Vector3(0, 0.1f, -recoil * 0.25f);

            if (!inputEnabled || cam == null) return;
            if (!Input.GetMouseButtonDown(0)) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (controller != null && !controller.CanFire) return;
            if (cooldown > 0f) return;

            Fire(AimPoint(cam.ScreenPointToRay(Input.mousePosition)));
        }

        /// <summary>구조물 앞면 근처(z ≤ AimPlaneZ + 여유)로 간주할 깊이. 그 뒤는 블록 틈으로 보이는 땅·배경이므로 무시한다.</summary>
        public const float AimPlaneZ = 0f;   // 사거리와 무관하게 단거리 테이블 기준 고정: 중·장거리는 같은 탭이면 같은 포물선, 멀리 있는 블록은 더 위를 겨냥해야 맞는다
        const float AimDepthTolerance = 1.2f;

        /// <summary>
        /// 탭 위치 → 조준점. 블록·받침대처럼 구조물 근처의 것을 맞히면 그 점을, 그렇지 않으면(블록 틈 사이로 보이는 뒤쪽 땅, 하늘 등)
        /// 구조물 정면 평면(z = AimPlaneZ)과 시선의 교점을 쓴다. 그래서 틈을 겨냥해도 대포가 땅으로 처박히지 않고 그 높이로 날아간다.
        /// </summary>
        public static Vector3 AimPoint(Ray ray)
        {
            // 시선과 구조물 정면 평면의 교점
            Vector3 plane;
            if (Mathf.Abs(ray.direction.z) > 0.001f)
            {
                float t = (AimPlaneZ - ray.origin.z) / ray.direction.z;
                plane = ray.origin + ray.direction * Mathf.Max(1f, t);
            }
            else plane = ray.origin + ray.direction * 10f;

            int debrisLayer = LayerMask.NameToLayer("Debris");
            int mask = debrisLayer >= 0 ? ~(1 << debrisLayer) : Physics.DefaultRaycastLayers;   // 파편은 조준 대상 아님
            var hits = Physics.RaycastAll(ray, 100f, mask & ~(1 << 2));
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider.isTrigger) continue;
                if (h.collider.GetComponentInParent<Ball>() != null) continue;      // 날아가는 공은 무시
                if (h.point.z > AimPlaneZ + AimDepthTolerance) break;                // 구조물 뒤(틈 사이로 보이는 땅·배경)
                return h.point;                                                       // 블록·받침대·장애물·앞쪽 땅
            }
            return plane;
        }

        public void Fire(Vector3 target)
        {
            cooldown = 0.18f;
            recoil = 1f;
            // 포신 회전축(pivot)에서 목표까지, 중력을 고려한 포물선 발사각으로 조준 (탭한 지점을 정확히 지나간다)
            Vector3 dir = BallisticDirection(barrel.position, target, Ball.Speed);
            barrel.rotation = Quaternion.LookRotation(dir, Vector3.up);
            // 공은 회전축이 아니라 포구에서 출발한다. 회전축 기준 해로 쏘면 포구까지 1.5 정도 앞선 만큼 포물선이 덜 꺾여
            // 목표를 2~3° 위로 지나간다(멀수록 0.5까지 벗어남). 포구 위치에서 다시 풀어 보정한다(두 번이면 충분).
            for (int i = 0; i < 2; i++)
            {
                dir = BallisticDirection(muzzle.position, target, Ball.Speed);
                barrel.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
            var ball = Ball.Spawn(muzzle.position, dir, stats, controller);
            PlayLog.Shot(muzzle.position, target, stats);
            if (controller != null)
            {
                ball.onHit = controller.OnBallHit;
                controller.OnFired();
            }
            MuzzleFlash();
        }

        public void FireAt(Vector3 target) => Fire(target);

        /// <summary>속도 speed로 from에서 to를 지나가는 낮은 포물선의 발사 방향. 닿을 수 없으면 직선 방향.</summary>
        public static Vector3 BallisticDirection(Vector3 from, Vector3 to, float speed)
        {
            Vector3 delta = to - from;
            Vector3 flat = new Vector3(delta.x, 0f, delta.z);
            float d = flat.magnitude;
            float h = delta.y;
            float g = -Physics.gravity.y;
            if (d < 0.01f) return delta.normalized;
            float v2 = speed * speed;
            float disc = v2 * v2 - g * (g * d * d + 2f * h * v2);
            if (disc < 0f) return delta.normalized;
            float tanTheta = (v2 - Mathf.Sqrt(disc)) / (g * d); // 낮은 각
            float theta = Mathf.Atan(tanTheta);
            return (flat.normalized * Mathf.Cos(theta) + Vector3.up * Mathf.Sin(theta)).normalized;
        }

        void MuzzleFlash()
        {
            var f = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            DestroyImmediate(f.GetComponent<Collider>());
            f.transform.position = muzzle.position;
            f.transform.localScale = Vector3.one * 0.6f;
            f.GetComponent<Renderer>().material = Materials.Get(new Color(1f, 0.85f, 0.3f), true);
            Destroy(f, 0.08f);
        }
    }
}
