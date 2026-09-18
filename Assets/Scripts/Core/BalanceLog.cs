using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SmashGame
{
    /// <summary>
    /// 난이도 밸런스용 플레이 로그. 에디터·빌드 모두 기록한다 (PlayLog는 에디터 전용 물리 추적).
    /// 세 CSV로 쌓는다 — 세션이 바뀌어도 같은 파일에 이어 붙이므로 그대로 pandas/엑셀로 읽으면 된다.
    ///   levels.csv  : 레벨 시도 1행 (구조물·블록·질량·시작 공·사용 공·결과·소요 시간·남은 블록·코인·스탯·원시 난이도 지수)
    ///   shots.csv   : 발사 1행 (목표점·발사 전 남은 블록·이 발로 떨어뜨린 블록 수(다음 발/종료까지)·발사 간격)
    ///   upgrades.csv: 강화 1행 (레벨·스탯·전후 Lv·비용·남은 코인)
    /// 위치: 에디터는 프로젝트 루트/PlayLogs/balance/, 빌드는 persistentDataPath/PlayLogs/balance/.
    /// </summary>
    public static class BalanceLog
    {
        static string dir;
        static string session;
        static bool ready;

        // 현재 레벨 시도
        static int level, attempt, blocks0, startBalls, ballsGiven, shots, blocksLeftAtLastShot;
        static float totalMass, tStart, tLastShot, rawIndex;
        static string structure;
        static int structureType; static float ballFactor;
        static bool active, hard;
        static string motion; static int obstacle, reinforced, sticky, plates;
        static readonly List<string> pendingShots = new();   // 이 발로 떨어뜨린 수는 다음 발에서 확정되므로 잠시 보관
        static int lastShotBlocksLeft = -1;
        static readonly Dictionary<int, int> attempts = new();

        static void Ensure()
        {
            if (ready) return;
            ready = true;
            string root = Application.isEditor ? System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "PlayLogs")) : System.IO.Path.Combine(Application.persistentDataPath, "PlayLogs");
            dir = System.IO.Path.Combine(root, "balance");
            System.IO.Directory.CreateDirectory(dir);
            session = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Header("levels.csv", "session,time,level,attempt,structure,structureType,ballFactor,blocks,totalMass,massScale,startBalls,ballsGiven,ballsUsed,ballsLeft,shots,result,durationSec,blocksLeft,blocksFallen,coinsEarned,coinsAfter,powerLv,sizeLv,massLv,ammoLv,hard,motion,obstacle,reinforced,sticky,plates,rawIndex");
            Header("shots.csv", "session,level,attempt,shot,tSinceStart,sinceLastShot,targetX,targetY,targetZ,blocksLeftBefore,fallenByThisShot,ballsLeftAfter");
            Header("upgrades.csv", "session,time,level,stat,fromLv,toLv,cost,coinsAfter");
        }

        static void Header(string file, string header)
        {
            string p = System.IO.Path.Combine(dir, file);
            if (!System.IO.File.Exists(p)) System.IO.File.WriteAllText(p, header + "\n", Encoding.UTF8);
        }

        static void Append(string file, string line)
        {
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(dir, file), line + "\n", Encoding.UTF8); }
            catch (System.Exception e) { Debug.LogWarning("[BalanceLog] " + e.Message); }
        }

        static string F(float v) => v.ToString("F3", CultureInfo.InvariantCulture);
        static string Q(string s) => "\"" + (s ?? "").Replace("\"", "'") + "\"";

        /// <summary>레벨 시작 (LevelController.Finish)</summary>
        public static void LevelStart(LevelInfo info, int lv, int ballsGivenTotal, SaveData d)
        {
            Ensure();
            if (info == null || info.tower || info.bonus) { active = false; return; }
            FlushPendingShots(-1);
            active = true; level = lv; structure = info.structureName; blocks0 = info.blocks.Count;
            structureType = info.structureType; ballFactor = info.ballFactor;
            attempts.TryGetValue(lv, out attempt); attempt++; attempts[lv] = attempt;
            totalMass = 0f; foreach (var b in info.blocks) { var rb = b != null ? b.GetComponent<Rigidbody>() : null; if (rb != null) totalMass += rb.mass; }
            startBalls = info.startBalls; ballsGiven = ballsGivenTotal; shots = 0; tStart = Time.time; tLastShot = -1f; lastShotBlocksLeft = -1;
            hard = info.hard; motion = info.motion.ToString();
            obstacle = 0; reinforced = 0; sticky = 0;
            foreach (var b in info.blocks) { if (b == null) continue; if (b.hp > 1) reinforced++; if (b.sticky) sticky++; }
            plates = LevelBuilder.PedestalColliders.FindAll(c => c != null && c.name == "PedestalTop").Count;
            var root = info.blocks.Count > 0 && info.blocks[0] != null ? info.blocks[0].transform.root : null;
            if (root != null) obstacle = (root.GetComponentInChildren<PendulumHammer>() != null || root.GetComponentInChildren<Windmill>() != null) ? 1 : 0;
            // 분석용 원시 난이도 지수 (난이도 곡선 설계 문서와 같은 식, 1레벨 원통 다발 = 1.0)
            float mot = info.motion == Balance.MotionKind.SpinBob ? 0.30f : info.motion == Balance.MotionKind.None ? 0f : 0.15f;
            float f = 1f + mot + 0.5f * reinforced / Mathf.Max(1, blocks0) + 0.05f * sticky / 2f + 0.2f * obstacle;
            rawIndex = (totalMass / Mathf.Max(1, startBalls)) * f / 0.855f;
        }

        /// <summary>발사 (Cannon.Fire → LevelController.OnFired 뒤)</summary>
        public static void Shot(Vector3 target, int blocksLeftBefore, int ballsLeftAfter)
        {
            if (!active) return;
            shots++;
            FlushPendingShots(blocksLeftBefore);
            float t = Time.time - tStart; float gap = tLastShot < 0f ? 0f : Time.time - tLastShot; tLastShot = Time.time;
            pendingShots.Add($"{session},{level},{attempt},{shots},{F(t)},{F(gap)},{F(target.x)},{F(target.y)},{F(target.z)},{blocksLeftBefore},%FALLEN%,{ballsLeftAfter}");
            lastShotBlocksLeft = blocksLeftBefore;
        }

        static void FlushPendingShots(int blocksLeftNow)
        {
            if (pendingShots.Count == 0) return;
            int fallen = (blocksLeftNow < 0 || lastShotBlocksLeft < 0) ? -1 : Mathf.Max(0, lastShotBlocksLeft - blocksLeftNow);
            foreach (var s in pendingShots) Append("shots.csv", s.Replace("%FALLEN%", fallen.ToString()));
            pendingShots.Clear();
        }

        /// <summary>레벨 종료. result: won / lost / quit</summary>
        public static void LevelEnd(string result, int ballsLeft, int blocksLeft, int coinsEarned, SaveData d)
        {
            if (!active) return;
            active = false;
            FlushPendingShots(blocksLeft);
            float dur = Time.time - tStart;
            Append("levels.csv", string.Join(",", new[] {
                session, System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), level.ToString(), attempt.ToString(), Q(structure), structureType.ToString(), F(ballFactor), blocks0.ToString(), F(totalMass), F(Balance.BlockMassScale(level)),
                startBalls.ToString(), ballsGiven.ToString(), (ballsGiven - ballsLeft).ToString(), ballsLeft.ToString(), shots.ToString(), result, F(dur), blocksLeft.ToString(), (blocks0 - blocksLeft).ToString(),
                coinsEarned.ToString(), d.coins.ToString(), d.powerLv.ToString(), d.sizeLv.ToString(), d.massLv.ToString(), d.ammoLv.ToString(),
                hard ? "1" : "0", motion, obstacle.ToString(), reinforced.ToString(), sticky.ToString(), plates.ToString(), F(rawIndex) }));
        }

        public static void Upgrade(int lv, string stat, int fromLv, int toLv, int cost, int coinsAfter)
        {
            Ensure();
            Append("upgrades.csv", $"{session},{System.DateTime.Now:yyyy-MM-dd HH:mm:ss},{lv},{stat},{fromLv},{toLv},{cost},{coinsAfter}");
        }

        public static string Dir { get { Ensure(); return dir; } }
    }
}
