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

        static readonly System.Collections.Generic.List<Ball> alive = new();

        public const float BallPhysicsMass = 0.05f;
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
            // 공의 물리 질량은 아주 작게: 블록에 주는 충격은 전부 아래 OnCollisionEnter에서 스탯 기반으로 직접 넣는다.
            // (질량이 크면 PhysX 자체 충돌 임펄스가 스탯과 무관하게 블록을 밀어 버리고, 공이 블록을 뚫고 지나가며 뒷블록까지 밀었다)
            rb.mass = BallPhysicsMass;
            rb.useGravity = true; // 중력 적용. 조준은 Cannon에서 포물선 보정
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearVelocity = dir.normalized * Speed;

            var b = go.AddComponent<Ball>();
            // 공끼리는 충돌하지 않는다 (연사 시 앞 공에 튕겨 조준이 틀어지는 것 방지)
            var col = go.GetComponent<Collider>();
            for (int i = alive.Count - 1; i >= 0; i--)
            {
                if (alive[i] == null) { alive.RemoveAt(i); continue; }
                var other = alive[i].GetComponent<Collider>();
                if (other != null) Physics.IgnoreCollision(col, other, true);
            }
            alive.Add(b);
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

        void OnDestroy() { alive.Remove(this); }

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
                Vector3 J = (dir + Vector3.up * 0.08f).normalized * impulse * falloff * mult;
                // 대부분은 질량중심에 밀어 넣어 "뒤로 밀리는" 힘이 되게 하고, 일부만 맞은 지점에 줘서 회전감을 남긴다
                brb.WakeUp();
                brb.AddForce(J * 0.75f, ForceMode.Impulse);
                brb.AddForceAtPosition(J * 0.25f, point, ForceMode.Impulse);
            }

            onHit?.Invoke(perfect, point);
            // 공은 사라지지 않고 튕겨 나와 떨어진다. 가벼운 공이라 이후 충돌은 블록을 거의 밀지 않는다.
            // 날아온 방향의 반대로 살짝 튕겨 나오며 위로 떠오른다 (접촉 법선은 상황에 따라 방향이 뒤집혀 신뢰하지 않는다)
            Vector3 bounce = -dir * 4f;
            bounce.y = Mathf.Abs(bounce.y) + 2f;
            rb.linearVelocity = bounce;
        }
    }
}
