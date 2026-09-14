using System.Collections.Generic;
using UnityEngine;

namespace SmashGame
{
    /// <summary>
    /// 테마별 배경 그림을 코드로 그린다(외부 이미지 없음).
    /// 멀리 세운 하늘 그림판(그라데이션 + 구름 + 언덕 실루엣 + 햇빛), 질감 있는 바닥, 양옆의 나무·바위 장식.
    /// 전부 콜라이더가 없어 게임플레이에 영향을 주지 않는다.
    /// </summary>
    public static class BackgroundArt
    {
        const int SkyW = 1024, SkyH = 1024, GroundSize = 512;
        static readonly Dictionary<Theme, Texture2D> skyCache = new();
        static readonly Dictionary<Theme, Texture2D> groundCache = new();
        static readonly Dictionary<Texture2D, Material> unlitCache = new();
        static readonly Dictionary<Texture2D, Material> litCache = new();

        struct Look
        {
            public Color skyTop, skyBottom, cloud, hillFar, hillNear, sun, ground, groundDetail, foliage, trunk;
        }

        static Look GetLook(Theme t) => t switch
        {
            Theme.Winter => new Look
            {
                skyTop = new Color(0.45f, 0.62f, 0.95f), skyBottom = new Color(0.86f, 0.93f, 1f), cloud = new Color(1f, 1f, 1f),
                hillFar = new Color(0.78f, 0.86f, 0.98f), hillNear = new Color(0.9f, 0.95f, 1f), sun = new Color(1f, 0.98f, 0.9f),
                ground = new Color(0.9f, 0.95f, 1f), groundDetail = new Color(0.78f, 0.86f, 0.98f), foliage = new Color(0.25f, 0.55f, 0.45f), trunk = new Color(0.45f, 0.3f, 0.2f),
            },
            Theme.Desert => new Look
            {
                skyTop = new Color(0.3f, 0.6f, 0.98f), skyBottom = new Color(1f, 0.88f, 0.7f), cloud = new Color(1f, 0.97f, 0.9f),
                hillFar = new Color(0.9f, 0.7f, 0.5f), hillNear = new Color(0.98f, 0.8f, 0.55f), sun = new Color(1f, 0.95f, 0.75f),
                ground = new Color(0.95f, 0.78f, 0.5f), groundDetail = new Color(0.85f, 0.66f, 0.4f), foliage = new Color(0.35f, 0.6f, 0.3f), trunk = new Color(0.5f, 0.35f, 0.2f),
            },
            _ => new Look
            {
                skyTop = new Color(0.32f, 0.6f, 0.98f), skyBottom = new Color(0.78f, 0.9f, 1f), cloud = new Color(1f, 1f, 1f),
                hillFar = new Color(0.55f, 0.78f, 0.55f), hillNear = new Color(0.45f, 0.75f, 0.4f), sun = new Color(1f, 0.97f, 0.85f),
                ground = new Color(0.48f, 0.8f, 0.34f), groundDetail = new Color(0.36f, 0.68f, 0.28f), foliage = new Color(0.3f, 0.65f, 0.3f), trunk = new Color(0.5f, 0.33f, 0.2f),
            },
        };

        // ------------------------------------------------------------ 씬 조립

        public static void Build(Transform root, Theme theme)
        {
            var look = GetLook(theme);

            // 하늘 그림판: 카메라 정면 멀리, 화면을 가득 채우는 크기
            var sky = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sky.name = "SkyPainting";
            Object.DestroyImmediate(sky.GetComponent<Collider>());
            sky.transform.SetParent(root);
            // 바닥 끝(z=70, y=-1.5)이 텍스처의 v≈0.2(가까운 언덕 능선)에 오도록 배치 → 화면에는 v 0.2~0.77 구간이 보인다
            sky.transform.position = new Vector3(0f, 28.5f, 70f);
            sky.transform.localScale = new Vector3(140f, 100f, 1f);
            sky.GetComponent<Renderer>().material = Unlit(SkyTexture(theme));
            sky.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 바닥: 질감 텍스처 타일
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(root);
            ground.transform.position = new Vector3(0, -1.5f, 10f);
            ground.transform.localScale = new Vector3(10f, 1f, 12f);
            var gr = ground.GetComponent<Renderer>();
            gr.material = Lit(GroundTexture(theme), 0.15f);
            gr.material.mainTextureScale = new Vector2(10f, 12f);
            if (gr.material.HasProperty("_BaseMap")) gr.material.SetTextureScale("_BaseMap", new Vector2(10f, 12f));

            // 양옆 장식: 나무(초원·겨울)/선인장·바위(사막)
            var rng = new System.Random((int)theme * 101 + 7);
            for (int i = 0; i < 7; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float x = side * (5.5f + (float)rng.NextDouble() * 4.5f);
                float z = 6f + i * 3.2f + (float)rng.NextDouble() * 2f;
                float s = 0.8f + (float)rng.NextDouble() * 0.7f;
                if (theme == Theme.Desert) { if (i % 3 == 0) Rock(root, new Vector3(x, -1.5f, z), s, look); else Cactus(root, new Vector3(x, -1.5f, z), s, look); }
                else Tree(root, new Vector3(x, -1.5f, z), s, look, theme == Theme.Winter);
            }
            // 받침대 주변 작은 덤불/돌
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f + 0.3f;
                var pos = new Vector3(Mathf.Cos(a) * 3.2f, -1.5f, 2f + Mathf.Sin(a) * 2.2f);
                if (pos.z < -1f) continue;
                Bush(root, pos, 0.35f + (float)rng.NextDouble() * 0.25f, look, theme);
            }
        }

        static GameObject Prim(PrimitiveType t, Transform root, Vector3 pos, Vector3 scale, Material m, float bevel = 0f)
        {
            var go = GameObject.CreatePrimitive(t);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().material = m;
            if (bevel > 0f) RoundedMesh.Apply(go, bevel);
            return go;
        }

        static void Tree(Transform root, Vector3 basePos, float s, Look look, bool snowy)
        {
            var trunk = Materials.GetBlock(BlockKind.Log, look.trunk);
            Prim(PrimitiveType.Cylinder, root, basePos + new Vector3(0, 0.6f * s, 0), new Vector3(0.35f * s, 0.6f * s, 0.35f * s), trunk, 0.03f).name = "TreeTrunk";
            var leaf = Materials.Get(look.foliage);
            var snow = Materials.Get(new Color(0.97f, 0.98f, 1f));
            // 둥근 잎 뭉치 3단
            for (int k = 0; k < 3; k++)
            {
                float r = (1.3f - k * 0.3f) * s;
                float y = (1.3f + k * 0.75f) * s;
                Prim(PrimitiveType.Sphere, root, basePos + new Vector3(0, y, 0), new Vector3(r, r * 0.85f, r), leaf).name = "TreeLeaf";
                if (snowy) Prim(PrimitiveType.Sphere, root, basePos + new Vector3(0, y + r * 0.28f, 0), new Vector3(r * 0.8f, r * 0.35f, r * 0.8f), snow).name = "TreeSnow";
            }
        }

        static void Cactus(Transform root, Vector3 basePos, float s, Look look)
        {
            var m = Materials.Get(look.foliage);
            Prim(PrimitiveType.Cylinder, root, basePos + new Vector3(0, 1.1f * s, 0), new Vector3(0.5f * s, 1.1f * s, 0.5f * s), m, 0.12f).name = "Cactus";
            Prim(PrimitiveType.Cylinder, root, basePos + new Vector3(0.55f * s, 1.3f * s, 0), new Vector3(0.32f * s, 0.55f * s, 0.32f * s), m, 0.1f).name = "CactusArm";
            Prim(PrimitiveType.Cylinder, root, basePos + new Vector3(-0.5f * s, 1.0f * s, 0), new Vector3(0.3f * s, 0.45f * s, 0.3f * s), m, 0.1f).name = "CactusArm";
        }

        static void Rock(Transform root, Vector3 basePos, float s, Look look)
        {
            var m = Materials.GetBlock(BlockKind.Stone, new Color(0.75f, 0.62f, 0.5f));
            Prim(PrimitiveType.Cube, root, basePos + new Vector3(0, 0.45f * s, 0), new Vector3(1.4f * s, 0.9f * s, 1.1f * s), m, 0.2f).name = "Rock";
            Prim(PrimitiveType.Cube, root, basePos + new Vector3(0.6f * s, 0.25f * s, 0.4f * s), new Vector3(0.7f * s, 0.5f * s, 0.6f * s), m, 0.15f).name = "Rock";
        }

        static void Bush(Transform root, Vector3 basePos, float s, Look look, Theme theme)
        {
            var m = theme == Theme.Desert ? Materials.GetBlock(BlockKind.Stone, new Color(0.8f, 0.68f, 0.55f)) : Materials.Get(look.foliage * 0.95f);
            Prim(PrimitiveType.Sphere, root, basePos + new Vector3(0, s * 0.6f, 0), new Vector3(s * 1.6f, s, s * 1.4f), m).name = "Bush";
            if (theme == Theme.Winter) Prim(PrimitiveType.Sphere, root, basePos + new Vector3(0, s * 0.85f, 0), new Vector3(s * 1.3f, s * 0.5f, s * 1.1f), Materials.Get(new Color(0.97f, 0.98f, 1f))).name = "BushSnow";
        }

        // ------------------------------------------------------------ 머티리얼

        static Material Unlit(Texture2D tex)
        {
            if (unlitCache.TryGetValue(tex, out var m) && m != null) return m;
            Shader sh = null;
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Texture");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            m = new Material(sh);
            m.mainTexture = tex;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            unlitCache[tex] = m;
            return m;
        }

        static Material Lit(Texture2D tex, float smooth)
        {
            if (litCache.TryGetValue(tex, out var m) && m != null) return m;
            m = new Material(Materials.Get(Color.white));
            m.mainTexture = tex;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            litCache[tex] = m;
            return m;
        }

        // ------------------------------------------------------------ 텍스처

        /// <summary>GLSL식 smoothstep(edge0, edge1, x). Mathf.SmoothStep은 "a~b 사이를 t로 보간"이라 의미가 다르다.</summary>
        static float SStep(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        static float Noise(float x, float y, float scale, int seed) => Mathf.PerlinNoise(x * scale + seed * 17.3f, y * scale + seed * 9.1f);
        static float Fbm(float x, float y, float scale, int seed)
            => 0.5f * Noise(x, y, scale, seed) + 0.28f * Noise(x, y, scale * 2f, seed + 1) + 0.14f * Noise(x, y, scale * 4f, seed + 2) + 0.08f * Noise(x, y, scale * 8f, seed + 3);

        /// <summary>하늘 그림: 위→아래 그라데이션, 햇빛, 뭉게구름 2층, 언덕 실루엣 2층(아래 35%)</summary>
        public static Texture2D SkyTexture(Theme theme)
        {
            if (skyCache.TryGetValue(theme, out var t) && t != null) return t;
            var look = GetLook(theme);
            t = new Texture2D(SkyW, SkyH, TextureFormat.RGBA32, true) { name = "Sky_" + theme, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[SkyW * SkyH];
            Vector2 sunPos = new Vector2(0.74f, 0.68f);
            for (int y = 0; y < SkyH; y++)
            {
                float v = y / (float)(SkyH - 1);
                for (int x = 0; x < SkyW; x++)
                {
                    float u = x / (float)(SkyW - 1);
                    // 하늘 그라데이션 (아래가 밝음)
                    Color c = Color.Lerp(look.skyBottom, look.skyTop, Mathf.Pow(Mathf.Clamp01((v - 0.24f) / 0.5f), 0.85f));
                    // 햇빛 글로우
                    float sd = Vector2.Distance(new Vector2(u, v), sunPos);
                    c = Color.Lerp(c, look.sun, Mathf.Exp(-sd * sd * 60f) * 0.9f + Mathf.Exp(-sd * 6f) * 0.25f);
                    // 구름: 두 층, 가장자리는 부드럽게, 아랫면은 살짝 그늘
                    float band1 = Mathf.Exp(-Mathf.Pow((v - 0.62f) / 0.1f, 2f));
                    float band2 = Mathf.Exp(-Mathf.Pow((v - 0.44f) / 0.07f, 2f));
                    float n1 = Fbm(u * 2.2f, v * 3f, 3f, 5);
                    float n2 = Fbm(u * 3f + 0.5f, v * 4f, 4f, 8);
                    float cloud = SStep(0.5f, 0.62f, n1 * band1 + 0.05f) * 0.95f + SStep(0.55f, 0.66f, n2 * band2 + 0.05f) * 0.75f;
                    float shade = 1f - 0.12f * SStep(0.4f, 0.9f, Fbm(u * 2.2f, v * 3f + 0.02f, 3f, 5) - n1 + 0.5f);
                    c = Color.Lerp(c, look.cloud * shade, Mathf.Clamp01(cloud));
                    // 언덕: 먼 층(연함) + 가까운 층(진함)
                    float hFar = 0.30f + 0.07f * Mathf.Sin(u * 7f + 1f) + 0.05f * Noise(u, 0.3f, 4f, 21);
                    float hNear = 0.20f + 0.06f * Mathf.Sin(u * 5f + 3f) + 0.05f * Noise(u, 0.7f, 6f, 22);
                    if (v < hFar) c = Color.Lerp(look.hillFar, look.skyBottom, 0.15f);
                    if (v < hFar && v > hFar - 0.012f) c = Color.Lerp(c, Color.white, 0.25f); // 능선 하이라이트
                    if (v < hNear) c = look.hillNear * (0.92f + 0.08f * Noise(u, v, 30f, 23));
                    if (v < hNear && v > hNear - 0.012f) c = Color.Lerp(c, Color.white, 0.2f);
                    px[y * SkyW + x] = c;
                }
            }
            t.SetPixels(px); t.Apply();
            skyCache[theme] = t;
            return t;
        }

        /// <summary>바닥 타일: 초원=풀 얼룩, 겨울=눈(반짝임), 사막=모래 물결</summary>
        public static Texture2D GroundTexture(Theme theme)
        {
            if (groundCache.TryGetValue(theme, out var t) && t != null) return t;
            var look = GetLook(theme);
            t = new Texture2D(GroundSize, GroundSize, TextureFormat.RGBA32, true) { name = "Ground_" + theme, wrapMode = TextureWrapMode.Repeat };
            var px = new Color[GroundSize * GroundSize];
            for (int y = 0; y < GroundSize; y++)
                for (int x = 0; x < GroundSize; x++)
                {
                    float u = x / (float)GroundSize, v = y / (float)GroundSize;
                    Color c;
                    switch (theme)
                    {
                        case Theme.Winter:
                        {
                            float n = Fbm(u, v, 6f, 31);
                            float sparkle = Noise(u, v, 90f, 32) > 0.9f ? 0.06f : 0f;
                            c = Color.Lerp(look.groundDetail, look.ground, 0.4f + 0.6f * n) + new Color(sparkle, sparkle, sparkle);
                            break;
                        }
                        case Theme.Desert:
                        {
                            float ripple = 0.5f + 0.5f * Mathf.Sin((v * 14f + 2f * Fbm(u, v, 3f, 41)) * Mathf.PI * 2f);
                            float grain = Noise(u, v, 120f, 42);
                            c = Color.Lerp(look.groundDetail, look.ground, 0.5f + 0.4f * ripple + 0.1f * grain);
                            break;
                        }
                        default:
                        {
                            float blotch = Fbm(u, v, 5f, 51);
                            float blades = Noise(u, v, 140f, 52);
                            c = Color.Lerp(look.groundDetail, look.ground, 0.35f + 0.5f * blotch + 0.15f * blades);
                            // 드문드문 작은 꽃점
                            if (Noise(u, v, 60f, 53) > 0.93f) c = Color.Lerp(c, new Color(1f, 0.9f, 0.4f), 0.7f);
                            break;
                        }
                    }
                    px[y * GroundSize + x] = c;
                }
            t.SetPixels(px); t.Apply();
            groundCache[theme] = t;
            return t;
        }
    }
}
