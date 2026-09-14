using UnityEngine;

namespace SmashGame
{
    public enum BlockKind { Cube, Cylinder, Candy, Ice, Crate, Log, Plank, Stone, Crown }

    /// <summary>
    /// 받침대 위의 블록 하나. 받침대 아래로 떨어지면 "제거"로 카운트된다.
    /// 강화 블록(hp>1)은 hp가 1이 될 때까지 고정(kinematic)되어 있다가 풀린다.
    /// </summary>
    public class Block : MonoBehaviour
    {
        public BlockKind kind;
        public int hp = 1;
        public bool crown;
        public bool sticky;
        public bool tall;   // 긴 변형(세로 2배). 텍스처 타일링·질량에 반영
        public LevelController controller;
        public float fallY = 1.0f;

        public const float ReinforcedMassMult = 6f;
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

        public void MakeCrown()
        {
            crown = true;
            kind = BlockKind.Crown;
            baseColor = new Color(1f, 0.82f, 0.15f);
            rend.material = Materials.GetBlock(BlockKind.Crown, baseColor, tall);
            // 왕관 마커: 위에 작은 금색 구
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.SetParent(transform, false);
            marker.transform.localPosition = new Vector3(0, 0.5f, -0.51f);
            // 부모 스케일이 비균등(긴 블록·원통)이어도 구슬이 찌그러지지 않게 보정
            var ls = transform.localScale;
            const float markerWorld = 0.18f; // 월드 지름 고정 (판자처럼 납작·넓은 블록에서도 같은 크기)
            marker.transform.localScale = new Vector3(markerWorld / ls.x, markerWorld / ls.y, markerWorld / ls.z);
            marker.GetComponent<Renderer>().material = Materials.Get(new Color(1f, 0.95f, 0.5f), false, true);
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

            bool shatter = kind == BlockKind.Ice || (kind == BlockKind.Candy && impactPower >= 1.2f);
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
                        bounciness = 0.05f,
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
            else if (kind == BlockKind.Candy || kind == BlockKind.Crown)
            {
                // 사탕·왕관: 아주 약한 자체 발광으로 채도를 살린다 (블룸 없이도 "빛나는" 느낌)
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
                d.layer = LayerMask.NameToLayer("Ignore Raycast");
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
