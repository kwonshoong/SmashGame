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

        public static readonly Vector3 DefaultPos = new Vector3(0f, 1.5f, -6.2f); // 받침대(2.0)보다 약간 낮은 높이 — 낮게 쏘면 받침대에 맞는다

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

        void BuildVisual()
        {
            // 받침(수레)
            var baseGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseGo.name = "Base";
            DestroyImmediate(baseGo.GetComponent<Collider>());
            baseGo.transform.SetParent(transform, false);
            baseGo.transform.localPosition = new Vector3(0, -0.35f, 0);
            baseGo.transform.localScale = new Vector3(1.4f, 0.5f, 1.2f);
            baseGo.GetComponent<Renderer>().material = Materials.Get(new Color(0.55f, 0.2f, 0.6f));
            RoundedMesh.Apply(baseGo, 0.07f);

            var wheelL = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            DestroyImmediate(wheelL.GetComponent<Collider>());
            wheelL.transform.SetParent(transform, false);
            wheelL.transform.localPosition = new Vector3(-0.85f, -0.45f, 0);
            wheelL.transform.localRotation = Quaternion.Euler(0, 0, 90);
            wheelL.transform.localScale = new Vector3(0.7f, 0.12f, 0.7f);
            wheelL.GetComponent<Renderer>().material = Materials.Get(new Color(0.2f, 0.45f, 0.9f));
            RoundedMesh.Apply(wheelL, 0.03f);
            var wheelR = Instantiate(wheelL, transform);
            wheelR.transform.localPosition = new Vector3(0.85f, -0.45f, 0);

            // 포신(회전 축)
            barrel = new GameObject("Barrel").transform;
            barrel.SetParent(transform, false);
            barrel.localPosition = new Vector3(0, 0.1f, 0);

            var tube = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tube.name = "Tube";
            DestroyImmediate(tube.GetComponent<Collider>());
            tube.transform.SetParent(barrel, false);
            tube.transform.localPosition = new Vector3(0, 0, 0.7f);
            tube.transform.localRotation = Quaternion.Euler(90, 0, 0);
            tube.transform.localScale = new Vector3(0.6f, 0.75f, 0.6f);
            tube.GetComponent<Renderer>().material = Materials.Get(new Color(0.8f, 0.15f, 0.2f), true);
            RoundedMesh.Apply(tube, 0.06f);

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(barrel, false);
            ring.transform.localPosition = new Vector3(0, 0, 1.3f);
            ring.transform.localRotation = Quaternion.Euler(90, 0, 0);
            ring.transform.localScale = new Vector3(0.7f, 0.08f, 0.7f);
            ring.GetComponent<Renderer>().material = Materials.Get(new Color(1f, 0.8f, 0.2f), false, true);
            RoundedMesh.Apply(ring, 0.025f);

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

            Vector3 target;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, 100f)) target = hit.point;
            else
            {
                // 아무것도 안 맞으면 구조물 평면(z=0)과의 교점
                float t = (0f - ray.origin.z) / Mathf.Max(0.001f, ray.direction.z);
                target = ray.origin + ray.direction * Mathf.Max(1f, t);
            }
            Fire(target);
        }

        public void Fire(Vector3 target)
        {
            cooldown = 0.18f;
            recoil = 1f;
            // 포신 회전축(pivot)에서 목표까지, 중력을 고려한 포물선 발사각으로 조준 (탭한 지점을 정확히 지나간다)
            Vector3 dir = BallisticDirection(barrel.position, target, Ball.Speed);
            barrel.rotation = Quaternion.LookRotation(dir, Vector3.up);
            var ball = Ball.Spawn(muzzle.position, dir, stats, controller);
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
