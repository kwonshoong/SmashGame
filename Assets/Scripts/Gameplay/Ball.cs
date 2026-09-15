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
        public System.Action<Vector3> onHit; // (point)

        Rigidbody rb;
        Vector3 lastVelocity;
        bool consumed;
        float spawnTime;

        static readonly System.Collections.Generic.List<Ball> alive = new();

        public const float BallPhysicsMass = 0.05f;   // PhysX 상의 질량(블록에 주는 힘은 스탯 임펄스로 따로 계산하므로 작게)
        public const float ReboundMassBase = 0.35f;    // 되튕김 계산용 "실제" 공 질량 (× 무게 스탯). 블록 질량과 비교되어 튕길지 밀고 나갈지 결정
        public const float Restitution = 0.35f;        // 반발계수 (0 = 완전 비탄성, 1 = 완전 탄성)
        public const float TangentKeep = 0.75f;        // 접선 속도 보존 비율 (마찰로 일부 손실)
        public const float Lifetime = 2f;
        public const float PedestalBounceKeep = 0.97f;   // 받침대에 튈 때 유지되는 속도 비율 (감소량 3%)
        static PhysicsMaterial ballPhysics; // 발사 후 공이 사라지기까지의 시간(충돌 여부와 무관)
        public static float Speed => Balance.BallSpeed;
        static float BaseImpulse => Balance.BallImpulse;

        public static Ball Spawn(Vector3 from, Vector3 dir, BallStats stats, LevelController controller)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Ball";
            { int bl = LayerMask.NameToLayer("Ball"); if (bl >= 0) go.layer = bl; }   // 파편과 충돌하지 않는 레이어
            float radius = 0.22f * stats.size;
            go.transform.position = from;
            go.transform.localScale = Vector3.one * radius * 2f;
            go.GetComponent<Renderer>().material = Materials.Get(BallColor(stats.star), true);

            var rb = go.AddComponent<Rigidbody>();
            // 공의 물리 질량은 아주 작게: 블록에 주는 충격은 전부 아래 OnCollisionEnter에서 스탯 기반으로 직접 넣는다.
            // (질량이 크면 PhysX 자체 충돌 임펄스가 스탯과 무관하게 블록을 밀어 버리고, 공이 블록을 뚫고 지나가며 뒷블록까지 밀었다)
            rb.mass = BallPhysicsMass;
            if (ballPhysics == null)
                ballPhysics = new PhysicsMaterial("Ball") { bounciness = Restitution, dynamicFriction = 0.4f, staticFriction = 0.4f,
                    bounceCombine = PhysicsMaterialCombine.Maximum, frictionCombine = PhysicsMaterialCombine.Average };
            go.GetComponent<Collider>().material = ballPhysics; // 첫 타격 이후의 충돌(받침대·바닥·다른 블록)은 PhysX가 같은 반발계수로 처리
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

        /// <summary>
        /// 공(질량 mB, 속도 v)이 블록(질량 mK, 속도 u)의 면(법선 n, 공 쪽 방향)에 부딪힌 뒤의 공 속도.
        /// 법선 성분은 반발계수 e의 충돌식, 접선 성분은 마찰로 일부만 남긴다. 블록 쪽 반작용은 스탯 임펄스가 대신한다.
        /// </summary>
        public static Vector3 ReboundVelocity(Vector3 v, Vector3 u, Vector3 n, float mB, float mK)
        {
            Vector3 vrel = v - u;
            float vn = Vector3.Dot(vrel, n);            // 접근 중이면 음수
            if (vn > 0f) return v;                        // 이미 멀어지는 중
            Vector3 vt = vrel - vn * n;
            float jn = -(1f + Restitution) * vn / (1f / mB + 1f / mK);   // 법선 임펄스 크기
            float vnAfter = vn + jn / mB;                 // 양수면 되튕김, 음수면 밀고 나감
            return u + vt * TangentKeep + n * vnAfter;
        }

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
                if (LevelBuilder.PedestalColliders.Contains(c.collider))
                {
                    // 받침대 상판·기둥에 먼저 닿은 공: 소모하지 않고 거의 그대로 튕겨 계속 날아간다.
                    // 카메라가 위에서 보므로 공은 살짝 내려오며 날아오는데, 상판이 블록보다 1.5 앞까지 나와 있어 블록 아래쪽을 겨냥하면
                    // 상판 앞 테두리에 먼저 닿는다(실측: 0.36 블록의 아래 절반이 안 맞았다). 물리 재질에 맡기면 마찰·반발로 속도가
                    // 크게 죽으므로 접촉면 기준으로 직접 반사시켜 PedestalBounceKeep 만큼만 유지한다. 반사된 공은 같은 기울기로
                    // 살짝 떠오르며 블록 아랫부분을 그대로 때린다.
                    Vector3 v = lastVelocity.sqrMagnitude > 0.01f ? lastVelocity : rb.linearVelocity;
                    float r = transform.localScale.x * 0.5f;
                    float topY = c.collider.bounds.max.y;
                    if (transform.position.y >= topY - r * 0.6f)
                    {
                        // 상판 윗면이나 앞 모서리를 스침: 살짝 떠서 넘어간다(수직 성분만 뒤집고 상판 위로 올려 둔다).
                        // 모서리에 정직하게 반사시키면 공이 카메라 쪽으로 되돌아와 낮은 블록을 영영 못 맞힌다.
                        var pos = transform.position; pos.y = Mathf.Max(pos.y, topY + r + 0.01f); transform.position = pos; rb.position = pos;
                        rb.linearVelocity = new Vector3(v.x, Mathf.Max(Mathf.Abs(v.y), 0.3f), v.z) * PedestalBounceKeep;
                    }
                    else
                    {
                        // 상판 옆면·기둥을 정면으로 맞힘: 접촉면 기준으로 반사 (되돌아온다)
                        rb.linearVelocity = Vector3.Reflect(v, c.GetContact(0).normal) * PedestalBounceKeep;
                    }
                    lastVelocity = rb.linearVelocity;
                    return;
                }
                // 장애물·바닥에 맞음: 이후는 일반 물리 공
                consumed = true;
                return;
            }

            consumed = true;
            Vector3 dir = lastVelocity.sqrMagnitude > 0.01f ? lastVelocity.normalized : transform.forward;
            Vector3 point = c.GetContact(0).point;
            float impulse = BaseImpulse * stats.power * Mathf.Sqrt(stats.mass);
            float radius = 0.4f * stats.size;   // 튐 반경: 크기 스탯 1에서는 직접 맞은 블록 위주, 이웃은 약하게 (이웃까지 같이 밀리면 한 덩어리처럼 보인다)
            int dmg = Mathf.Max(1, Mathf.CeilToInt(stats.power - 0.01f));

            // 되튕김 계산에 쓸 값은 블록이 부서지기(Hit) 전에 읽어 둔다
            var directRb = block.GetComponent<Rigidbody>();
            float blockMass = directRb == null || directRb.isKinematic ? 1e6f : directRb.mass;
            Vector3 blockVel = directRb != null ? directRb.linearVelocity : Vector3.zero;
            Vector3 normal = c.GetContact(0).normal;
            if (Vector3.Dot(normal, lastVelocity) > 0f) normal = -normal;   // 항상 공 쪽을 향하게

            // 직접 맞은 블록 (짧은 간격으로 같은 블록을 다시 맞히면 콤보로 더 세게 민다)
            float combo = block.RegisterHitCombo();
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
                falloff *= falloff;   // 거리에 따라 급하게 줄어들게 (이웃은 접촉을 통해서만 밀리는 게 자연스럽다)
                if (b == block) { if (b.IsReinforced) continue; falloff = 1f; }
                float mult = b == block ? combo : 1f;
                Vector3 J = (dir + Vector3.up * 0.08f).normalized * impulse * falloff * mult;
                b.Push(J, point);   // 몇 물리 스텝에 나눠 밀어 이웃 사슬까지 같이 밀리게 (Block.Push 참고)
            }

            onHit?.Invoke(point);
            // 공은 사라지지 않고 튕겨 나와 떨어진다. 가벼운 공이라 이후 충돌은 블록을 거의 밀지 않는다.
            // 공의 되튕김: 맞은 면의 법선·양쪽 질량·반발계수로 1차원 충돌식을 풀어 실제와 비슷하게.
            // 가벼운 블록(사탕)이면 밀고 나가고, 무거운 블록(돌·격파 탑)이면 되튕기고, 원통 옆면을 비스듬히 치면 법선 방향으로 꺾여 나간다.
            rb.linearVelocity = ReboundVelocity(lastVelocity, blockVel, normal, ReboundMassBase * stats.mass, blockMass);
            PlayLog.Hit(block, point, dir, impulse, combo, lastVelocity, rb.linearVelocity);
        }
    }
}
