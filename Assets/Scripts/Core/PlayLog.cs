using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SmashGame
{
    /// <summary>
    /// 플레이 물리 로그(에디터 전용). 공이 블록에 맞을 때마다 맞은 블록과 그 주변(반경 1.2) 블록을 1.5초 동안 추적해
    /// 물리 스텝 3개마다 위치·속도·회전속도·수면 상태를 기록한다. 파일: 프로젝트 루트/PlayLogs/play_<시각>.txt
    /// 분석용이라 게임 빌드에는 들어가지 않는다.
    /// </summary>
    public class PlayLog : MonoBehaviour
    {
#if UNITY_EDITOR
        static PlayLog inst;
        StringBuilder sb = new StringBuilder();
        string path;
        int step;
        int shotIndex;
        float flushAt;

        class Watch { public Block block; public string tag; public float until; }
        readonly List<Watch> watches = new();

        public static void Ensure()
        {
            if (inst != null) return;
            var go = new GameObject("PlayLog");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<PlayLog>();
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "PlayLogs"));
            System.IO.Directory.CreateDirectory(dir);
            inst.path = System.IO.Path.Combine(dir, "play_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
            inst.sb.AppendLine($"# PlayLog start {System.DateTime.Now}  fixedDt={Time.fixedDeltaTime}  gravity={Physics.gravity.y}  depen={Physics.defaultMaxDepenetrationVelocity}");
            inst.sb.AppendLine($"# impulseBase={Balance.BallImpulse} spreadSteps={Block.PushSpreadSteps} combo={Block.ComboStep}/{Block.ComboWindow}s");
        }

        public static void Shot(Vector3 from, Vector3 target, BallStats stats)
        {
            if (inst == null) return;
            inst.shotIndex++;
            inst.sb.AppendLine($"SHOT #{inst.shotIndex} t={Time.time:F3} from={V(from)} target={V(target)} power={stats.power:F2} size={stats.size:F2} mass={stats.mass:F2}");
        }

        public static void Hit(Block direct, Vector3 point, Vector3 dir, float impulse, float combo, Vector3 ballVelBefore, Vector3 ballVelAfter)
        {
            if (inst == null || direct == null) return;
            var rb = direct.GetComponent<Rigidbody>();
            inst.sb.AppendLine($"HIT t={Time.time:F3} block={direct.name}#{direct.GetInstanceID()} kind={direct.kind} mass={(rb ? rb.mass : 0):F2} hp={direct.hp} point={V(point)} dir={V(dir)} impulse={impulse:F1} combo={combo:F2} ballV={V(ballVelBefore)}->{V(ballVelAfter)}");
            inst.AddWatch(direct, "D");
            foreach (var h in Physics.OverlapSphere(direct.transform.position, 1.2f))
            {
                var b = h.GetComponentInParent<Block>();
                if (b != null && b != direct) inst.AddWatch(b, "N");
            }
            inst.flushAt = Time.time + 2f;
        }

        void AddWatch(Block b, string tag)
        {
            foreach (var w in watches) if (w.block == b) { w.until = Time.time + 1.5f; if (tag == "D") w.tag = "D"; return; }
            watches.Add(new Watch { block = b, tag = tag, until = Time.time + 1.5f });
        }

        void FixedUpdate()
        {
            step++;
            if (watches.Count == 0) return;
            if (step % 3 != 0) return;
            bool any = false;
            var line = new StringBuilder();
            line.Append($"S t={Time.time:F3}");
            for (int i = watches.Count - 1; i >= 0; i--)
            {
                var w = watches[i];
                if (w.block == null || Time.time > w.until) { watches.RemoveAt(i); continue; }
                var rb = w.block.GetComponent<Rigidbody>();
                if (rb == null) continue;
                any = true;
                line.Append($" | {w.tag}:{w.block.name}#{w.block.GetInstanceID()} p={V(w.block.transform.position)} v={V(rb.linearVelocity)} w={rb.angularVelocity.magnitude:F1} {(rb.IsSleeping() ? "Z" : "")}{(w.block.Removed ? "X" : "")}");
            }
            if (any) sb.AppendLine(line.ToString());
        }

        void Update()
        {
            if (flushAt > 0f && Time.time > flushAt) Flush();
        }

        void Flush()
        {
            flushAt = 0f;
            if (sb.Length == 0) return;
            System.IO.File.AppendAllText(path, sb.ToString());
            sb.Clear();
        }

        void OnDestroy() { Flush(); }
        void OnApplicationQuit() { Flush(); }

        static string V(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";
#else
        public static void Ensure() { }
        public static void Shot(Vector3 from, Vector3 target, BallStats stats) { }
        public static void Hit(Block direct, Vector3 point, Vector3 dir, float impulse, float combo, Vector3 ballVelBefore, Vector3 ballVelAfter) { }
#endif
    }
}
