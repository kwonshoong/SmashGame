using System.Collections.Generic;
using UnityEngine;

namespace SmashGame
{
    /// <summary>
    /// 모서리가 둥근 큐브/원기둥 메시를 코드로 만든다(외부 모델 없음).
    /// 프리미티브(Cube: 1×1×1, Cylinder: 반지름 0.5·높이 2)와 같은 로컬 공간에 만들어지므로
    /// 기존 transform.localScale·콜라이더는 그대로 두고 MeshFilter의 메시만 바꾸면 된다.
    /// 라운딩 반지름은 월드 단위로 받고, 비균등 스케일을 감안해 축마다 나눠 적용한다.
    /// </summary>
    public static class RoundedMesh
    {
        static readonly Dictionary<string, Mesh> cache = new();

        /// <summary>프리미티브로 만든 오브젝트의 메시를 둥근 버전으로 교체한다. 콜라이더는 건드리지 않는다.</summary>
        public static void Apply(GameObject go, float radiusWorld)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            string src = mf.sharedMesh.name;
            var s = go.transform.localScale;
            if (src.StartsWith("Cube")) mf.sharedMesh = Box(s, radiusWorld);
            else if (src.StartsWith("Cylinder")) mf.sharedMesh = Cylinder(s, radiusWorld);
        }

        static string Key(string kind, Vector3 s, float r)
            => $"{kind}_{s.x:F3}_{s.y:F3}_{s.z:F3}_{r:F3}";

        // ---------------------------------------------------------------- 둥근 박스

        /// <summary>단위 큐브 공간(±0.5)에서, 월드 스케일 s를 적용했을 때 모서리 반지름이 r이 되는 박스</summary>
        public static Mesh Box(Vector3 s, float r)
        {
            r = Mathf.Min(r, Mathf.Min(s.x, Mathf.Min(s.y, s.z)) * 0.45f);
            string key = Key("Box", s, r);
            if (cache.TryGetValue(key, out var m) && m != null) return m;

            const int rimSegs = 3;
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            Vector3 half = s * 0.5f;                       // 월드 반크기
            Vector3 inner = half - Vector3.one * r;        // 라운딩이 시작되는 안쪽 박스

            // 각 축의 분할 좌표(월드 단위)
            float[] AxisCoords(float h)
            {
                var list = new List<float>();
                for (int i = 0; i <= rimSegs; i++) list.Add(-h + r * (i / (float)rimSegs));
                for (int i = 1; i <= rimSegs; i++) list.Add(h - r + r * (i / (float)rimSegs));
                return list.ToArray();
            }
            float[] cx = AxisCoords(half.x), cy = AxisCoords(half.y), cz = AxisCoords(half.z);

            // 면 6개: (법선 축, 부호, 가로 축, 세로 축)
            void Face(int nAxis, int sign, int uAxis, int vAxis)
            {
                float[] cu = uAxis == 0 ? cx : uAxis == 1 ? cy : cz;
                float[] cv = vAxis == 0 ? cx : vAxis == 1 ? cy : cz;
                float hn = nAxis == 0 ? half.x : nAxis == 1 ? half.y : half.z;
                int baseIndex = verts.Count;
                for (int j = 0; j < cv.Length; j++)
                    for (int i = 0; i < cu.Length; i++)
                    {
                        Vector3 p = Vector3.zero;
                        p[nAxis] = sign * hn;
                        p[uAxis] = cu[i];
                        p[vAxis] = cv[j];
                        // 안쪽 박스로 클램프한 점에서 r만큼 밀어내면 둥근 모서리가 된다
                        Vector3 q = new Vector3(
                            Mathf.Clamp(p.x, -inner.x, inner.x),
                            Mathf.Clamp(p.y, -inner.y, inner.y),
                            Mathf.Clamp(p.z, -inner.z, inner.z));
                        Vector3 n = (p - q);
                        if (n.sqrMagnitude < 1e-8f) { n = Vector3.zero; n[nAxis] = sign; }
                        n.Normalize();
                        Vector3 w = q + n * r;
                        // 월드 → 단위 큐브 로컬 (스케일로 나눔)
                        verts.Add(new Vector3(w.x / s.x, w.y / s.y, w.z / s.z));
                        // 법선은 비균등 스케일의 역전치로 변환해야 렌더 시 올바르다: n_local = n * s (Unity가 다시 1/s를 곱함)
                        norms.Add(new Vector3(n.x * s.x, n.y * s.y, n.z * s.z).normalized);
                        float uu = (cu[i] + (uAxis == 0 ? half.x : uAxis == 1 ? half.y : half.z)) / (2f * (uAxis == 0 ? half.x : uAxis == 1 ? half.y : half.z));
                        float vv = (cv[j] + (vAxis == 0 ? half.x : vAxis == 1 ? half.y : half.z)) / (2f * (vAxis == 0 ? half.x : vAxis == 1 ? half.y : half.z));
                        uvs.Add(new Vector2(uu, vv));
                    }
                int w2 = cu.Length;
                for (int j = 0; j < cv.Length - 1; j++)
                    for (int i = 0; i < cu.Length - 1; i++)
                    {
                        int a = baseIndex + j * w2 + i, b = a + 1, c = a + w2, d = c + 1;
                        // 바깥을 향하도록 감기 방향 결정
                        Vector3 pa = verts[a], pb = verts[b], pc = verts[c];
                        Vector3 nrm = Vector3.Cross(pb - pa, pc - pa);
                        Vector3 outward = Vector3.zero; outward[nAxis] = sign;
                        if (Vector3.Dot(nrm, outward) > 0) { tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(b); tris.Add(d); tris.Add(c); }
                        else { tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(b); tris.Add(c); tris.Add(d); }
                    }
            }
            Face(0, +1, 2, 1); Face(0, -1, 2, 1);   // ±X : 가로 z, 세로 y
            Face(2, +1, 0, 1); Face(2, -1, 0, 1);   // ±Z : 가로 x, 세로 y
            Face(1, +1, 0, 2); Face(1, -1, 0, 2);   // ±Y : 가로 x, 세로 z

            m = new Mesh { name = "RoundedBox" };
            m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateBounds(); m.RecalculateTangents();
            cache[key] = m;
            return m;
        }

        // ---------------------------------------------------------------- 둥근 원기둥

        /// <summary>단위 원기둥 공간(반지름 0.5, y ±1)에서, 스케일 s 적용 시 위아래 테두리가 반지름 r로 둥근 원기둥</summary>
        public static Mesh Cylinder(Vector3 s, float r)
        {
            float radiusW = 0.5f * Mathf.Max(s.x, s.z);
            float halfHW = s.y;
            r = Mathf.Min(r, Mathf.Min(radiusW, halfHW) * 0.6f);
            string key = Key("Cyl", s, r);
            if (cache.TryGetValue(key, out var m) && m != null) return m;

            const int radial = 36, rimSegs = 5;
            // 프로파일 (월드 단위): (반지름 방향, y, 법선 r, 법선 y, v)
            var prof = new List<(float pr, float py, float nr, float ny, float v)>();
            prof.Add((0f, -halfHW, 0f, -1f, 0f));
            prof.Add((radiusW - r, -halfHW, 0f, -1f, 0f));
            for (int i = 1; i <= rimSegs; i++)
            {
                float a = Mathf.Lerp(-Mathf.PI * 0.5f, 0f, i / (float)rimSegs);
                float pr = radiusW - r + r * Mathf.Cos(a), py = -halfHW + r + r * Mathf.Sin(a);
                prof.Add((pr, py, Mathf.Cos(a), Mathf.Sin(a), (py + halfHW) / (2f * halfHW)));
            }
            for (int i = 1; i <= rimSegs; i++)
            {
                float a = Mathf.Lerp(0f, Mathf.PI * 0.5f, i / (float)rimSegs);
                float pr = radiusW - r + r * Mathf.Cos(a), py = halfHW - r + r * Mathf.Sin(a);
                prof.Add((pr, py, Mathf.Cos(a), Mathf.Sin(a), (py + halfHW) / (2f * halfHW)));
            }
            prof.Add((0f, halfHW, 0f, 1f, 1f));

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            int rows = prof.Count, cols = radial + 1;
            for (int j = 0; j < rows; j++)
            {
                var p = prof[j];
                bool cap = j == 0 || j == 1 || j == rows - 1 || j == rows - 2;
                for (int i = 0; i < cols; i++)
                {
                    float ang = i / (float)radial * Mathf.PI * 2f;
                    float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                    Vector3 w = new Vector3(p.pr * cs, p.py, p.pr * sn);
                    Vector3 n = new Vector3(p.nr * cs, p.ny, p.nr * sn);
                    verts.Add(new Vector3(w.x / s.x, w.y / s.y, w.z / s.z));
                    norms.Add(new Vector3(n.x * s.x, n.y * s.y, n.z * s.z).normalized);
                    // 옆면: u=둘레, v=높이 / 윗·아랫면: 평면 투영(텍스처 중앙)
                    if (cap && (j == 0 || j == rows - 1)) uvs.Add(new Vector2(0.5f, 0.5f));
                    else if (cap) uvs.Add(new Vector2(0.5f + 0.4f * cs, 0.5f + 0.4f * sn));
                    else uvs.Add(new Vector2(1f - i / (float)radial, p.v));
                }
            }
            for (int j = 0; j < rows - 1; j++)
                for (int i = 0; i < radial; i++)
                {
                    int a = j * cols + i, b = a + 1, c = a + cols, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }

            m = new Mesh { name = "RoundedCylinder" };
            m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateBounds(); m.RecalculateTangents();
            cache[key] = m;
            return m;
        }
    }
}
