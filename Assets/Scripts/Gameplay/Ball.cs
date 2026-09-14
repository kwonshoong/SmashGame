using UnityEngine;

namespace SmashGame
{
    /// <summary>
    /// 대포에서 발사되는 공. 첫 충돌에서 충격량을 전달하고 사라진다(레퍼런스와 동일).
    /// 파괴력·크기·무게 스탯이 여기서 물리에 반영된다.
    /// </summary>
    public class Ball : MonoBehaviour
    {
        public BallStats stats;
        public LevelController controller;
        public System.Action<bool, Vector3> onHit; // (perfect, point)

        Rigidbody rb;
        Vector3 lastVelocity;
        bool consumed;
        float spawnTime;

        public const float Lifetime = 2f; // 발사 후 공이 사라지기까지의 시간(충돌 여부와 무관)
        public static float Speed => Balance.BallSpeed;
        static float BaseImpulse => Balance.BallImpulse;

        public static Ball Spawn(Vector3 from, Vector3 dir, BallStats stats, LevelController controller)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Ball";
            float radius = 0.22f * stats.size;
            go.transform.position = from;
            go.transform.localScale = Vector3.one * radius * 2f;
            go.GetComponent<Renderer>().material = Materials.Get(BallColor(stats.star), true);

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.5f * stats.mass;
            rb.useGravity = true; // 중력 적용. 조준은 Cannon에서 포물선 보정
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearVelocity = dir.normalized * Speed;

            var b = go.AddComponent<Ball>();
            b.stats = stats;
            b.controller = controller;
            b.rb = rb;
            b.spawnTime = Time.time;
            Destroy(go, Lifetime);
            return b;
        }

        public static Color BallColor(int star) => star switch
        {
            1 => new Color(0.95f, 0.3f, 0.3f),
            2 => new Color(0.95f, 0.55f, 0.2f),
            3 => new Color(0.4f, 0.75f, 0.95f),
            4 => new Color(0.75f, 0.45f, 0.95f),
            _ => new Color(1f, 0.85f, 0.2f),
        };

        void FixedUpdate()
        {
            if (rb != null && !consumed) lastVelocity = rb.linearVelocity;
        }

        void Update()
        {
            if (transform.position.y < -3f) Destroy(gameObject);
        }

        void OnCollisionEnter(Collision c)
        {
            if (consumed) return;
            var block = c.collider.GetComponentInParent<Block>();
#if UNITY_EDITOR
            Debug.Log($"[Ball] hit {c.collider.name} block={(block != null ? block.kind.ToString() : "-")} at {c.GetContact(0).point:F2}");
#endif
            if (block == null)
            {
                // 장애물·바닥에 맞음: 이후는 일반 물리 공
                consumed = true;
                return;
            }

            consumed = true;
            Vector3 dir = lastVelocity.sqrMagnitude > 0.01f ? lastVelocity.normalized : transform.forward;
            Vector3 point = c.GetContact(0).point;
            float impulse = BaseImpulse * stats.power * Mathf.Sqrt(stats.mass);
            float radius = 0.6f * stats.size;
            int dmg = Mathf.Max(1, Mathf.CeilToInt(stats.power - 0.01f));

            // 직접 맞은 블록
            bool perfect = block.crown;
            block.Hit(dmg, dir, stats.power);

            // 반경 안의 블록에 충격 (크기 스탯이 클수록 인접 블록도 밀림)
            var hits = Physics.OverlapSphere(point, radius);
            foreach (var h in hits)
            {
                var b = h.GetComponentInParent<Block>();
                if (b == null) continue;
                var brb = b.GetComponent<Rigidbody>();
                if (brb == null || brb.isKinematic) continue;
                if (b.IsReinforced && b != block) continue; // 강화 블록은 직접 맞혀야만 반응
                float dist = Vector3.Distance(point, h.ClosestPoint(point));
                float falloff = Mathf.Clamp01(1f - dist / radius);
                if (b == block) { if (b.IsReinforced) continue; falloff = 1f; }
                float mult = perfect && b == block ? 1.5f : 1f;
                brb.AddForceAtPosition((dir + Vector3.up * 0.08f).normalized * impulse * falloff * mult, point, ForceMode.Impulse);
            }

            onHit?.Invoke(perfect, point);
            // 공은 사라지지 않고 물리 공으로 남아 튕기거나 굴러 떨어진다 (이후 충돌은 일반 물리로만 작용)
            rb.linearVelocity *= 0.35f;
        }
    }
}
