using UnityEngine;

namespace SmashGame
{
    /// <summary>
    /// 경량 확장안(B′) 기획서의 수치를 한 곳에 모은 밸런스 테이블.
    /// 모든 값은 기획서의 "튜닝 시작점"이며 소프트런치에서 조정한다.
    /// </summary>
    public static class Balance
    {
        // ---------- 물리 감각 ----------
        public const float GravityScale = 2.6f;   // 낙하 속도감 (1 = 실제 중력). 2~3 사이에서 튜닝
        public const float BallSpeed = 30f;       // 발사 속도. 낮을수록 포물선이 커짐
        public const float BlockFriction = 0.22f; // 블록 운동 마찰. 낮을수록 밀리면 잘 미끄러져 떨어짐
        public const float BlockStaticFriction = 0.35f; // 블록 정지 마찰. 0.7이면 살짝 들썩인 블록이 다시 닿는 순간 죽은 듯 멈춘다(플레이 로그로 확인) — 구조물 안정은 SettleAndSleep이 맡는다
        public const float BallImpulse = 15f;     // 기본 충격량 (파괴력 100% 기준). 공 자체의 물리 충돌은 거의 0이라 이 값이 밀림의 전부

        // ---------- 공 스탯 ----------
        public const int StatMaxLevel = 50;

        public static float PowerMult(int lv) => 1f + 2.0f * (lv - 1) / (StatMaxLevel - 1);   // 100% → 300%
        public static float SizeMult(int lv)  => 1f + 0.2f * ((lv - 1) / 10);                  // 10레벨마다 1단계, 5단계 → 180%
        public static float MassMult(int lv)  => 1f + 1.5f * (lv - 1) / (StatMaxLevel - 1);   // 100% → 250%
        public static int   AmmoBonus(int lv) => Mathf.RoundToInt(12f * (lv - 1) / (StatMaxLevel - 1)); // +0 → +12
        public static int   StarRank(int lv)  => Mathf.Clamp((lv - 1) / 10 + 1, 1, 5);

        /// <summary>스탯 1레벨 강화 비용(코인). Lv1 40 → Lv50 약 7,000 (기획서 4.3 곡선 근사)</summary>
        public static int StatUpgradeCost(int currentLevel)
        {
            if (currentLevel >= StatMaxLevel) return int.MaxValue;
            return Mathf.RoundToInt(40f * Mathf.Pow(1.1115f, currentLevel - 1));
        }

        // ---------- 레벨 보상 ----------
        public const int RefundPerBall = 2;      // 남은 공 1개당 코인
        public const int PerfectBonus = 5;       // 퍼펙트 히트 1회당 코인
        public const int TrackLevels = 20;       // 20레벨 트랙
        public const int TrackReward = 100;
        public static int ClearCoin(int level) => 15 + (level * 7) % 26; // 15~40 사이, 레벨마다 고정

        // ---------- 온보딩 게이트 ----------
        public const int ForgeUnlockLevel = 8;
        public const int ForgeGiftCoins = 300;
        public const int RefundUnlockLevel = 10;
        public const int CrownUnlockLevel = 12;
        public const int TrainingUnlockLevel = 15;
        public const int ReinforcedFromLevel = 61;
        public const int StickyFromLevel = 91;

        // ---------- 보너스 스테이지 (자동차 부수기) ----------
        public const int   BonusEveryLevels = 10;          // 5, 15, 25, … (하드 레벨 사이 중간)
        public const float BonusSeconds = 20f;             // 제한 시간, 공 무제한
        public const int   BonusCoinPerBlock = 3;          // 떨어뜨린/부순 블록 1개당 코인
        public const int   BonusAllClearCoin = 80;         // 전부 부수면 추가
        public const bool  CarBonusEnabled = false;        // 자동차 보너스는 레벨 흐름에서 잠시 뺀다 (격파 도전으로 대체)
        public static bool IsBonusLevel(int level) => CarBonusEnabled && level >= 5 && level % BonusEveryLevels == 5;

        // ---------- 격파 도전 (별도 모드: 20초 무제한 발사로 거대·초중량 탑 무너뜨리기) ----------
        public const int   TowerUnlockLevel = 20;          // 레벨 20 클리어 후 해금
        public const float TowerSeconds = 20f;
        public static int  TowerCols(int stage)  => Mathf.Min(8 + (stage - 1) / 2, 10);     // 1단계 8칸 → 5단계 10칸
        public static int  TowerRows(int stage)  => Mathf.Min(8 + (stage - 1) / 2, 11);     // 1단계 8칸 → 7단계 11칸 (카메라 프레임 상한)
        public static int  TowerDepth(int stage) => 2;                                       // 항상 두 겹 (블록 100개 안팎)
        public static float TowerMassMult(int stage) => 2f * Mathf.Pow(1.45f, stage - 1);   // 1단계 ×2, 5단계 ×8.8, 8단계 ×27
        public static int  TowerHp(int stage) => stage >= 8 ? 3 : (stage >= 4 ? 2 : 1);      // 4단계부터 강화 블록
        public const float TowerReinforcedRatio = 0.25f;
        public static int  TowerCoinPerBlock(int stage) => 2 + stage;
        public static int  TowerClearCoin(int stage) => 200 * stage;
        /// <summary>안내용 권장 파괴력(%). 블록 질량이 커질수록 필요한 파괴력이 비례해서 오른다.</summary>
        public static int  TowerRecommendedPower(int stage) => Mathf.RoundToInt(Mathf.Clamp(TowerMassMult(stage) * 55f, 100f, 300f) / 10f) * 10;

        // ---------- 난이도 ----------
        public const float ReinforcedRatioCap = 0.20f;
        public static bool IsHardLevel(int level) => level >= 10 && level % 10 == 0;
        public const int StructureTypes = 12;
        /// <summary>시작 공 개수. 기본 14~20(초반 5레벨 +6, 하드 14)에 블록 수가 많으면 더 준다 (20개 초과분 2개당 +1).</summary>
        public static int StartBalls(int level, bool hard, int blockCount = 20)
        {
            int n = hard ? 14 : 14 + (level * 5) % 7;
            if (!hard && level <= 5) n += 6;
            n += Mathf.Max(0, (blockCount - 20) / 2);
            return n;
        }
        /// <summary>새 구조물(피라미드·요새·성문·쌍둥이 탑·계단·원진)이 등장하는 레벨. 그 전엔 기본 6종만.</summary>
        public const int NewStructuresFromLevel = 8;
        /// <summary>새 구조물이 두 겹(깊이 2)이 되는 레벨</summary>
        public const int DeepStructuresFromLevel = 25;
        /// <summary>새 구조물의 바닥·기둥이 돌(무거움)로 바뀌는 레벨. 그 전엔 상자·원통.</summary>
        public const int HeavyStructuresFromLevel = 30;
        /// <summary>레벨이 오를수록 블록이 조금씩 무거워진다 (Lv1 1.0 → Lv51+ 1.4 상한). 무거운 블록이 위에 얹히면 마찰로 아래 블록이 굳으므로 상한을 낮게 둔다.</summary>
        public static float BlockMassScale(int level) => Mathf.Min(1.4f, 1f + Mathf.Max(0, level - 1) * 0.008f);
        /// <summary>장애물 등장: 하드 레벨 전부 + 5레벨마다</summary>
        public static bool HasObstacle(int level) => IsHardLevel(level) || (level >= 4 && level % 5 == 2);

        /// <summary>챕터 권장 4스탯 합계 (기획서 6.4)</summary>
        public static int RecommendedStatSum(int level)
        {
            if (level <= 60) return 0;
            if (level <= 90) return 24;
            if (level <= 120) return 48;
            if (level <= 160) return 80;
            return 120;
        }

        // ---------- 훈련장 ----------
        public const float TrainingBaseCoinPerHour = 6f;
        public const float OfflineEfficiencyBase = 0.5f;
        public const float OfflineEfficiencyGloria = 0.75f;
        public const int   TrainingMaxLevel = 30;
        public const int   TrainingSmashBaseCoin = 5;   // 훈련장 구조물을 전부 떨어뜨렸을 때 기본 코인
        public static int  TrainingSmashReward(SaveData d)
            => Mathf.Max(1, Mathf.RoundToInt(TrainingSmashBaseCoin * ChapterMult[d.trainingChapter] * (1f + 0.1f * (d.trainingLevel - 1))));

        public static float TrainingLevelMult(int lv) => 1f + 0.11f * (lv - 1); // Lv10 2.0, Lv20 3.1, Lv30 4.2
        public static int   TrainingUpgradeCost(int lv) => Mathf.RoundToInt(120f * Mathf.Pow(1.25f, lv - 1));

        /// <summary>오프라인 누적 상한(시간)</summary>
        public static float OfflineCapHours(int trainingLevel, int chapter, bool charlie, bool adFree)
        {
            float h = 4f;
            if (trainingLevel >= 10) h = 6f;
            if (trainingLevel >= 20) h = 8f;
            if (charlie) h += 2f;
            if (adFree) h += 4f;
            h += ChapterCapBonus(chapter);
            return h;
        }

        /// <summary>오늘 클리어 수 배율 (기획서 5.6)</summary>
        public static float DailyClearMult(int clearedToday)
        {
            if (clearedToday <= 0) return 0.5f;
            if (clearedToday < 5) return Mathf.Lerp(0.5f, 1f, clearedToday / 5f);
            if (clearedToday < 15) return Mathf.Lerp(1f, 1.3f, (clearedToday - 5) / 10f);
            return 1.3f;
        }

        // 돼지 합류 (훈련장 레벨) — 수집·성장 없음
        public static readonly string[] PigNames = { "Rocky", "Bob", "Richie", "Punk", "Gloria", "Charlie" };
        public static readonly int[] PigJoinTrainingLevel = { 1, 3, 6, 10, 15, 20 };
        public static readonly string[] PigTraits =
        {
            "기본 생산",
            "재건 속도 +30%",
            "코인 생산 +20%",
            "탭 보너스 2배",
            "오프라인 효율 75%",
            "오프라인 상한 +2h",
        };
        public static int PigCount(int trainingLevel)
        {
            int n = 0;
            for (int i = 0; i < PigJoinTrainingLevel.Length; i++) if (trainingLevel >= PigJoinTrainingLevel[i]) n++;
            return n;
        }
        public static bool HasPig(int trainingLevel, int index) => trainingLevel >= PigJoinTrainingLevel[index];

        // 훈련장 챕터 (기획서 5.5)
        public static readonly string[] ChapterNames = { "초원 훈련장", "겨울 훈련장", "사막 훈련장", "성 훈련장" };
        public static readonly int[] ChapterUnlockLevel = { 0, 60, 120, 200 };
        public static readonly int[] ChapterUnlockCoin = { 0, 5000, 20000, 60000 };
        public static readonly float[] ChapterMult = { 1f, 1.5f, 2f, 3f };
        public static float ChapterCapBonus(int chapter) => chapter switch { 1 => 1f, 2 => 2f, 3 => 4f, _ => 0f };
        public static int ChapterExtraSlots(int chapter) => chapter; // 챕터당 슬롯 +1

        public static float CoinPerHour(SaveData d)
        {
            int pigs = Balance.PigCount(d.trainingLevel) + ChapterExtraSlots(d.trainingChapter);
            float v = TrainingBaseCoinPerHour * pigs * PowerMult(d.powerLv) * TrainingLevelMult(d.trainingLevel) * ChapterMult[d.trainingChapter];
            if (HasPig(d.trainingLevel, 2)) v *= 1.2f; // Richie
            return v;
        }
    }
}
