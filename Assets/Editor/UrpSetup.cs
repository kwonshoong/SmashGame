using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SmashGame.EditorTools
{
    /// <summary>
    /// URP 파이프라인 에셋을 코드로 만들어 프로젝트에 지정한다(외부 에셋 없음).
    /// 프로젝트를 열 때 자동으로 한 번 실행되며, 메뉴 SmashGame/Setup URP 로 다시 실행할 수 있다.
    /// 런타임에 Shader.Find로 찾는 셰이더들은 Always Included Shaders에 등록해 빌드에서 빠지지 않게 한다.
    /// </summary>
    [InitializeOnLoad]
    public static class UrpSetup
    {
        const string Dir = "Assets/Settings";
        const string RendererPath = Dir + "/SmashRenderer.asset";
        const string PipelinePath = Dir + "/SmashURP.asset";

        static UrpSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (GraphicsSettings.defaultRenderPipeline == null || !System.IO.File.Exists(PipelinePath)) Setup();
            };
        }

        [MenuItem("SmashGame/Setup URP")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            System.IO.Directory.CreateDirectory(Dir);

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }
            renderer.renderingMode = RenderingMode.Forward;

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            // 모바일 캐주얼 기준 품질: HDR(블룸에 필요) + 소프트 섀도 + MSAA 4x
            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 4;
            pipeline.supportsCameraDepthTexture = false;
            pipeline.supportsCameraOpaqueTexture = false;
            pipeline.shadowDistance = 40f;
            pipeline.shadowCascadeCount = 2;
            pipeline.mainLightShadowmapResolution = 2048;
            pipeline.colorGradingMode = ColorGradingMode.HighDynamicRange;
            pipeline.colorGradingLutSize = 32;
            // 공개 setter가 없는 항목은 직렬화 필드로 설정
            var pso = new SerializedObject(pipeline);
            var soft = pso.FindProperty("m_SoftShadowsSupported"); if (soft != null) soft.boolValue = true;
            var addl = pso.FindProperty("m_AdditionalLightsPerObjectLimit"); if (addl != null) addl.intValue = 4;
            pso.ApplyModifiedProperties();
            EditorUtility.SetDirty(pipeline);
            EditorUtility.SetDirty(renderer);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, false);

            AddAlwaysIncludedShaders(new[]
            {
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Unlit",
                "Skybox/Procedural",
                "UI/Default",
            });

            AssetDatabase.SaveAssets();
            Debug.Log("[SmashGame] URP 설정 완료: " + PipelinePath);
        }

        static void AddAlwaysIncludedShaders(IEnumerable<string> names)
        {
            var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (gs == null || gs.Length == 0) return;
            var so = new SerializedObject(gs[0]);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            if (arr == null) return;
            foreach (var n in names)
            {
                var sh = Shader.Find(n);
                if (sh == null) continue;
                bool exists = false;
                for (int i = 0; i < arr.arraySize; i++)
                    if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) { exists = true; break; }
                if (exists) continue;
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
            }
            so.ApplyModifiedProperties();
        }
    }
}
