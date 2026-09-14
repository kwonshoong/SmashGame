using UnityEngine;

namespace SmashGame
{
    /// <summary>
    /// 어떤 씬에서 시작하든 GameManager가 없으면 만든다. 씬 편집 없이 코드만으로 게임이 뜬다.
    /// </summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (GameManager.I != null) return;
            if (Object.FindFirstObjectByType<GameManager>() != null) return;
            var go = new GameObject("GameManager");
            go.AddComponent<GameManager>();
        }
    }
}
