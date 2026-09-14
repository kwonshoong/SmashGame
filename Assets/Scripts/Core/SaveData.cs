using System;
using UnityEngine;

namespace SmashGame
{
    [Serializable]
    public class SaveData
    {
        public int coins = 0;
        public int gems = 0;
        public int currentLevel = 1;          // 다음에 플레이할 레벨
        public int winStreak = 0;
        public int trackProgress = 0;         // 20레벨 트랙

        // 공 스탯
        public int powerLv = 1;
        public int sizeLv = 1;
        public int massLv = 1;
        public int ammoLv = 1;

        // 훈련장
        public int trainingLevel = 1;
        public int trainingChapter = 0;
        public long lastCollectTicks = 0;     // 마지막 수령 시각 (UTC ticks)
        public float storedCoins = 0f;        // 누적 미수령 코인 (실시간 뷰용)
        public int clearedToday = 0;
        public string clearedDay = "";

        // 격파 도전
        public int towerStage = 1;            // 현재 도전 단계
        public int towerBest = 0;             // 돌파한 최고 단계

        public bool forgeGiftGiven = false;
        public bool adFree = false;

        public int StatSum => (powerLv - 1) + (sizeLv - 1) + (massLv - 1) + (ammoLv - 1);

        const string Key = "SmashGame.Save.v1";

        public static SaveData Load()
        {
            try
            {
                if (PlayerPrefs.HasKey(Key))
                {
                    var d = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(Key));
                    if (d != null) { d.RefreshDay(); return d; }
                }
            }
            catch (Exception e) { Debug.LogWarning("Save load failed: " + e.Message); }
            var fresh = new SaveData { lastCollectTicks = DateTime.UtcNow.Ticks };
            fresh.RefreshDay();
            return fresh;
        }

        public void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        public void RefreshDay()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (clearedDay != today) { clearedDay = today; clearedToday = 0; }
        }

        public static void Reset()
        {
            PlayerPrefs.DeleteKey(Key);
        }
    }
}
