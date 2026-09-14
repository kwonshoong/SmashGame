using System;
using UnityEngine;

namespace SmashGame
{
    /// <summary>
    /// 훈련장(방치) 계산. 돼지들이 오프라인에 코인을 벌어 온다.
    /// 온라인(실시간 뷰)과 오프라인 모두 같은 시간당 생산량을 쓰고, 오프라인은 효율과 상한이 붙는다.
    /// </summary>
    public static class TrainingGround
    {
        public static bool IsUnlocked(SaveData d) => d.currentLevel > Balance.TrainingUnlockLevel;

        public static float CoinPerHour(SaveData d) => Balance.CoinPerHour(d) * Balance.DailyClearMult(d.clearedToday);

        public static float OfflineCapHours(SaveData d)
            => Balance.OfflineCapHours(d.trainingLevel, d.trainingChapter, Balance.HasPig(d.trainingLevel, 5), d.adFree);

        public static float OfflineEfficiency(SaveData d)
            => Balance.HasPig(d.trainingLevel, 4) ? Balance.OfflineEfficiencyGloria : Balance.OfflineEfficiencyBase;

        /// <summary>마지막 수령 이후 쌓인 코인(상한 적용). 저장하지 않고 계산만 한다.</summary>
        public static float PendingCoins(SaveData d)
        {
            if (!IsUnlocked(d)) return 0f;
            double hours = (DateTime.UtcNow - new DateTime(d.lastCollectTicks, DateTimeKind.Utc)).TotalHours;
            if (hours < 0) hours = 0;
            float cap = OfflineCapHours(d);
            float effHours = (float)Math.Min(hours, cap);
            return d.storedCoins + CoinPerHour(d) * effHours * OfflineEfficiency(d);
        }

        public static float ElapsedHours(SaveData d)
        {
            double hours = (DateTime.UtcNow - new DateTime(d.lastCollectTicks, DateTimeKind.Utc)).TotalHours;
            return (float)Math.Max(0, hours);
        }

        public static bool IsCapped(SaveData d) => ElapsedHours(d) >= OfflineCapHours(d);

        /// <summary>수령. 배율(광고 2배 등)을 곱해 코인에 넣고 타이머를 리셋한다.</summary>
        public static int Collect(SaveData d, float multiplier = 1f)
        {
            int amount = Mathf.FloorToInt(PendingCoins(d) * multiplier);
            d.coins += amount;
            d.storedCoins = 0f;
            d.lastCollectTicks = DateTime.UtcNow.Ticks;
            return amount;
        }

        /// <summary>실시간 뷰에서 플레이어가 화면을 탭했을 때</summary>
        public static int TapBonus(SaveData d)
        {
            int v = Balance.HasPig(d.trainingLevel, 3) ? 2 : 1; // Punk
            d.storedCoins += v;
            return v;
        }

        public static bool CanUpgrade(SaveData d)
            => d.trainingLevel < Balance.TrainingMaxLevel && d.coins >= Balance.TrainingUpgradeCost(d.trainingLevel);

        public static bool Upgrade(SaveData d)
        {
            if (!CanUpgrade(d)) return false;
            d.coins -= Balance.TrainingUpgradeCost(d.trainingLevel);
            d.trainingLevel++;
            return true;
        }

        public static int NextChapter(SaveData d) => Mathf.Min(d.trainingChapter + 1, Balance.ChapterNames.Length - 1);
        public static bool HasNextChapter(SaveData d) => d.trainingChapter < Balance.ChapterNames.Length - 1;

        public static bool CanUnlockChapter(SaveData d)
        {
            if (!HasNextChapter(d)) return false;
            int next = d.trainingChapter + 1;
            return d.currentLevel > Balance.ChapterUnlockLevel[next] && d.coins >= Balance.ChapterUnlockCoin[next];
        }

        public static bool UnlockChapter(SaveData d)
        {
            if (!CanUnlockChapter(d)) return false;
            int next = d.trainingChapter + 1;
            d.coins -= Balance.ChapterUnlockCoin[next];
            d.trainingChapter = next;
            return true;
        }
    }
}
