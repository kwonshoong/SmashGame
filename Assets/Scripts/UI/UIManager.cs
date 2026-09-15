using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SmashGame
{
    /// <summary>
    /// 로비 · HUD · 결과 · 대장간 · 훈련장 5개 화면을 코드로 조립한다.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        GameManager gm;
        Canvas canvas;

        RectTransform lobby, hud, result, forge, training;

        // 로비
        Text lobbyCoins, lobbyLevelInfo, lobbyTrainingBadge, lobbyForgeBadge, lobbyStreak, lobbyTowerBadge;
        Button towerBtn;
        Button playBtn;

        // HUD
        Text hudBalls, hudBlocks, hudBallsTitle;

        // 결과
        Text resultTitle, resultBody;
        Button resultMain, resultSub;

        // 대장간
        Text forgeCoins, forgeSummary;
        readonly Text[] forgeStatLabels = new Text[4];
        readonly Button[] forgeStatBtns = new Button[4];

        // 훈련장
        Text trainCoins, trainInfo, trainChapter, trainGaugeText;
        Button collectBtn, collect2xBtn, trainUpBtn, chapterBtn;
        RectTransform trainGauge;

        public static UIManager Create(GameManager manager)
        {
            var go = new GameObject("UI");
            go.transform.SetParent(manager.transform);
            var ui = go.AddComponent<UIManager>();
            ui.gm = manager;
            ui.Build();
            manager.OnDataChanged += ui.RefreshAll;
            return ui;
        }

        void Build()
        {
            canvas = UIKit.CreateCanvas("Canvas");
            canvas.transform.SetParent(transform);
            BuildLobby();
            BuildHUD();
            BuildResult();
            BuildForge();
            BuildTraining();
            HideAll();
        }

        void HideAll()
        {
            lobby.gameObject.SetActive(false);
            hud.gameObject.SetActive(false);
            result.gameObject.SetActive(false);
            forge.gameObject.SetActive(false);
            training.gameObject.SetActive(false);
        }

        // ======================= 로비 =======================

        void BuildLobby()
        {
            lobby = UIKit.FullPanel(canvas.transform, "Lobby", new Color(0, 0, 0, 0));
            lobby.GetComponent<Image>().raycastTarget = false;

            var top = UIKit.Panel(lobby, "TopBar", UIKit.Bar, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -150), new Vector2(0, 0));
            UIKit.Label(top, "ROYAL SMASH", 44, UIKit.Gold, new Vector2(0, 0.5f), new Vector2(30, 0), new Vector2(400, 80), TextAnchor.MiddleLeft, true);
            lobbyCoins = UIKit.Label(top, "", 40, Color.white, new Vector2(1, 0.5f), new Vector2(-30, 0), new Vector2(400, 80), TextAnchor.MiddleRight, true);

            lobbyLevelInfo = UIKit.Label(lobby, "", 34, Color.white, new Vector2(0.5f, 1), new Vector2(0, -200), new Vector2(900, 120), TextAnchor.UpperCenter);
            lobbyStreak = UIKit.Label(lobby, "", 30, UIKit.Gold, new Vector2(0.5f, 1), new Vector2(0, -320), new Vector2(900, 60), TextAnchor.UpperCenter);

            playBtn = UIKit.Button(lobby, "레벨 1", UIKit.Green, new Vector2(0.5f, 0), new Vector2(0, 300), new Vector2(560, 150), () => gm.StartLevel(), 54);

            // 하단 탭: 대장간 · 격파 도전 · 훈련장  (초기화는 상단 바로)
            var bottom = UIKit.Panel(lobby, "BottomBar", UIKit.Bar, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 200));
            var forgeBtn = UIKit.Button(bottom, "대장간", UIKit.Purple, new Vector2(0, 0.5f), new Vector2(30, 0), new Vector2(320, 150), OpenForge, 40);
            lobbyForgeBadge = UIKit.Label(forgeBtn.transform, "", 26, UIKit.Gold, new Vector2(1, 1), new Vector2(-6, -6), new Vector2(200, 40), TextAnchor.UpperRight, true);
            towerBtn = UIKit.Button(bottom, "격파 도전", UIKit.Orange, new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(340, 150), OpenTower, 40);
            lobbyTowerBadge = UIKit.Label(towerBtn.transform, "", 26, UIKit.Gold, new Vector2(1, 1), new Vector2(-6, -6), new Vector2(240, 40), TextAnchor.UpperRight, true);
            var trainBtn = UIKit.Button(bottom, "훈련장", UIKit.Blue, new Vector2(1, 0.5f), new Vector2(-30, 0), new Vector2(320, 150), OpenTraining, 40);
            lobbyTrainingBadge = UIKit.Label(trainBtn.transform, "", 26, UIKit.Gold, new Vector2(1, 1), new Vector2(-6, -6), new Vector2(240, 40), TextAnchor.UpperRight, true);
            UIKit.Button(top, "초기화", new Color(0.4f, 0.4f, 0.4f), new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(150, 64), () => gm.ResetSave(), 24);
        }

        public void ShowLobby()
        {
            HideAll();
            lobby.gameObject.SetActive(true);
            RefreshLobby();
        }

        void RefreshLobby()
        {
            var d = gm.Data;
            lobbyCoins.text = $"코인 {d.coins:N0}";
            bool hard = Balance.IsHardLevel(d.currentLevel);
            UIKit.SetButtonLabel(playBtn, hard ? $"하드!  레벨 {d.currentLevel}" : $"레벨 {d.currentLevel}");
            playBtn.GetComponent<Image>().color = hard ? UIKit.Blue : UIKit.Green;

            var s = gm.CurrentBallStats();
            string stars = new string('★', s.star);
            lobbyLevelInfo.text = $"{stars}  공 파괴력 {s.power * 100:0}%  크기 {s.size * 100:0}%  탄약 +{s.ammoBonus}\n" +
                                  $"20레벨 트랙 {d.trackProgress}/{Balance.TrackLevels}  ·  테마: {LevelBuilder.ThemeFor(d.currentLevel)}";
            lobbyStreak.text = d.winStreak > 0 ? $"연승 {d.winStreak}  (+3 공 보너스)" : "";

            // 대장간 배지: 해금 전 / 권장치 미달
            if (!gm.IsForgeUnlocked) lobbyForgeBadge.text = $"Lv{Balance.ForgeUnlockLevel + 1} 해금";
            else lobbyForgeBadge.text = d.StatSum < Balance.RecommendedStatSum(d.currentLevel) ? "강화 권장!" : "";

            if (!gm.IsTowerUnlocked) lobbyTowerBadge.text = $"Lv{Balance.TowerUnlockLevel + 1} 해금";
            else lobbyTowerBadge.text = $"{d.towerStage}단계";

            if (!TrainingGround.IsUnlocked(d)) lobbyTrainingBadge.text = $"Lv{Balance.TrainingUnlockLevel + 1} 해금";
            else
            {
                int pending = Mathf.FloorToInt(TrainingGround.PendingCoins(d));
                lobbyTrainingBadge.text = pending > 0 ? $"+{pending} 코인" + (TrainingGround.IsCapped(d) ? " (가득)" : "") : "";
            }
        }

        // ======================= HUD =======================

        void BuildHUD()
        {
            hud = UIKit.FullPanel(canvas.transform, "HUD", new Color(0, 0, 0, 0));
            hud.GetComponent<Image>().raycastTarget = false;

            var ballBox = UIKit.Box(hud, "BallBox", UIKit.Red, new Vector2(0, 1), new Vector2(30, -40), new Vector2(230, 170));
            hudBallsTitle = UIKit.Label(ballBox, "남은 공", 28, Color.white, new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(220, 40));
            hudBalls = UIKit.Label(ballBox, "30", 76, Color.white, new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(220, 110), TextAnchor.MiddleCenter, true);

            hudBlocks = UIKit.Label(hud, "", 30, Color.white, new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(560, 90));
            UIKit.Button(hud, "나가기", new Color(0.5f, 0.15f, 0.3f), new Vector2(1, 1), new Vector2(-30, -40), new Vector2(200, 90), () =>
            {
                if (gm.Level != null) gm.Level.Abort();
                gm.EnterLobby();
            }, 30);

        }

        public void ShowHUD()
        {
            HideAll();
            hud.gameObject.SetActive(true);
            if (gm.Level != null)
            {
                gm.Level.OnHudChanged += RefreshHUD;
            }
            RefreshHUD();
        }

        void RefreshHUD()
        {
            if (gm.Level == null) return;
            if (gm.Level.IsTimed)
            {
                hudBallsTitle.text = "남은 시간";
                hudBalls.text = Mathf.CeilToInt(gm.Level.BonusTimeLeft).ToString();
                int total = gm.Level.BlocksLeft + gm.Level.BlocksDestroyed;
                hudBlocks.text = gm.Level.IsTower
                    ? $"격파 도전 {gm.Level.Info.towerStage}단계  ·  {gm.Level.BlocksDestroyed}/{total}"
                    : $"보너스!  자동차 부수기  ·  {gm.Level.BlocksDestroyed}/{total}";
            }
            else
            {
                hudBallsTitle.text = "남은 공";
                hudBalls.text = gm.Level.BallsLeft.ToString();
                hudBlocks.text = $"레벨 {gm.Level.Level} · {gm.Level.Info.structureName}" + (gm.Level.Info.rangeTier > 0 ? $" · {Balance.RangeName[gm.Level.Info.rangeTier]}" : "") + $"\n블록 {gm.Level.BlocksLeft}";
            }
        }

        // ======================= 결과 =======================

        void BuildResult()
        {
            result = UIKit.FullPanel(canvas.transform, "Result", UIKit.Overlay);
            var card = UIKit.Box(result, "Card", UIKit.PanelDark, new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(880, 900));
            resultTitle = UIKit.Label(card, "잘했어요!", 80, UIKit.Gold, new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(800, 120), TextAnchor.MiddleCenter, true);
            resultBody = UIKit.Label(card, "", 36, Color.white, new Vector2(0.5f, 1), new Vector2(0, -180), new Vector2(760, 520), TextAnchor.UpperLeft);
            resultMain = UIKit.Button(card, "계속하기", UIKit.Green, new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(520, 130), null, 44);
            resultSub = UIKit.Button(card, "로비로", new Color(0.4f, 0.4f, 0.5f), new Vector2(0.5f, 0), new Vector2(0, 190), new Vector2(400, 90), () => gm.EnterLobby(), 32);
        }

        public void ShowResult(GameManager.ResultInfo r)
        {
            HideAll();
            result.gameObject.SetActive(true);
            resultMain.onClick.RemoveAllListeners();
            if (r.tower)
            {
                var d = gm.Data;
                resultTitle.text = r.towerCleared ? $"{r.towerStage}단계 격파!" : "격파 실패";
                string body = $"격파 도전 {r.towerStage}단계\n\n";
                body += $"부순 블록 {r.destroyed}/{r.totalBlocks}     +{r.clearCoin}\n";
                if (r.towerCleared) body += $"단계 돌파 보너스        +{r.trackCoin}\n";
                body += $"\n합계  +{r.total} 코인      (보유 {d.coins:N0})\n";
                if (r.towerCleared) body += $"\n다음 단계: 블록 질량 ×{Balance.TowerMassMult(d.towerStage):0.0}  ·  권장 파괴력 {Balance.TowerRecommendedPower(d.towerStage)}%";
                else body += $"\n이 단계 블록 질량 ×{Balance.TowerMassMult(r.towerStage):0.0}  ·  권장 파괴력 {Balance.TowerRecommendedPower(r.towerStage)}%\n대장간에서 파괴력·무게를 올리면 블록이 더 잘 밀립니다.";
                resultBody.text = body;
                UIKit.SetButtonLabel(resultMain, r.towerCleared ? $"{d.towerStage}단계 도전" : "다시 도전");
                resultMain.GetComponent<Image>().color = r.towerCleared ? UIKit.Green : UIKit.Orange;
                resultMain.onClick.AddListener(() => gm.StartTowerRush());
            }
            else if (r.bonus)
            {
                var d = gm.Data;
                resultTitle.text = r.destroyed >= r.totalBlocks ? "완전 파괴!" : "보너스 종료!";
                string body = $"보너스 스테이지 (레벨 {r.level})\n\n";
                body += $"부순 블록 {r.destroyed}/{r.totalBlocks}     +{r.clearCoin}\n";
                if (r.trackCoin > 0) body += $"완전 파괴 보너스        +{r.trackCoin}\n";
                body += $"\n합계  +{r.total} 코인      (보유 {d.coins:N0})";
                resultBody.text = body;
                UIKit.SetButtonLabel(resultMain, $"계속하기 (레벨 {d.currentLevel})");
                resultMain.GetComponent<Image>().color = UIKit.Green;
                resultMain.onClick.AddListener(() => gm.StartLevel());
            }
            else if (r.won)
            {
                resultTitle.text = "잘했어요!";
                var d = gm.Data;
                string body = $"레벨 {r.level} 클리어\n\n";
                body += $"클리어 보상          +{r.clearCoin}\n";
                if (r.level >= Balance.RefundUnlockLevel) body += $"남은 공 {r.remainingBalls}개 환급     +{r.refundCoin}\n";
                else body += $"남은 공 {r.remainingBalls}개 (환급은 레벨 {Balance.RefundUnlockLevel}부터)\n";
                if (r.trackCompleted) body += $"20레벨 트랙 완료!      +{r.trackCoin}\n";
                body += $"\n합계  +{r.total} 코인      (보유 {d.coins:N0})\n";
                body += $"트랙 {d.trackProgress}/{Balance.TrackLevels}";
                if (d.currentLevel - 1 == Balance.ForgeUnlockLevel) body += $"\n\n대장간이 열렸습니다! 코인 {Balance.ForgeGiftCoins} 지급";
                if (d.currentLevel - 1 == Balance.TrainingUnlockLevel) body += "\n\n훈련장이 열렸습니다! Rocky가 합류합니다";
                resultBody.text = body;
                UIKit.SetButtonLabel(resultMain, Balance.IsHardLevel(d.currentLevel) ? $"하드! 레벨 {d.currentLevel}" : $"계속하기 (레벨 {d.currentLevel})");
                resultMain.GetComponent<Image>().color = UIKit.Green;
                resultMain.onClick.AddListener(() => gm.StartLevel());
            }
            else
            {
                resultTitle.text = "아쉬워요";
                resultBody.text = $"레벨 {r.level} 실패\n\n공을 다 썼는데 블록이 남았습니다.\n대장간에서 공을 강화하면 한 발에 더 많이 무너집니다.\n\n연승이 끊겼습니다.";
                UIKit.SetButtonLabel(resultMain, "다시 도전");
                resultMain.GetComponent<Image>().color = UIKit.Orange;
                resultMain.onClick.AddListener(() => gm.RetryLevel());
            }
        }

        // ======================= 격파 도전 =======================

        void OpenTower()
        {
            if (!gm.IsTowerUnlocked) { Toast($"격파 도전은 레벨 {Balance.TowerUnlockLevel} 클리어 후 열립니다"); return; }
            int st = gm.Data.towerStage;
            Toast($"격파 도전 {st}단계  ·  {Balance.TowerSeconds:0}초 무제한 발사  ·  권장 파괴력 {Balance.TowerRecommendedPower(st)}%");
            gm.StartTowerRush();
        }

        // ======================= 대장간 =======================

        static readonly string[] StatNames = { "파괴력", "크기", "무게", "탄약" };
        static readonly StatType[] StatOrder = { StatType.Power, StatType.Size, StatType.Mass, StatType.Ammo };

        void BuildForge()
        {
            forge = UIKit.FullPanel(canvas.transform, "Forge", new Color(0, 0, 0, 0f));
            forge.GetComponent<Image>().raycastTarget = false;
            var card = UIKit.Panel(forge, "Card", UIKit.PanelDark, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 800));

            // 제목 줄
            UIKit.Label(card, "대장간", 44, UIKit.Gold, new Vector2(0, 1), new Vector2(30, -18), new Vector2(300, 60), TextAnchor.MiddleLeft, true);
            forgeCoins = UIKit.Label(card, "", 32, Color.white, new Vector2(1, 1), new Vector2(-30, -18), new Vector2(400, 60), TextAnchor.MiddleRight, true);
            forgeSummary = UIKit.Label(card, "", 26, new Color(0.9f, 0.9f, 1f), new Vector2(0.5f, 1), new Vector2(0, -84), new Vector2(1020, 70), TextAnchor.UpperCenter);

            // 스탯 4줄
            for (int i = 0; i < 4; i++)
            {
                float y = -160 - i * 112;
                var row = UIKit.Box(card, "Row" + i, new Color(1, 1, 1, 0.08f), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(1020, 100));
                forgeStatLabels[i] = UIKit.Label(row, "", 30, Color.white, new Vector2(0, 0.5f), new Vector2(24, 0), new Vector2(680, 96), TextAnchor.MiddleLeft);
                int idx = i;
                forgeStatBtns[i] = UIKit.Button(row, "강화", UIKit.Orange, new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(280, 84), () => OnUpgrade(idx), 28);
            }

            // 하단 버튼
            UIKit.Button(card, "시험 발사", UIKit.Blue, new Vector2(0.5f, 0), new Vector2(-270, 30), new Vector2(480, 100), TestFire, 34);
            UIKit.Button(card, "닫기", new Color(0.45f, 0.45f, 0.5f), new Vector2(0.5f, 0), new Vector2(270, 30), new Vector2(480, 100), ClosePanels, 34);
        }

        void ClosePanels()
        {
            forge.gameObject.SetActive(false);
            training.gameObject.SetActive(false);
            var range = FindFirstObjectByType<LevelBuilder.TestRange>();
            if (range != null) range.SetAutoFire(false);
            lobby.gameObject.SetActive(true);
            gm.SetCameraFocus(false);
            RefreshLobby();
        }

        void OpenForge()
        {
            if (!gm.IsForgeUnlocked) { Toast($"대장간은 레벨 {Balance.ForgeUnlockLevel} 클리어 후 열립니다"); return; }
            lobby.gameObject.SetActive(false);
            forge.gameObject.SetActive(true);
            gm.SetCameraFocus(true);
            RefreshForge();
        }

        void RefreshForge()
        {
            var d = gm.Data;
            var s = gm.CurrentBallStats();
            forgeCoins.text = $"코인 {d.coins:N0}";
            forgeSummary.text = $"{new string('★', s.star)}  파괴력 {s.power * 100:0}%  ·  크기 {s.size * 100:0}%  ·  무게 {s.mass * 100:0}%  ·  탄약 +{s.ammoBonus}\n" +
                                $"권장 스탯 합계 {Balance.RecommendedStatSum(d.currentLevel)}  /  현재 {d.StatSum}";
            for (int i = 0; i < 4; i++)
            {
                var t = StatOrder[i];
                int lv = gm.GetStatLevel(t);
                string val = t switch
                {
                    StatType.Power => $"{Balance.PowerMult(lv) * 100:0}% → {Balance.PowerMult(Mathf.Min(lv + 1, Balance.StatMaxLevel)) * 100:0}%",
                    StatType.Size => $"{Balance.SizeMult(lv) * 100:0}% → {Balance.SizeMult(Mathf.Min(lv + 1, Balance.StatMaxLevel)) * 100:0}%",
                    StatType.Mass => $"{Balance.MassMult(lv) * 100:0}% → {Balance.MassMult(Mathf.Min(lv + 1, Balance.StatMaxLevel)) * 100:0}%",
                    _ => $"+{Balance.AmmoBonus(lv)}발 → +{Balance.AmmoBonus(Mathf.Min(lv + 1, Balance.StatMaxLevel))}발",
                };
                forgeStatLabels[i].text = $"{StatNames[i]}  Lv {lv}/{Balance.StatMaxLevel}   <size=24>{val}</size>";
                forgeStatLabels[i].supportRichText = true;
                bool max = lv >= Balance.StatMaxLevel;
                UIKit.SetButtonLabel(forgeStatBtns[i], max ? "MAX" : $"강화\n{gm.GetUpgradeCost(t):N0}");
                forgeStatBtns[i].interactable = gm.CanUpgrade(t);
            }
        }

        void OnUpgrade(int idx)
        {
            var t = StatOrder[idx];
            if (gm.UpgradeStat(t))
            {
                RefreshForge();
                var range = FindFirstObjectByType<LevelBuilder.TestRange>();
                if (range != null) range.RefreshStats();
                int lv = gm.GetStatLevel(t);
                if (lv % 10 == 1 && lv > 1) Toast($"★ 별 승급!  {StatNames[idx]} {lv - 1}레벨 달성");
            }
        }

        void TestFire()
        {
            var range = FindFirstObjectByType<LevelBuilder.TestRange>();
            if (range != null) range.TestFire();
        }

        // ======================= 훈련장 =======================

        void BuildTraining()
        {
            training = UIKit.FullPanel(canvas.transform, "Training", new Color(0, 0, 0, 0f));
            // 헤더·카드 바깥(3D 뷰) 탭 = 직접 한 발 발사 + 코인
            var tapArea = training.gameObject.AddComponent<Button>();
            tapArea.transition = Selectable.Transition.None;
            tapArea.onClick.AddListener(() =>
            {
                if (!TrainingGround.IsUnlocked(gm.Data)) return;
                TrainingGround.TapBonus(gm.Data);
                var range = FindFirstObjectByType<LevelBuilder.TestRange>();
                if (range != null) range.TestFire();
                RefreshTraining();
            });

            // ---- 상단 헤더: 제목 · 코인 · 시간당 생산 · 오프라인 효율 ----
            var header = UIKit.Panel(training, "Header", UIKit.PanelDark, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -260), new Vector2(0, 0));
            var headerBlock = header.gameObject.AddComponent<Button>();
            headerBlock.transition = Selectable.Transition.None;
            trainChapter = UIKit.Label(header, "훈련장", 44, UIKit.Gold, new Vector2(0, 1), new Vector2(30, -18), new Vector2(600, 60), TextAnchor.MiddleLeft, true);
            trainCoins = UIKit.Label(header, "", 32, Color.white, new Vector2(1, 1), new Vector2(-30, -18), new Vector2(400, 60), TextAnchor.MiddleRight, true);
            trainInfo = UIKit.Label(header, "", 26, new Color(0.92f, 0.92f, 1f), new Vector2(0, 1), new Vector2(30, -86), new Vector2(1020, 170), TextAnchor.UpperLeft);
            UIKit.Label(training, "가운데를 탭하면 직접 한 발 (코인 +1)", 24, new Color(1, 1, 1, 0.8f), new Vector2(0.5f, 1), new Vector2(0, -280), new Vector2(700, 40));

            // ---- 하단 카드: 누적 게이지 · 버튼 ----
            var card = UIKit.Panel(training, "Card", UIKit.PanelDark, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 330));
            var blocker = card.gameObject.AddComponent<Button>(); // 카드 클릭이 탭 보너스로 새지 않게
            blocker.transition = Selectable.Transition.None;

            var gaugeBg = UIKit.Box(card, "GaugeBg", new Color(1, 1, 1, 0.12f), new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(1020, 56));
            trainGauge = UIKit.Panel(gaugeBg, "Fill", new Color(0.3f, 0.65f, 0.25f, 0.9f), Vector2.zero, new Vector2(0, 1), Vector2.zero, Vector2.zero);
            trainGaugeText = UIKit.FillLabel(gaugeBg, "", 28, Color.white, TextAnchor.MiddleCenter, true);

            collectBtn = UIKit.Button(card, "수령", UIKit.Green, new Vector2(0.5f, 0), new Vector2(-270, 140), new Vector2(480, 96), () => { int v = gm.CollectTraining(1f); Toast($"+{v} 코인 수령"); RefreshTraining(); }, 32);
            collect2xBtn = UIKit.Button(card, "광고 2배 수령", UIKit.Orange, new Vector2(0.5f, 0), new Vector2(270, 140), new Vector2(480, 96), () => { int v = gm.CollectTraining(2f); Toast($"+{v} 코인 (2배)"); RefreshTraining(); }, 30);
            trainUpBtn = UIKit.Button(card, "훈련장 레벨업", UIKit.Purple, new Vector2(0.5f, 0), new Vector2(-355, 30), new Vector2(310, 96), () => { if (TrainingGround.Upgrade(gm.Data)) { gm.NotifyDataChanged(); Toast("훈련장 레벨업!"); } RefreshTraining(); }, 26);
            chapterBtn = UIKit.Button(card, "다음 챕터", UIKit.Blue, new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(360, 96), () => { if (TrainingGround.UnlockChapter(gm.Data)) { gm.NotifyDataChanged(); gm.EnterLobby(); OpenTraining(); Toast("새 훈련장 해금!"); } }, 26);
            UIKit.Button(card, "닫기", new Color(0.45f, 0.45f, 0.5f), new Vector2(0.5f, 0), new Vector2(355, 30), new Vector2(310, 96), ClosePanels, 30);
        }

        static string NextPigHint(SaveData d)
        {
            for (int i = 0; i < Balance.PigNames.Length; i++)
                if (!Balance.HasPig(d.trainingLevel, i))
                    return $"\n다음 합류: {Balance.PigNames[i]} ({Balance.PigTraits[i]}) — 훈련장 Lv{Balance.PigJoinTrainingLevel[i]}";
            return "\n돼지 6명 전원 합류";
        }

        void OpenTraining()
        {
            if (!TrainingGround.IsUnlocked(gm.Data)) { Toast($"훈련장은 레벨 {Balance.TrainingUnlockLevel} 클리어 후 열립니다"); return; }
            lobby.gameObject.SetActive(false);
            training.gameObject.SetActive(true);
            gm.SetCameraFocus(true);
            var range = FindFirstObjectByType<LevelBuilder.TestRange>();
            if (range != null) range.SetAutoFire(true);
            RefreshTraining();
        }

        void RefreshTraining()
        {
            var d = gm.Data;
            trainCoins.text = $"코인 {d.coins:N0}";
            trainChapter.text = $"{Balance.ChapterNames[d.trainingChapter]}  Lv {d.trainingLevel}";
            float pending = TrainingGround.PendingCoins(d);
            float cap = TrainingGround.OfflineCapHours(d);
            float elapsed = TrainingGround.ElapsedHours(d);
            trainGauge.anchorMax = new Vector2(Mathf.Clamp01(elapsed / cap), 1f);
            trainGaugeText.text = $"쌓인 코인 {Mathf.FloorToInt(pending):N0}    ·    {elapsed:0.0}h / {cap:0}h{(TrainingGround.IsCapped(d) ? "  가득!" : "")}";
            int pigs = Balance.PigCount(d.trainingLevel) + Balance.ChapterExtraSlots(d.trainingChapter);
            trainInfo.text =
                $"시간당 획득  {TrainingGround.CoinPerHour(d):0.0} 코인\n" +
                $"  = 돼지 {pigs}명 × 파괴력 {Balance.PowerMult(d.powerLv) * 100:0}% × 훈련장 {Balance.TrainingLevelMult(d.trainingLevel):0.0} × 챕터 {Balance.ChapterMult[d.trainingChapter]:0.0} × 오늘 클리어 배율 {Balance.DailyClearMult(d.clearedToday) * 100:0}%\n" +
                $"오프라인 효율 {TrainingGround.OfflineEfficiency(d) * 100:0}%  ·  누적 상한 {cap:0}h  ·  오늘 {d.clearedToday}레벨 클리어 (5레벨이면 배율 100%)" + NextPigHint(d);

            collectBtn.interactable = pending >= 1f;
            collect2xBtn.interactable = pending >= 1f;
            bool maxLv = d.trainingLevel >= Balance.TrainingMaxLevel;
            UIKit.SetButtonLabel(trainUpBtn, maxLv ? "훈련장 MAX" : $"레벨업  {Balance.TrainingUpgradeCost(d.trainingLevel):N0}");
            trainUpBtn.interactable = TrainingGround.CanUpgrade(d);
            if (TrainingGround.HasNextChapter(d))
            {
                int n = d.trainingChapter + 1;
                UIKit.SetButtonLabel(chapterBtn, $"{Balance.ChapterNames[n]}  Lv{Balance.ChapterUnlockLevel[n]}+ · {Balance.ChapterUnlockCoin[n]:N0}");
                chapterBtn.interactable = TrainingGround.CanUnlockChapter(d);
            }
            else { UIKit.SetButtonLabel(chapterBtn, "마지막 챕터"); chapterBtn.interactable = false; }
        }

        // ======================= 공통 =======================

        void RefreshAll()
        {
            if (lobby.gameObject.activeSelf) RefreshLobby();
            if (forge.gameObject.activeSelf) RefreshForge();
            if (training.gameObject.activeSelf) RefreshTraining();
        }

        Text toast;
        Coroutine toastCo;
        public void Toast(string msg)
        {
            if (toast == null)
            {
                toast = UIKit.Label(canvas.transform, "", 36, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0, 420), new Vector2(900, 100), TextAnchor.MiddleCenter, true);
                var bg = toast.gameObject.AddComponent<Outline>();
                bg.effectColor = Color.black; bg.effectDistance = new Vector2(3, -3);
            }
            toast.text = msg;
            toast.transform.SetAsLastSibling();
            if (toastCo != null) StopCoroutine(toastCo);
            toastCo = StartCoroutine(ToastRoutine());
        }

        IEnumerator ToastRoutine()
        {
            toast.gameObject.SetActive(true);
            yield return new WaitForSecondsRealtime(1.6f);
            toast.gameObject.SetActive(false);
        }

        void Update()
        {
            // 훈련장 화면이 열려 있으면 1초마다 갱신
            if (training != null && training.gameObject.activeSelf && Time.frameCount % 60 == 0) RefreshTraining();
        }
    }
}
