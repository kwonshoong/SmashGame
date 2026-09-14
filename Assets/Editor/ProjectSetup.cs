using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SmashGame.EditorTools
{
    /// <summary>
    /// 프로젝트를 처음 열 때 Main 씬을 만들고 빌드 설정에 넣는다. 메뉴 SmashGame/Setup Main Scene 으로 다시 실행 가능.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        static ProjectSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (!System.IO.File.Exists(ScenePath)) CreateMainScene();
            };
        }

        [MenuItem("SmashGame/Setup Main Scene")]
        public static void CreateMainScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.45f, 0.72f, 0.98f);
            camGo.transform.position = new Vector3(0, 2.6f, -9.5f);

            var sun = new GameObject("Sun");
            var l = sun.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.1f;
            l.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50, -30, 0);

            var gm = new GameObject("GameManager");
            gm.AddComponent<GameManager>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.runInBackground = true; // 에디터가 뒤에 있어도 플레이 모드가 멈추지 않게
            Debug.Log("[SmashGame] Main 씬 생성 완료: " + ScenePath + "  — Play 버튼을 누르면 게임이 시작됩니다.");
        }

        [MenuItem("SmashGame/Reset Save Data")]
        public static void ResetSave()
        {
            SaveData.Reset();
            Debug.Log("[SmashGame] 세이브 초기화");
        }

        [MenuItem("SmashGame/Set Game View Portrait (1080x1920)")]
        public static void Portrait()
        {
            Debug.Log("[SmashGame] Game 뷰 우측 상단 해상도 드롭다운에서 1080x1920 또는 9:19.5 세로를 선택하세요.");
        }
    }
}
