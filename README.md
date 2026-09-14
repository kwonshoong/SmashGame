# SmashGame (Royal Smash 경량 확장안 B′ 프로토타입)

Unity 6000.3.13f1 기준. 씬 편집 없이 **코드만으로** 게임이 생성됩니다.

## 여는 법
1. Unity Hub → Add → `D:\Unity_Project\SmashGame` 선택 → 열기 (첫 실행 시 패키지 해석 1~2분).
2. 프로젝트가 열리면 `Assets/Scenes/Main.unity`가 자동 생성되고 빌드 설정에 등록됩니다.
   (안 생겼으면 메뉴 `SmashGame > Setup Main Scene`.)
3. Game 뷰 해상도를 세로(예: 1080x1920)로 두고 **Play**.

## 조작
- 화면 탭 = 대포 발사. 받침대 위 블록을 전부 떨어뜨리면 클리어.
- 왕관(금색) 블록을 직접 맞히면 PERFECT (파괴력 1.5배 + 코인 보너스). 레벨 12부터.

## 구현된 기획 요소 (경량 확장안 기획서 대응)
| 기획서 | 구현 |
|---|---|
| 3.2 공 스탯 4종 (파괴력·크기·무게·탄약, Lv1~50) | `Balance.cs`, `GameManager.UpgradeStat` |
| 3.3 강화 규칙 / 별 승급 외형 | 대장간 UI, 공 색상 = 별 등급 |
| 3.4 시험 발사대 | 로비 배경의 `TestRange` (대장간 "시험 발사" 버튼) |
| 3.5 남은 공 환급 (1개당 2코인, 레벨 10부터) | `GameManager.OnLevelWon` |
| 3.6 퍼펙트 히트 (왕관 블록) | `Block.MakeCrown`, `Ball.OnCollisionEnter`, 슬로모션 |
| 5. 훈련장 (돼지 6명 순차 합류, 4h 상한, 오프라인 효율, 탭 보너스) | `TrainingGround.cs`, 훈련장 UI |
| 5.5 훈련장 챕터 4단계 | `Balance.Chapter*`, 훈련장 "다음 챕터" |
| 5.6 오늘 클리어 수 배율 | `Balance.DailyClearMult` |
| 6.1 강화 블록 (레벨 61~, 20% 이하) | `LevelBuilder.Build` → `Block.hp` |
| 6.2 접착 블록 (레벨 91~) | `FixedJoint` |
| 6.3 시작 공 수 보정 + 연승 +3 | `LevelController.Init` |
| 8. 온보딩 게이트 (레벨 8 대장간, 10 환급, 12 왕관, 15 훈련장) | `Balance` 상수 |
| 역기획서 3장 (소재 9종, 받침대 1~3개, 망치·풍차, 테마 3종, 하드 레벨) | `LevelBuilder`, `Obstacles.cs` |

## 폴더
```
Assets/Scripts/Core      Balance, SaveData, GameManager
Assets/Scripts/Gameplay  Block, Ball, Cannon, Obstacles, LevelBuilder, LevelController
Assets/Scripts/Meta      TrainingGround
Assets/Scripts/UI        UIKit, UIManager
Assets/Scripts/Bootstrap.cs   씬에 아무것도 없어도 GameManager 생성
Assets/Editor/ProjectSetup.cs Main 씬 자동 생성 메뉴
```

## 테스트 팁
- 에디터 창이 뒤에 있으면 플레이 모드가 멈출 수 있어 `Run In Background`를 켜 두었습니다(`ProjectSetup` 실행 시 자동 설정).
- 메뉴 `SmashGame > Reset Save Data`로 세이브 초기화 (로비의 "초기화" 버튼과 동일).
- 훈련장 오프라인 테스트: 수령 후 Play를 끄고 잠시 뒤 다시 Play. `lastCollectTicks` 기준으로 계산되므로 에디터에서도 동작.
- 광고 2배 버튼은 프로토타입이라 광고 없이 즉시 2배 지급.
- 모든 수치는 `Balance.cs` 한 파일에 있음.
