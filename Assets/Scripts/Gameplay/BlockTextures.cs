using System.Collections.Generic;
using UnityEngine;

namespace SmashGame
{
    /// <summary>
    /// 블록 소재별 텍스처를 코드로 생성한다(외부 이미지 없음).
    /// 텍스처는 밝은 회색조/약한 색으로 만들고, 실제 색은 머티리얼 tint(color)로 입힌다.
    /// </summary>
    public static class BlockTextures
    {
        const int Size = 512;

        struct Mask { public float[] val; public float[] mix; } // val: 밝기, mix: 틴트 적용 비율(0=흰색 유지, 1=틴트)
        static readonly Dictionary<BlockKind, Mask> masks = new();
        static readonly Dictionary<long, Texture2D> cache = new();

        /// <summary>소재 + 색 조합의 텍스처. 흰 줄무늬·테두리는 흰색으로 남고 나머지에 색이 입혀진다.</summary>
        public static Texture2D Get(BlockKind kind, Color tint)
        {
            long key = ((long)kind << 32) | ((long)Mathf.RoundToInt(tint.r * 255) << 16) | ((long)Mathf.RoundToInt(tint.g * 255) << 8) | (long)Mathf.RoundToInt(tint.b * 255);
            if (cache.TryGetValue(key, out var t) && t != null) return t;
            var m = GetMask(kind);
            bool wrap = WrapsAround(kind);
            t = new Texture2D(Size, Size, TextureFormat.RGBA32, true);
            var px = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                float vv = y / (float)(Size - 1);
                for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x;
                    float uu = x / (float)(Size - 1);
                    // 구운 AO: 면 가장자리·모서리를 살짝 어둡게, 중앙은 살짝 밝게 (둥근 메시와 합쳐져 부드러운 입체감)
                    float edge = wrap ? Mathf.Min(vv, 1f - vv) : EdgeDist(uu, vv);
                    float ao = 1f - 0.10f * (1f - SStep(0f, 0.22f, edge));
                    float center = 1f + 0.04f * SStep(0.15f, 0.45f, edge);
                    float v = m.val[i] * ao * center;
                    Color c = Color.Lerp(Color.white, tint, m.mix[i]);
                    px[i] = new Color(Mathf.Clamp01(c.r * v), Mathf.Clamp01(c.g * v), Mathf.Clamp01(c.b * v), 1f);
                }
            }
            t.SetPixels(px); t.Apply();
            t.name = "Tex_" + kind;
            t.wrapMode = TextureWrapMode.Repeat;
            t.filterMode = FilterMode.Bilinear;
            t.anisoLevel = 4;
            cache[key] = t;
            return t;
        }

        /// <summary>원통형 블록(옆면 텍스처가 u 방향으로 이어짐)</summary>
        static bool WrapsAround(BlockKind kind)
            => kind == BlockKind.Cylinder || kind == BlockKind.Candy || kind == BlockKind.Log || kind == BlockKind.Stone;

        static Mask GetMask(BlockKind kind)
        {
            if (masks.TryGetValue(kind, out var m)) return m;
            m = new Mask { val = new float[Size * Size], mix = new float[Size * Size] };
            for (int i = 0; i < m.mix.Length; i++) m.mix[i] = 1f;
            switch (kind)
            {
                case BlockKind.Crown: CrownEmblem(m); break;
                case BlockKind.Cylinder: Bands(m); break;
                case BlockKind.Candy: CandyStripes(m); break;
                case BlockKind.Ice: Ice(m); break;
                case BlockKind.Crate: Crate(m); break;
                case BlockKind.Log: Bark(m); break;
                case BlockKind.Plank: WoodGrain(m); break;
                case BlockKind.Stone: Marble(m); break;
                default: Bevel(m); break;
            }
            masks[kind] = m;
            return m;
        }

        /// <summary>소재별 표면 질감 (smoothness, metallic)</summary>
        public static (float smooth, float metal) Surface(BlockKind kind) => kind switch
        {
            BlockKind.Ice => (0.95f, 0.1f),
            BlockKind.Candy => (0.9f, 0f),
            BlockKind.Cylinder => (0.78f, 0f),
            BlockKind.Cube => (0.72f, 0f),
            BlockKind.Crown => (0.8f, 0.55f),
            BlockKind.Stone => (0.28f, 0f),
            BlockKind.Crate => (0.2f, 0f),
            BlockKind.Log => (0.15f, 0f),
            BlockKind.Plank => (0.3f, 0f),
            _ => (0.5f, 0f),
        };

        // ---------------- 생성기 (val = 밝기, mix = 틴트 비율) ----------------

        static float Noise(float x, float y, float scale, int seed)
            => Mathf.PerlinNoise(x * scale + seed * 13.7f, y * scale + seed * 7.1f);

        static float Fbm(float x, float y, float scale, int seed)
            => 0.55f * Noise(x, y, scale, seed) + 0.3f * Noise(x, y, scale * 2f, seed + 1) + 0.15f * Noise(x, y, scale * 4f, seed + 2);

        /// <summary>GLSL식 smoothstep(edge0, edge1, x). Mathf.SmoothStep은 "a~b 사이를 t로 보간"이라 의미가 다르다.</summary>
        static float SStep(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        static float EdgeDist(float u, float v) => Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v));

        /// <summary>플라스틱 큐브: 가장자리 베벨 + 안쪽 밝은 면</summary>
        static void Bevel(Mask m)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)(Size - 1), v = y / (float)(Size - 1);
                    float edge = EdgeDist(u, v);
                    // 메시 자체가 둥글어졌으므로 텍스처의 베벨은 얇고 은은하게, 안쪽 패널은 살짝 밝게
                    float bevel = Mathf.SmoothStep(0.86f, 1f, Mathf.Clamp01(edge / 0.05f));
                    float inner = 1f + 0.05f * SStep(0.14f, 0.18f, edge);
                    float shine = 1f + 0.06f * (v - 0.5f);
                    float line = 1f - 0.10f * Mathf.Exp(-Mathf.Pow((edge - 0.145f) / 0.006f, 2f));
                    m.val[y * Size + x] = 0.97f * bevel * inner * shine * line;
                }
        }

        /// <summary>왕관 큐브: 베벨 위에 금색 왕관 문양 (문양은 흰색+노랑으로 틴트와 무관)</summary>
        static void CrownEmblem(Mask m)
        {
            Bevel(m);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    bool inCrown = u > 0.28f && u < 0.72f && v > 0.30f && v < 0.42f;
                    for (int k = 0; k < 3; k++)
                    {
                        float cx = 0.36f + k * 0.14f;
                        float h = k == 1 ? 0.30f : 0.22f;
                        float dy = v - 0.42f;
                        if (dy >= 0 && dy < h && Mathf.Abs(u - cx) < 0.07f * (1f - dy / h)) inCrown = true;
                    }
                    if (inCrown) { m.mix[y * Size + x] = 0f; m.val[y * Size + x] = 1.0f; }
                }
        }

        /// <summary>원통: 위아래 흰 테두리 띠 + 세로 하이라이트</summary>
        static void Bands(Mask m)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    int i = y * Size + x;
                    bool rim = v < 0.07f || v > 0.93f;
                    bool rimLine = (v > 0.07f && v < 0.09f) || (v > 0.91f && v < 0.93f);
                    if (rim) { m.mix[i] = 0f; m.val[i] = 0.98f; }
                    else if (rimLine) m.val[i] = 0.75f;
                    else m.val[i] = 0.92f + 0.18f * Mathf.Exp(-Mathf.Pow((u - 0.3f) / 0.12f, 2f));
                }
        }

        /// <summary>사탕: 흰 대각 줄무늬</summary>
        static void CandyStripes(Mask m)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    int i = y * Size + x;
                    float s = Mathf.Repeat(u * 3f + v * 1.0f, 1f);          // 둘레에 굵은 줄 3개 (멀리서도 보이게)
                    float white = 1f - SStep(0.44f, 0.48f, s);   // 1 = 흰 줄
                    m.mix[i] = 1f - white;
                    m.val[i] = 0.96f + 0.08f * Mathf.Exp(-Mathf.Pow((u - 0.3f) / 0.12f, 2f));
                }
        }

        /// <summary>얼음: 밝은 바탕 + 서리 + 가는 금 (틴트는 약하게)</summary>
        static void Ice(Mask m)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    int i = y * Size + x;
                    float frost = Fbm(u, v, 5f, 3);
                    float n = Noise(u, v, 3f, 9);
                    float crack = Mathf.Abs(n - 0.5f) < 0.006f ? 0.7f : 1f;
                    float rim = Mathf.SmoothStep(0.6f, 1f, Mathf.Clamp01(EdgeDist(u, v) / 0.06f));
                    float sparkle = Noise(u, v, 60f, 17) > 0.86f ? 1.08f : 1f;   // 반짝이는 결정 점
                    m.val[i] = (0.86f + 0.14f * frost) * crack * (0.9f + 0.1f * rim) * sparkle;
                    m.mix[i] = 0.5f - 0.3f * frost;    // 틴트는 절반만: 얼음은 흰빛이 도는 연한 색이어야 조명 음영이 보인다
                }
        }

        /// <summary>나무 상자: 가로 널빤지 + 이음새 + 못 + 대각 보강대</summary>
        static void Crate(Mask m)
        {
            var nails = new[] { new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.05f), new Vector2(0.05f, 0.95f), new Vector2(0.95f, 0.95f) };
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    int i = y * Size + x;
                    float grain = 0.85f + 0.15f * Noise(u * 0.3f, v, 40f, 5) + 0.1f * Fbm(u, v, 6f, 6);
                    float plankSeam = Mathf.Repeat(v * 4f, 1f) < 0.06f ? 0.55f : 1f;
                    float edge = EdgeDist(u, v);
                    float frame = edge < 0.09f ? 0.92f : 1f;
                    float frameLine = (edge > 0.085f && edge < 0.1f) ? 0.6f : 1f;
                    bool diag = Mathf.Abs(u - v) < 0.05f || Mathf.Abs(u - (1 - v)) < 0.05f;
                    float brace = diag && edge > 0.09f ? 0.88f : 1f;
                    float nail = 1f;
                    foreach (var p in nails) if ((new Vector2(u, v) - p).magnitude < 0.018f) nail = 0.45f;
                    m.val[i] = grain * plankSeam * frame * frameLine * brace * nail;
                }
        }

        /// <summary>통나무 껍질: 축 방향 굵은 결 + 옹이</summary>
        static void Bark(Mask m)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    int i = y * Size + x;
                    float streak = 0.7f + 0.3f * Noise(u, v * 0.15f, 30f, 11);
                    float rough = 0.9f + 0.1f * Fbm(u, v, 12f, 12);
                    float knot = Mathf.Exp(-((u - 0.62f) * (u - 0.62f) + (v - 0.4f) * (v - 0.4f)) * 180f);
                    m.val[i] = streak * rough * (1f - 0.35f * knot);
                }
        }

        /// <summary>판자: 가로 나뭇결</summary>
        static void WoodGrain(Mask m)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    int i = y * Size + x;
                    float g = Noise(u * 0.25f, v, 22f, 21);
                    float rings = 0.5f + 0.5f * Mathf.Sin((v + 0.15f * g) * 40f);
                    float val = 0.9f + 0.1f * rings - 0.06f * Fbm(u, v, 8f, 22);
                    val *= Mathf.SmoothStep(0.7f, 1f, Mathf.Clamp01(EdgeDist(u, v) / 0.05f));
                    m.val[i] = val;
                }
        }

        /// <summary>돌기둥: 대리석 결 + 세로 홈 + 위아래 받침 (틴트 약하게)</summary>
        static void Marble(Mask m)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    int i = y * Size + x;
                    float vein = Mathf.Abs(Mathf.Sin((u * 3f + v * 5f + 2f * Fbm(u, v, 4f, 31)) * 3.1f));
                    float marble = 0.8f + 0.2f * Mathf.Pow(vein, 0.5f);
                    float flute = 0.88f + 0.12f * Mathf.Abs(Mathf.Sin(u * Mathf.PI * 10f));
                    float capital = (v < 0.08f || v > 0.92f) ? 0.85f : 1f;
                    m.val[i] = marble * flute * capital;
                    m.mix[i] = 0.7f;
                }
        }
    }
}
