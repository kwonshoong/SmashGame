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

        // ---------- 실제 물리 모드 ----------
        // true면 공은 그냥 강체: 스탯이 곧 물리량(무게 = 질량, 파워 = 발사 속도, 크기 = 반지름, 탄성 = 반발계수)이고
        // 블록을 미는 힘·이웃 밀기·되튕김을 코드로 넣지 않는다 — PhysX 운동량 보존이 전부. false면 옛 방식(스탯 임펄스).
        public const bool RealPhysics = true;
        public const float RealBallMassBase = 1.2f;    // 무게 스탯 100% = 1.2kg (250% → 3.0kg). 블록은 칸당 0.5~1.1kg
        public const float RealBallRadiusBase = 0.15f;  // 크기 스탯 100% = 반지름 0.15 (180% → 0.27). 0.2에서 줄임
        public const float RealBallBounce = 0.35f;     // 탄성(반발계수). 0.1 퍽 하고 죽는 공, 0.8 통통 튀는 공
        public const float RealBallLifetime = 4f;      // 굴러다니는 공이 사라지기까지
        public const float RealHitMinSpeedFrac = 0.3f; // 이 비율(발사 속도 대비)보다 느린 접촉은 "타격"(깨짐·콤보)으로 세지 않는다
        /// <summary>파워 스탯 → 발사 속도. 100% 20, 200% 26, 300% 32</summary>
        public static float RealBallSpeed(float power) => 20f * (0.7f + 0.3f * power);

        // ---------- 공 스탯 (상한 없음) ----------
        // 레벨이 무한이므로 스탯도 상한이 없다. 레벨당 증가폭은 옛 Lv50 기준(파워 300%·무게 250%·탄약 +12)과 같은 기울기.
        // 강화 비용은 40×1.085^(Lv−1): Lv50 2.2k, Lv100 130k, Lv150 7.6M. 코인 보상이 레벨에 비례해 커지므로(CoinScale) 계속 강화가 된다.
        // 경제 시뮬(하루 100스테이지, 제일 싼 스탯부터 강화): 파워 Lv 100L 21 · 500L 43 · 1000L 55 · 2000L 70, 체감 난이도 1.2 → 500L 1.7 → 1000L 2.2 → 2000L 3.2.
        public static float PowerMult(int lv) => 1f + 0.0408f * (lv - 1);          // Lv50 300%, Lv100 504%
        public static float SizeMult(int lv)  => Mathf.Min(2.0f, 1f + 0.2f * ((lv - 1) / 10));   // 10레벨마다 +20%, 200%에서 고정 (공이 블록만큼 커지면 안 된다)
        public static float MassMult(int lv)  => 1f + 0.0306f * (lv - 1);          // Lv50 250%
        public static int   AmmoBonus(int lv) => Mathf.RoundToInt(0.245f * (lv - 1)); // Lv50 +12
        public static int   StarRank(int lv)  => Mathf.Clamp((lv - 1) / 10 + 1, 1, 5);

        /// <summary>스탯 1레벨 강화 비용(코인). 40×1.085^(Lv−1)</summary>
        public static int StatUpgradeCost(int currentLevel) => Mathf.RoundToInt(40f * Mathf.Pow(1.085f, currentLevel - 1));

        // ---------- 레벨 보상 ----------
        /// <summary>코인 보상 배율: 레벨에 비례해 커진다 (100L ×1.6, 500L ×4, 1000L ×7). 상한 없는 스탯 강화 비용을 따라가기 위한 것.</summary>
        public static float CoinScale(int level) => 1f + 0.006f * level;
        public const int RefundPerBall = 2;      // 남은 공 1개당 코인 (×CoinScale)
        public const int TrackLevels = 20;       // 20레벨 트랙
        public const int TrackReward = 100;      // (×CoinScale)
        public static int ClearCoin(int level) => Mathf.RoundToInt((15 + (level * 7) % 26) * CoinScale(level)); // 기본 15~40, 레벨 비례
        public static int RefundCoin(int level, int remainingBalls) => Mathf.RoundToInt(remainingBalls * RefundPerBall * CoinScale(level));
        public static int TrackCoin(int level) => Mathf.RoundToInt(TrackReward * CoinScale(level));

        // ---------- 온보딩 게이트 ----------
        public const int ForgeUnlockLevel = 8;
        public const int ForgeGiftCoins = 300;
        public const int RefundUnlockLevel = 10;
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
        /// <summary>강화 블록 비율 상한: 200레벨까지 20%, 그 뒤 레벨당 +0.075%p로 계속(600L 50%). 강화는 바닥 줄에만 두므로 실제로는 바닥 블록 수가 자연 상한.</summary>
        public static float ReinforcedRatioCap(int level) => 0.20f + Mathf.Max(0, level - 200) * 0.00075f;
        /// <summary>접착 블록 쌍 수: 91레벨 1쌍, 150레벨 2쌍, 그 뒤 150레벨마다 +1 (300L 3, 450L 4, 600L 5 …). 상한 없음(블록 수가 자연 상한).</summary>
        public static int StickyPairs(int level) => level < StickyFromLevel ? 0 : level < 150 ? 1 : 2 + (level - 150) / 150;
        public static bool IsHardLevel(int level) => level >= 10 && level % 10 == 0;
        public const int StructureTypes = 76;

        // ---------- 시작 공 = 구조물 전체 질량 ÷ 목표 "공 1개당 질량" ----------
        // 난이도 곡선의 핵심. 하루 2시간·100스테이지 기준. 두 축이 곱해진다: ① 공 1개당 기준 질량(레벨당 +0.8%) ② 블록 질량 배율(레벨당 +0.15%, 400L 1.6배).
        // 대장간 경제 시뮬(클리어 코인 + 남은 공 환급 + 트랙 보상으로 제일 싼 스탯부터 강화)로 체감 곡선을 확인한다 (프로젝트 문서 '난이도 곡선 설계').
        // 블록 "수"가 아니라 "질량" 기준이라 무거운 돌 구조물엔 공이 더, 가벼운 얼음엔 덜 나와 같은 구간 안의 편차가 1/3로 준다.
        // 파워·무게 스탯이 최대 3배(50레벨)까지 오르므로 6배 곡선을 체감으로는 2배 남짓으로 따라잡는다.
        public const float TargetMassPerBallBase = 1.0f;     // 1레벨: 공 1개당 1.0kg
        public const float TargetMassPerBallGrowth = 0.008f; // 레벨당 +0.8% (기준 질량 기준). 여기에 블록 질량 배율 성장(BlockMassGrowth)이 곱해져 실제 곡선이 된다
        public const float HardLevelMassMult = 1.35f;        // 하드 레벨은 공 1개당 35% 더 밀어야 한다
        public const int StartBallsBase = 4;                 // 질량 비례분에 더하는 여유
        public const int MinStartBalls = 8, MaxStartBalls = 40;   // 하한 8: 아주 높은 레벨에선 공이 8개로 고정되고 그 뒤 난이도는 블록 질량 배율이 계속 올린다
        public static float TargetMassPerBall(int level, bool hard) => TargetMassPerBallBase * (1f + level * TargetMassPerBallGrowth) * (hard ? HardLevelMassMult : 1f);
        public static int StartBalls(int level, bool hard, float totalMass)
        {
            int n = StartBallsBase + Mathf.RoundToInt(totalMass / TargetMassPerBall(level, hard));
            if (!hard && level <= 5) n += 5;   // 튜토리얼 구간 여유
            return Mathf.Clamp(n, MinStartBalls, MaxStartBalls);
        }
        /// <summary>구조물 크기 성장: base에서 시작해 perLevels 레벨마다 +1, cap까지</summary>
        // ---------- 움직이는 받침대 ----------
        public enum MotionKind { None, Spin, Bob, SpinBob }
        public const int MotionFromLevel = 30;
        /// <summary>레벨별 받침대 움직임: 30레벨부터 4레벨마다 하나씩 (회전 → 승강 → 회전+승강 순환). 하드 레벨은 항상 회전+승강.</summary>
        public static MotionKind PedestalMotionKind(int level)
        {
            if (level < MotionFromLevel) return MotionKind.None;
            if (IsHardLevel(level)) return MotionKind.SpinBob;
            if (level % 4 != 2) return MotionKind.None;
            int n = (level - MotionFromLevel) / 4;
            return n % 3 == 0 ? MotionKind.Spin : n % 3 == 1 ? MotionKind.Bob : MotionKind.SpinBob;
        }
        public static float PedestalSpinDegPerSec(int level) => Mathf.Min(30f, 15f + (level - MotionFromLevel) * 0.1f);
        public const float PedestalBobAmplitude = 0.35f;   // 위아래 ±0.35
        public const float PedestalBobPeriod = 4f;
        public const int MotionExtraBalls = 3;   // 움직이는 받침대 레벨은 타이밍을 맞춰야 하니 공 +3
        public const int MultiPedestalBobFromLevel = 25;   // 독립 받침대 여러 개짜리 구조물은 이 레벨부터 기본 승강
        /// <summary>넓은 판 받침대의 다리 수 (레퍼런스: 다리 3개 202·243·248, 5개 252). 20레벨부터 가끔.</summary>
        /// <summary>넓은 상판의 다리 수: 가운데 1개 또는 양쪽 2개만 (3~5개는 어수선해서 제거)</summary>
        public static int PedestalLegs(int level) => level < 20 ? 1 : level % 4 == 1 ? 2 : 1;
        public static string MotionName(MotionKind k) => k switch { MotionKind.Spin => "회전", MotionKind.Bob => "승강", MotionKind.SpinBob => "회전+승강", _ => "" };

        // ---------- 사거리(테이블 거리) ----------
        /// <summary>테이블 거리 단계별 z 오프셋: 단거리(지금) · 중거리 · 장거리. 카메라·대포는 그대로, 구조물만 뒤로 간다.</summary>
        public static readonly float[] RangeZ = { 0f, 2.5f, 5f };
        public static readonly string[] RangeName = { "단거리", "중거리", "장거리" };
        /// <summary>레벨별 사거리 단계(0~2). 1~5레벨은 단거리, 그 뒤로는 레벨마다 고정된 섞임.</summary>
        /// <summary>디버그·테스트용: 0 이상이면 모든 레벨의 사거리 단계를 이 값으로 고정</summary>
        public static int RangeTierOverride = -1;
        /// <summary>사거리 섞기 사용 여부. 꺼두면 전부 단거리(구조물이 화면을 꽉 채우는 레퍼런스 구도). 켜면 6레벨부터 3단계가 섞인다.</summary>
        public const bool UseRangeTiers = false;
        public static int RangeTier(int level) => RangeTierOverride >= 0 ? RangeTierOverride : (!UseRangeTiers || level <= 5) ? 0 : (level + level / 3 + 2) % 3;
        /// <summary>멀수록 조준이 어려우니 시작 공 약간 추가</summary>
        public static int RangeExtraBalls(int tier) => tier * 3;
        /// <summary>구조물별 시작 공 보정. 얼음 젠가(14)는 부재 수가 적어 블록 비례 공이 적게 나오는데 실제로는 한 층씩 밀어내야 해서 더 준다 (봇: 20발에 딱 클리어)</summary>
        public static int StructureExtraBalls(int type) => 0;   // (얼음 젠가가 규격 블록 탑으로 바뀌어 보정 불필요)   // 멀수록 화면 맞춤으로 블록이 커져(무거워져) 공을 더 준다

        public static int Grow(int level, int baseVal, int perLevels, int cap) => Mathf.Min(cap, baseVal + level / perLevels);
        /// <summary>새 구조물(피라미드·요새·성문·쌍둥이 탑·계단·원진)이 등장하는 레벨. 그 전엔 기본 6종만.</summary>
        public const int NewStructuresFromLevel = 8;
        public const int WideStructuresFromLevel = 20;   // 얼음 성문·통나무 다리·얼음 젠가 (긴 부재·넓은 구조)
        public const int EasyStructuresUntilLevel = 11;  // 원통 다발(0)·판자 선반(2)·통나무 탑(3)은 이 레벨까지만 (그 뒤엔 너무 쉽다)
        static readonly int[] PoolEarly = { 0, 1, 2, 3, 4, 5 };
        static readonly int[] PoolMid   = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };
        static readonly int[] PoolNoEasy = { 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29 };   // 12~19: 마스크 3종(창문 벽·계단 성·무늬 벽) 먼저 소개
        static readonly int[] PoolFull  = { 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75 };
        /// <summary>레벨에서 고를 수 있는 구조물 종류 목록</summary>
        public static int[] StructurePool(int level)
        {
            if (level < NewStructuresFromLevel) return PoolEarly;
            if (level <= EasyStructuresUntilLevel) return PoolMid;
            if (level < WideStructuresFromLevel) return PoolNoEasy;
            return PoolFull;
        }
        /// <summary>새 구조물이 두 겹(깊이 2)이 되는 레벨</summary>
        public const int DeepStructuresFromLevel = 60;   // 세 겹이 되는 레벨 (두 겹은 1레벨부터 기본)
        /// <summary>새 구조물의 바닥·기둥이 돌(무거움)로 바뀌는 레벨. 그 전엔 상자·원통.</summary>
        public const int HeavyStructuresFromLevel = 30;
        /// <summary>블록 전체 질량 배율. 기본 0.7(이전 0.6)에서 레벨당 +0.15%로 상한 없이 계속 무거워진다 (500L 1.75배, 1000L 2.5배, 2000L 4배).
        /// 시작 공은 이 배율을 뺀 "기준 질량"(BallRefMassScale 기준)으로 세므로, 무거워진 만큼이 그대로 난이도가 된다. 레벨은 무한이므로 상한을 두지 않는다.</summary>
        public const float BlockMassBase = 0.7f;
        public const float BlockMassGrowth = 0.0015f;
        public static float BlockMassScale(int level) => BlockMassBase * (1f + Mathf.Max(0, level - 1) * BlockMassGrowth);
        /// <summary>시작 공 계산의 기준 질량 배율 (TargetMassPerBall이 이 배율에서 튜닝됨). 실제 배율/기준 배율만큼 블록이 더 무겁고, 그만큼 어렵다.</summary>
        public const float BallRefMassScale = 0.6f;
        /// <summary>장애물 등장: 하드 레벨 전부 + 5레벨마다</summary>
        public static bool HasObstacle(int level) => IsHardLevel(level) || (level >= 4 && level % 5 == 2);

        /// <summary>권장 4스탯 합계: 경제 시뮬(하루 100스테이지, 제일 싼 스탯부터 강화)에서 나온 값의 근사 — 100L 84, 300L 140, 500L 172, 1000L 220, 2000L 280.</summary>
        public static int RecommendedStatSum(int level)
        {
            if (level < 8) return 4;
            return Mathf.RoundToInt(4f + 82f * Mathf.Log(1f + level / 60f));
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
