using System;
using System.Collections;
using UnityEngine;

namespace SmashGame
{
    /// <summary>
    /// 한 레벨의 진행: 남은 공, 남은 블록, 퍼펙트 카운트, 승패 판정.
    /// </summary>
    public class LevelController : MonoBehaviour
    {
        public LevelInfo Info { get; private set; }
        public int BallsLeft { get; private set; }
        public int BlocksLeft { get; private set; }
        public int Perfects { get; private set; }
        public int Level { get; private set; }
        public bool Ended { get; private set; }

        public event Action OnHudChanged;
        public event Action<Vector3> OnPerfect;

        GameManager gm;
        Cannon cannon;
        float lastBallTime = -1f;
        float startTime;

        public bool IsBonus => Info != null && Info.bonus;
        public bool IsTower => Info != null && Info.tower;
        public bool IsTimed => IsBonus || IsTower;
        public float BonusTimeLeft => IsTimed ? Mathf.Max(0f, Info.timeLimit - (Time.time - startTime)) : 0f;
        int totalBlocks;
        public int BlocksDestroyed => totalBlocks - BlocksLeft;

        public bool CanFire => !Ended && (IsTimed || BallsLeft > 0);

        public void Init(GameManager manager, int level)
        {
            gm = manager;
            Level = level;
            Info = LevelBuilder.Build(level, gm.levelRoot, gm.Data, gm.mainCamera);
            Finish();
        }

        /// <summary>격파 도전 모드로 시작</summary>
        public void InitTower(GameManager manager, int stage)
        {
            gm = manager;
            Level = gm.Data.currentLevel;
            Info = LevelBuilder.BuildTower(stage, gm.levelRoot, gm.Data, gm.mainCamera);
            Finish();
        }

        void Finish()
        {
            foreach (var b in Info.blocks) b.controller = this;
            BlocksLeft = Info.blocks.Count;
            totalBlocks = BlocksLeft;

            var stats = gm.CurrentBallStats();
            int streakBonus = gm.Data.winStreak >= 1 ? 3 : 0; // 연승 +3 (역기획서 2.4)
            BallsLeft = Info.startBalls + stats.ammoBonus + streakBonus;
            cannon = Cannon.Create(gm.levelRoot, gm.mainCamera, stats, this);
            startTime = Time.time;
            OnHudChanged?.Invoke();
        }

        public void OnFired()
        {
            if (!IsTimed) BallsLeft--;   // 제한 시간 모드는 공 무제한
            lastBallTime = Time.time;
            OnHudChanged?.Invoke();
        }

        public void OnBallHit(bool perfect, Vector3 point)
        {
            if (Ended) return;
            if (perfect)
            {
                Perfects++;
                OnPerfect?.Invoke(point);
                StartCoroutine(SlowMo());
                OnHudChanged?.Invoke();
            }
        }

        IEnumerator SlowMo()
        {
            Time.timeScale = 0.3f;
            yield return new WaitForSecondsRealtime(0.3f);
            Time.timeScale = 1f;
        }

        public void OnBlockRemoved(Block b)
        {
            if (Ended) return;
            BlocksLeft--;
            OnHudChanged?.Invoke();
            if (BlocksLeft <= 0) StartCoroutine(WinRoutine());
        }

        IEnumerator WinRoutine()
        {
            Ended = true;
            cannon.inputEnabled = false;
            yield return new WaitForSeconds(1.0f);
            Time.timeScale = 1f;
            EndTimedOrWin();
        }

        int lastShownSecond = -1;

        void EndTimedOrWin()
        {
            if (IsTower) gm.OnTowerEnded(Info.towerStage, BlocksDestroyed, totalBlocks);
            else if (IsBonus) gm.OnBonusEnded(BlocksDestroyed, totalBlocks);
            else gm.OnLevelWon(BallsLeft, Perfects);
        }

        void Update()
        {
            if (Ended) return;
            if (IsTimed)
            {
                int sec = Mathf.CeilToInt(BonusTimeLeft);
                if (sec != lastShownSecond) { lastShownSecond = sec; OnHudChanged?.Invoke(); }
                if (BonusTimeLeft <= 0f)
                {
                    Ended = true;
                    cannon.inputEnabled = false;
                    Time.timeScale = 1f;
                    EndTimedOrWin();
                }
                return;
            }
            // 공을 다 썼고 3.5초 동안 상황이 정리됐는데 블록이 남았으면 실패
            if (BallsLeft <= 0 && lastBallTime > 0f && Time.time - lastBallTime > 3.5f && BlocksLeft > 0)
            {
                Ended = true;
                cannon.inputEnabled = false;
                Time.timeScale = 1f;
                gm.OnLevelLost();
            }
        }

        public void Abort()
        {
            Ended = true;
            Time.timeScale = 1f;
        }

        void OnDestroy() { Time.timeScale = 1f; }
    }
}
