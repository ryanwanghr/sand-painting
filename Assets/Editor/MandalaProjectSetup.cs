using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mandala.Editor
{
    public static class MandalaProjectSetup
    {
        // Invoked explicitly in batch mode to generate the initial scene.
        public static void Initialize()
        {
            const string scenePath = "Assets/Scenes/Main.unity";
            if (File.Exists(scenePath))
            {
                Debug.Log("Mandala starter scene already exists; leaving it intact.");
                return;
            }

            Directory.CreateDirectory("Assets/Scenes");
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0, 0, -10);
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.067f, 0.063f);
            new GameObject("Welcome", typeof(MandalaWelcome));

            PlayerSettings.companyName = "Mandala";
            PlayerSettings.productName = "Sand Painting!";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 800;
            EditorSettings.serializationMode = SerializationMode.ForceText;
            EditorSceneManager.SaveScene(scene, scenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Mandala starter scene created successfully.");
        }
    }
}
