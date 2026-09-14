using System;
using UnityEngine;

namespace SmashGame
{
    public enum GameState { Lobby, Playing, Result }
    public enum StatType { Power, Size, Mass, Ammo }

    /// <summary>
    /// 게임 전체 상태와 경제를 관리하는 싱글턴. 씬에 아무것도 없어도 Bootstrap이 생성한다.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }

        public SaveData Data { get; private set; }
        public GameState State { get; private set; } = GameState.Lobby;
        public LevelController Level { get; private set; }
        public UIManager UI { get; private set; }

        public event Action OnDataChanged;

        [Header("Runtime")]
        public Transform levelRoot;
        public Camera mainCamera;

        // 결과 화면용
        public struct ResultInfo
        {
            public bool won;
            public int level;
            public int clearCoin, refundCoin, perfectCoin, trackCoin, total;
            public int remainingBalls, perfects;
            public bool trackCompleted;
        }
        public ResultInfo LastResult;

        // 카메라 포커스: 패널(대장간·훈련장)이 열리면 카메라를 올려 3D 뷰가 화면 위쪽에 보이게 한다
        Vector3 camTargetPos; Quaternion camTargetRot; bool camLerp;
        public static readonly Vector3 CamDefaultPos = new Vector3(0f, 2.9f, -9.5f);
        public static readonly Quaternion CamDefaultRot = Quaternion.Euler(-3f, 0f, 0f);
        public static readonly Vector3 CamPanelPos = new Vector3(0f, 4.0f, -13f);   // 뒤로 빠져 대포·받침대·구조물이 한 화면에
        public static readonly Quaternion CamPanelRot = Quaternion.Euler(8f, 0f, 0f);

        public void SetCameraFocus(bool panelOpen)
        {
            camTargetPos = panelOpen ? CamPanelPos : CamDefaultPos;
            camTargetRot = panelOpen ? CamPanelRot : CamDefaultRot;
            camLerp = true;
        }

        void Update()
        {
            if (!camLerp || mainCamera == null) return;
            var t = mainCamera.transform;
            t.position = Vector3.Lerp(t.position, camTargetPos, Time.unscaledDeltaTime * 7f);
            t.rotation = Quaternion.Slerp(t.rotation, camTargetRot, Time.unscaledDeltaTime * 7f);
            if ((t.position - camTargetPos).sqrMagnitude < 0.0001f && Quaternion.Angle(t.rotation, camTargetRot) < 0.05f) camLerp = false;
        }

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
            DontDestroyOnLoad(gameObject);
            Data = SaveData.Load();
            Application.targetFrameRate = 60;
            // 씬 스케일이 작아(블록 0.5유닛) 실제 중력(9.81)은 둥둥 떠 보인다. 캐주얼 물리 게임 관례대로 중력을 키우고 물리 스텝을 촘촘하게.
            Physics.gravity = new Vector3(0f, -9.81f * Balance.GravityScale, 0f);
            Time.fixedDeltaTime = 1f / 90f;
        }

        void Start()
        {
            EnsureCamera();
            levelRoot = new GameObject("LevelRoot").transform;
            levelRoot.SetParent(transform);
            UI = UIManager.Create(this);
            EnterLobby();
        }

        void EnsureCamera()
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                mainCamera = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.fieldOfView = 60f;
            mainCamera.nearClipPlane = 0.1f;
            mainCamera.farClipPlane = 200f;
            if (FindFirstObjectByType<Light>() == null)
            {
                var lgo = new GameObject("Sun");
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Directional;
                l.intensity = 1.1f;
                l.color = new Color(1f, 0.97f, 0.9f);
                lgo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                l.shadows = LightShadows.Soft;
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.6f, 0.7f);
        }

        // ---------------- 상태 전환 ----------------

        public void EnterLobby()
        {
            State = GameState.Lobby;
            ClearLevel();
            LevelBuilder.BuildLobbyBackdrop(levelRoot, mainCamera, Data);
            SetCameraFocus(false);
            UI.ShowLobby();
            Data.Save();
            OnDataChanged?.Invoke();
        }

        public void StartLevel()
        {
            State = GameState.Playing;
            ClearLevel();
            var go = new GameObject("LevelController");
            go.transform.SetParent(levelRoot);
            Level = go.AddComponent<LevelController>();
            Level.Init(this, Data.currentLevel);
            camLerp = false;
            UI.ShowHUD();
            OnDataChanged?.Invoke();
        }

        public void RetryLevel() => StartLevel();

        void ClearLevel()
        {
            // 즉시 제거: 같은 프레임에 새 구조물을 같은 자리에 만들기 때문에 이전 콜라이더가 남아 있으면 물리가 튄다
            if (Level != null) { DestroyImmediate(Level.gameObject); Level = null; }
            for (int i = levelRoot.childCount - 1; i >= 0; i--) DestroyImmediate(levelRoot.GetChild(i).gameObject);
            Time.timeScale = 1f;
        }

        /// <summary>레벨 클리어. 기획서 3.5 남은 공 환급, 3.6 퍼펙트 보너스, 20레벨 트랙.</summary>
        public void OnLevelWon(int remainingBalls, int perfects)
        {
            State = GameState.Result;
            int lvl = Data.currentLevel;
            var r = new ResultInfo { won = true, level = lvl, remainingBalls = remainingBalls, perfects = perfects };
            r.clearCoin = Balance.ClearCoin(lvl);
            r.refundCoin = lvl >= Balance.RefundUnlockLevel ? remainingBalls * Balance.RefundPerBall : 0;
            r.perfectCoin = perfects * Balance.PerfectBonus;

            Data.trackProgress++;
            if (Data.trackProgress >= Balance.TrackLevels)
            {
                Data.trackProgress = 0;
                r.trackCoin = Balance.TrackReward;
                r.trackCompleted = true;
            }
            r.total = r.clearCoin + r.refundCoin + r.perfectCoin + r.trackCoin;
            Data.coins += r.total;
            Data.winStreak++;
            Data.RefreshDay();
            Data.clearedToday++;
            Data.currentLevel++;

            // 온보딩: 대장간 해금 시 코인 300 지급
            if (!Data.forgeGiftGiven && Data.currentLevel > Balance.ForgeUnlockLevel)
            {
                Data.forgeGiftGiven = true;
                Data.coins += Balance.ForgeGiftCoins;
            }

            LastResult = r;
            Data.Save();
            UI.ShowResult(r);
            OnDataChanged?.Invoke();
        }

        public void OnLevelLost()
        {
            State = GameState.Result;
            Data.winStreak = 0;
            LastResult = new ResultInfo { won = false, level = Data.currentLevel };
            Data.Save();
            UI.ShowResult(LastResult);
            OnDataChanged?.Invoke();
        }

        // ---------------- 대장간 ----------------

        public bool IsForgeUnlocked => Data.currentLevel > Balance.ForgeUnlockLevel;

        public int GetStatLevel(StatType t) => t switch
        {
            StatType.Power => Data.powerLv,
            StatType.Size => Data.sizeLv,
            StatType.Mass => Data.massLv,
            _ => Data.ammoLv,
        };

        public int GetUpgradeCost(StatType t) => Balance.StatUpgradeCost(GetStatLevel(t));

        public bool CanUpgrade(StatType t)
        {
            int lv = GetStatLevel(t);
            return lv < Balance.StatMaxLevel && Data.coins >= Balance.StatUpgradeCost(lv);
        }

        public bool UpgradeStat(StatType t)
        {
            if (!CanUpgrade(t)) return false;
            Data.coins -= GetUpgradeCost(t);
            switch (t)
            {
                case StatType.Power: Data.powerLv++; break;
                case StatType.Size: Data.sizeLv++; break;
                case StatType.Mass: Data.massLv++; break;
                case StatType.Ammo: Data.ammoLv++; break;
            }
            Data.Save();
            OnDataChanged?.Invoke();
            return true;
        }

        /// <summary>현재 공의 실제 스탯</summary>
        public BallStats CurrentBallStats() => new BallStats
        {
            power = Balance.PowerMult(Data.powerLv),
            size = Balance.SizeMult(Data.sizeLv),
            mass = Balance.MassMult(Data.massLv),
            ammoBonus = Balance.AmmoBonus(Data.ammoLv),
            star = Balance.StarRank((Data.powerLv + Data.sizeLv + Data.massLv + Data.ammoLv) / 4),
        };

        // ---------------- 훈련장 ----------------

        public int CollectTraining(float mult = 1f)
        {
            int v = TrainingGround.Collect(Data, mult);
            Data.Save();
            OnDataChanged?.Invoke();
            return v;
        }

        /// <summary>훈련장 구조물 전부 파괴 보상</summary>
        public int OnTrainingStructureCleared()
        {
            int v = Balance.TrainingSmashReward(Data);
            Data.coins += v;
            Data.Save();
            OnDataChanged?.Invoke();
            UI?.Toast($"구조물 파괴!  +{v} 코인");
            return v;
        }

        public void NotifyDataChanged() { Data.Save(); OnDataChanged?.Invoke(); }

        public void ResetSave()
        {
            SaveData.Reset();
            Data = SaveData.Load();
            EnterLobby();
        }

        void OnApplicationPause(bool pause) { if (pause && Data != null) Data.Save(); }
        void OnApplicationQuit() { if (Data != null) Data.Save(); }
    }

    public struct BallStats
    {
        public float power;
        public float size;
        public float mass;
        public int ammoBonus;
        public int star;
    }
}
