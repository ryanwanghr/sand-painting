using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mandala.Game.Editor
{
    public static class MandalaPortSetup
    {
        [MenuItem("Tools/Mandala/Install Gameplay in Main Scene")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play Mode first.");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            foreach (var welcome in Object.FindObjectsByType<MandalaWelcome>()) Object.DestroyImmediate(welcome.gameObject);
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Mandala/UI/MandalaPanel.asset");
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, "Assets/Mandala/UI/MandalaPanel.asset");
            }
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize; panel.referenceResolution = new Vector2Int(430, 900);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; panel.match = 0;
            var game = Object.FindAnyObjectByType<MandalaGame>();
            if (game == null)
            {
                var go = new GameObject("Mandala Game");
                var document = go.AddComponent<UIDocument>(); document.panelSettings = panel;
                document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Mandala/UI/Mandala.uxml");
                go.AddComponent<MandalaGame>();
            }
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.defaultScreenWidth = 430; PlayerSettings.defaultScreenHeight = 900;
            EditorUtility.SetDirty(panel); AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scene.path, true) };
            Debug.Log("Native mobile mandala installed in Main.unity.");
        }
        public static string BuildSmoke()
        {
            Directory.CreateDirectory("Builds/Linux");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { "Assets/Scenes/Main.unity" }, locationPathName = "Builds/Linux/Mandala.x86_64", target = BuildTarget.StandaloneLinux64, options = BuildOptions.Development });
            return report.summary.result + " errors=" + report.summary.totalErrors;
        }
    }
}
