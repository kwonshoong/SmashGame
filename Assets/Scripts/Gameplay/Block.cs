using UnityEngine;

namespace SmashGame
{
    public enum BlockKind { Cube, Cylinder, Candy, Ice, Crate, Log, Plank, Stone }

    /// <summary>
    /// 받침대 위의 블록 하나. 받침대 아래로 떨어지면 "제거"로 카운트된다.
    /// 강화 블록(hp>1)은 hp가 1이 될 때까지 고정(kinematic)되어 있다가 풀린다.
    /// </summary>
    public class Block : MonoBehaviour
    {
        public BlockKind kind;
        public int hp = 1;
        /// <summary>얼음이라도 맞았을 때 깨지지 않고 밀리기만 한다 (젠가 부재처럼 구조를 받치는 얼음 판)</summary>
        public bool noShatter;
        public bool sticky;
        public bool tall;   // 긴 변형(세로 2배). 텍스처 타일링·질량에 반영
        /// <summary>눕힌 통나무처럼 굴러갈 수 있는 원통. 가만히 있을 땐 구름 저항(높은 각감쇠)으로 미세 떨림에 저절로 굴러 내리는 걸 막고, 맞아서 움직이면 자유롭게 구른다.</summary>
        public bool rollingLog;
        public void SetRollingLog() { rollingLog = true; var r = GetComponent<Rigidbody>(); if (r != null) r.angularDamping = RollRestDamping; }   // 빌드 직후 정착(PreSettle)부터 구름 저항이 걸리게
        public const float RollRestDamping = 12f, RollFreeDamping = 0.05f;
        public LevelController controller;
        public float fallY = 1.0f;

        public const float ReinforcedMassMult = 2f;   // 6이면 강화 블록(최대 79kg)이 닿아 있는 일반 블록을 마찰로 눌러 구조물 전체가 붙은 듯 굳는다(플레이 로그로 확인)
        public bool IsReinforced => hp > 1;
        float baseMass = 1f;

        Rigidbody rb;
        Renderer rend;
        Color baseColor;
        bool removed;
        bool everHit;

        public bool Removed => removed;

        public void Setup(BlockKind k, Color color, float mass, int hitPoints)
        {
            kind = k;
            hp = hitPoints;
            rend = GetComponent<Renderer>();
            baseColor = color;
            rend.material = Materials.GetBlock(k, color, tall);
            rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.2f;
            rb.sleepThreshold = 0.05f;
            var col = GetComponent<Collider>();
            if (col != null) col.material = Materials.BlockPhysics;
            // 강화 블록: 고정(kinematic)하면 받침이 사라져도 공중에 떠 있으므로, 대신 무겁게 만들고 공의 충격만 무시한다.
            baseMass = mass;
            rb.isKinematic = false;
            if (hp > 1) { rb.mass = mass * ReinforcedMassMult; ApplyCrackTint(); }
        }

        void ApplyCrackTint()
        {
            // hp 3: 진한 톤, hp 2: 금 간 톤(밝게), hp 1: 원래 색
            float t = hp >= 3 ? 0.55f : (hp == 2 ? 0.75f : 1f);
            rend.material = Materials.GetBlock(kind, baseColor * t, tall);
        }

        /// <summary>공에 맞았을 때. dmg는 공 파괴력에서 계산된 정수.</summary>
        public void Hit(int dmg, Vector3 dir, float impactPower)
        {
            if (removed) return;
            everHit = true;
            if (hp > 1)
            {
                hp = Mathf.Max(1, hp - dmg);
                ApplyCrackTint();
                rb.WakeUp();
                if (hp <= 1) rb.mass = baseMass;   // 강화 해제: 이제 밀린다
                else return;                        // 아직 강화 상태(충격 무시)
            }

            bool shatter = !noShatter && (kind == BlockKind.Ice || (kind == BlockKind.Candy && impactPower >= 1.2f));
            if (shatter)
            {
                Debris.Spawn(transform.position, baseColor, 8, transform.localScale.magnitude * 0.25f);
                MarkRemoved();
                Destroy(gameObject);
            }
        }

        void Update()
        {
            if (!removed && transform.position.y < fallY)
            {
                MarkRemoved();
                Debris.Spawn(transform.position, baseColor, 4, transform.localScale.magnitude * 0.2f);
                Destroy(gameObject, 1.5f);
            }
        }

        void MarkRemoved()
        {
            if (removed) return;
            removed = true;
            if (controller != null) controller.OnBlockRemoved(this);
            WakeNeighbors();
        }

        /// <summary>잠들어 있던 이웃 블록을 깨워 받침이 사라진 뒤 공중에 남지 않게 한다</summary>
        void WakeNeighbors()
        {
            var hits = Physics.OverlapSphere(transform.position, 1.2f);
            foreach (var h in hits)
            {
                var b = h.GetComponentInParent<Block>();
                if (b == null || b == this) continue;
                var r = b.GetComponent<Rigidbody>();
                if (r != null && !r.isKinematic) r.WakeUp();
            }
        }

        /// <summary>구조물 생성 직후 호출: 정지 상태로 잠재워 공에 맞기 전까지 미동도 하지 않게 한다</summary>
        public void SettleAndSleep()
        {
            if (rb == null || rb.isKinematic) return;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.Sleep();
        }

        // ---------- 밀기(임펄스)를 몇 물리 스텝에 나눠 적용 + 연속 타격 콤보 ----------
        // 한 번에 큰 임펄스를 주면 이웃과 깊게 겹쳤다가 침투 복원으로 되튕겨 "밀었던 힘이 사라진" 것처럼 보인다.
        // 몇 스텝에 나눠 밀면 이웃 사슬이 같이 밀리고, 같은 블록을 짧은 간격으로 다시 맞히면 콤보로 더 세게 민다.
        public const int   PushSpreadSteps = 5;     // 90Hz 기준 약 55ms
        public const float ComboWindow = 0.9f;      // 이 시간 안에 다시 맞으면 콤보 유지
        public const float ComboStep = 0.35f;       // 콤보당 +35%, 최대 4콤보(×2.4)
        Vector3 pushJ, pushPoint; int pushSteps;
        float lastHitTime = -10f; int combo;

        /// <summary>직접 맞은 블록의 콤보 배율. 호출할 때마다 타격으로 기록된다.</summary>
        public float RegisterHitCombo()
        {
            combo = Time.time - lastHitTime < ComboWindow ? Mathf.Min(combo + 1, 4) : 0;
            lastHitTime = Time.time;
            return 1f + ComboStep * combo;
        }

        /// <summary>임펄스를 예약한다. 이후 FixedUpdate에서 몇 스텝에 걸쳐 나눠 적용.</summary>
        public void Push(Vector3 impulse, Vector3 point)
        {
            if (rb == null || removed) return;
            rb.WakeUp();
            pushJ += impulse;
            pushPoint = point;
            pushSteps = PushSpreadSteps;
        }

        void FixedUpdate()
        {
            if (rollingLog && rb != null && !removed)
            {
                // 구름 저항 흉내: 거의 정지해 있으면(선속도 1.5·각속도 2 미만 — 승강/회전 받침대에 실려 가는 속도 0.5~1.3은 정지로 본다)
                // 각감쇠를 크게 — 실제 통나무도 마찰로 제자리에 머문다. 공에 맞거나 떨어지며 빨라지면 감쇠를 풀어 자연스럽게 굴러간다.
                bool resting = rb.linearVelocity.sqrMagnitude < 2.25f && rb.angularVelocity.sqrMagnitude < 4f;
                float want = resting ? RollRestDamping : RollFreeDamping;
                if (rb.angularDamping != want) rb.angularDamping = want;
            }
            if (pushSteps <= 0 || rb == null || removed) return;
            Vector3 j = pushJ / pushSteps;
            rb.AddForce(j * 0.9f, ForceMode.Impulse);                  // 대부분은 질량중심으로: 뒤로 미는 힘
            rb.AddForceAtPosition(j * 0.1f, pushPoint, ForceMode.Impulse); // 접점 비율이 크면 긴 블록이 미끄러지는 대신 들썩이며 에너지를 잃는다
            pushJ -= j;
            pushSteps--;
        }

        public bool WasHit => everHit;
        public Color BaseColor => baseColor;

        /// <summary>색만 바꿔 다시 입힌다 (접착 블록 표시 등)</summary>
        public void Retint(Color c)
        {
            baseColor = c;
            rend.material = Materials.GetBlock(kind, c, tall);
        }
    }

    /// <summary>색상별 머티리얼 캐시. Standard(빌트인) 우선, 없으면 URP Lit.</summary>
    public static class Materials
    {
        static readonly System.Collections.Generic.Dictionary<int, Material> cache = new();
        static Shader shader;
        static PhysicsMaterial blockPhysics;

        /// <summary>블록 공통 물리 재질: 마찰을 낮춰 밀리면 미끄러져 떨어지게</summary>
        public static PhysicsMaterial BlockPhysics
        {
            get
            {
                if (blockPhysics == null)
                {
                    blockPhysics = new PhysicsMaterial("Block")
                    {
                        dynamicFriction = Balance.BlockFriction,
                        staticFriction = Balance.BlockStaticFriction,   // 정지 마찰은 높게: 가만히 있을 땐 미끄러지지 않음
                        bounciness = 0f,
                        frictionCombine = PhysicsMaterialCombine.Minimum,
                        bounceCombine = PhysicsMaterialCombine.Minimum,
                    };
                }
                return blockPhysics;
            }
        }

        static Shader GetShader()
        {
            if (shader != null) return shader;
            // URP가 켜져 있으면 URP Lit, 아니면 빌트인 Standard
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");
            return shader;
        }

        static readonly System.Collections.Generic.Dictionary<long, Material> blockCache = new();

        /// <summary>긴 블록에서 텍스처를 세로로 2번 반복할 소재 (상자는 상자 2개가 쌓인 듯, 얼음·통나무는 결이 늘어나지 않게). 나머지는 늘려 쓴다.</summary>
        static bool TilesVertically(BlockKind kind) => kind == BlockKind.Crate || kind == BlockKind.Ice || kind == BlockKind.Log;

        /// <summary>소재 텍스처 + 색이 입혀진 블록 머티리얼. tall이면 긴 변형용(세로 타일링).</summary>
        public static Material GetBlock(BlockKind kind, Color c, bool tall = false)
        {
            bool tile = tall && TilesVertically(kind);
            long key = ((long)kind << 32) | ((long)Mathf.RoundToInt(c.r * 255) << 16) | ((long)Mathf.RoundToInt(c.g * 255) << 8) | (long)Mathf.RoundToInt(c.b * 255) | (tile ? 1L << 40 : 0L);
            if (blockCache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(GetShader());
            var tex = BlockTextures.Get(kind, c);
            m.mainTexture = tex;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.color = Color.white;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            var (smooth, metal) = BlockTextures.Surface(kind);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            if (tile)
            {
                m.mainTextureScale = new Vector2(1f, 2f);
                if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", new Vector2(1f, 2f));
            }
            if (kind == BlockKind.Ice) MakeIceLook(m, c);
            else if (kind == BlockKind.Candy)
            {
                // 사탕: 아주 약한 자체 발광으로 채도를 살린다 (블룸 없이도 "빛나는" 느낌)
                if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 0.08f); }
            }
            blockCache[key] = m;
            return m;
        }

        /// <summary>얼음: 반투명 + 은은한 푸른 발광. Standard 셰이더의 Fade 모드를 코드로 설정한다.</summary>
        static void MakeIceLook(Material m, Color tint)
        {
            var col = new Color(1f, 1f, 1f, 0.8f);
            m.color = col;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (m.HasProperty("_Mode"))
            {
                m.SetFloat("_Mode", 2f); // Fade
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 1);   // 겹친 얼음끼리 정렬 깨짐 방지: 깊이는 쓴다
                m.DisableKeyword("_ALPHATEST_ON"); m.EnableKeyword("_ALPHABLEND_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            else if (m.HasProperty("_Surface"))
            {
                m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_AlphaClip", 0f);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (m.HasProperty("_SrcBlendAlpha")) { m.SetInt("_SrcBlendAlpha", 1); m.SetInt("_DstBlendAlpha", 10); }
                m.SetInt("_ZWrite", 1);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(0.35f, 0.6f, 0.9f) * 0.12f); }
        }

        public static Material Get(Color c, bool glossy = false, bool metallic = false)
        {
            int key = (Mathf.RoundToInt(c.r * 255) << 16) | (Mathf.RoundToInt(c.g * 255) << 8) | Mathf.RoundToInt(c.b * 255)
                      | (glossy ? 1 << 24 : 0) | (metallic ? 1 << 25 : 0);
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(GetShader());
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", glossy ? 0.6f : 0.3f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", glossy ? 0.6f : 0.3f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic ? 0.45f : 0f);
            cache[key] = m;
            return m;
        }
    }

    /// <summary>파편 연출. 작은 큐브를 흩뿌리고 1.5초 뒤 제거.</summary>
    public static class Debris
    {
        public static void Spawn(Vector3 pos, Color color, int count, float size)
        {
            size = Mathf.Clamp(size, 0.08f, 0.3f);
            for (int i = 0; i < count; i++)
            {
                var d = GameObject.CreatePrimitive(PrimitiveType.Cube);
                d.name = "Debris";
                d.transform.position = pos + Random.insideUnitSphere * 0.3f;
                d.transform.localScale = Vector3.one * size * Random.Range(0.6f, 1.2f);
                d.transform.rotation = Random.rotation;
                d.GetComponent<Renderer>().material = Materials.Get(color);
                int dl = LayerMask.NameToLayer("Debris");
                d.layer = dl >= 0 ? dl : LayerMask.NameToLayer("Ignore Raycast");   // 공·다른 파편과 충돌 안 함 (GameManager.Awake 레이어 설정)
                var rb = d.AddComponent<Rigidbody>();
                rb.mass = 0.05f;
                rb.linearVelocity = Random.insideUnitSphere * 5f + Vector3.up * 3f;
                rb.angularVelocity = Random.insideUnitSphere * 10f;
                var col = d.GetComponent<Collider>();
                col.isTrigger = false;
                Object.Destroy(d, Random.Range(1.0f, 1.8f));
            }
        }
    }
}
