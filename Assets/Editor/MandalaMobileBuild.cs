using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Mandala.Editor
{
    public static class MandalaMobileBuild
    {
        public const string MainScene = "Assets/Scenes/Main.unity";
        public static string SigningFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "sand-painting", "android-upload", "signing.json");

        [Serializable]
        public sealed class AndroidSigning
        {
            public string keystorePath;
            public string keystorePassword;
            public string keyAlias;
            public string keyPassword;
        }

        public static void Configure(string applicationId)
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Stop Play Mode before configuring mobile builds.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(applicationId,
                @"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$"))
                throw new ArgumentException("Use a lowercase reverse-domain app ID, for example com.yourstudio.sandpainting.");

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, applicationId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, applicationId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)36;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScene, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Android and iOS settings configured for " + applicationId);
        }

        [MenuItem("Tools/Mandala/Build/Android Google Play Bundle")]
        public static void BuildAndroidBundle() => BuildAndroid(true);

        [MenuItem("Tools/Mandala/Build/Android Device APK")]
        public static void BuildAndroidApk() => BuildAndroid(false);

        static void BuildAndroid(bool bundle)
        {
            ValidateTarget(BuildTarget.Android);
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP ||
                (PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) == 0)
                throw new InvalidOperationException("Android release builds require IL2CPP and ARM64.");
            if (!File.Exists(SigningFile))
                throw new FileNotFoundException("Configure the external Android upload key first: " + SigningFile);
            var signing = JsonUtility.FromJson<AndroidSigning>(File.ReadAllText(SigningFile));
            if (signing == null || !File.Exists(signing.keystorePath) ||
                string.IsNullOrEmpty(signing.keystorePassword) || string.IsNullOrEmpty(signing.keyAlias) ||
                string.IsNullOrEmpty(signing.keyPassword))
                throw new InvalidOperationException("The external signing configuration is incomplete.");

            bool oldCustom = PlayerSettings.Android.useCustomKeystore;
            string oldPath = PlayerSettings.Android.keystoreName;
            string oldAlias = PlayerSettings.Android.keyaliasName;
            bool oldBundle = EditorUserBuildSettings.buildAppBundle;
            bool oldExport = EditorUserBuildSettings.exportAsGoogleAndroidProject;
            try
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = signing.keystorePath;
                PlayerSettings.Android.keyaliasName = signing.keyAlias;
                PlayerSettings.Android.keystorePass = signing.keystorePassword;
                PlayerSettings.Android.keyaliasPass = signing.keyPassword;
                EditorUserBuildSettings.buildAppBundle = bundle;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
                Build(BuildTarget.Android, "Builds/Android/SandPainting." + (bundle ? "aab" : "apk"));
            }
            finally
            {
                PlayerSettings.Android.keystorePass = "";
                PlayerSettings.Android.keyaliasPass = "";
                PlayerSettings.Android.keystoreName = oldPath;
                PlayerSettings.Android.keyaliasName = oldAlias;
                PlayerSettings.Android.useCustomKeystore = oldCustom;
                EditorUserBuildSettings.buildAppBundle = oldBundle;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = oldExport;
                AssetDatabase.SaveAssets();
            }
        }

        [MenuItem("Tools/Mandala/Build/iOS Xcode Project")]
        public static void BuildIosProject()
        {
            ValidateTarget(BuildTarget.iOS);
            Build(BuildTarget.iOS, "Builds/iOS");
        }

        static void ValidateTarget(BuildTarget target)
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling)
                throw new InvalidOperationException("Stop Play Mode and wait for compilation before building.");
            if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
                throw new InvalidOperationException("Install " + target + " Build Support for Unity 6000.6.9f1 first.");
            if (EditorUserBuildSettings.activeBuildTarget != target)
                throw new InvalidOperationException("Switch the active Build Profile to " + target + " first.");
            if (Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
                .Any(i => EditorSceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Save your scene changes before building.");
        }

        static void Build(BuildTarget target, string output)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { MainScene }, target = target, locationPathName = output,
                options = BuildOptions.DetailedBuildReport
            });
            string summary = $"{target}: {report.summary.result}; errors={report.summary.totalErrors}; " +
                $"warnings={report.summary.totalWarnings}; bytes={report.summary.totalSize}; output={output}";
            File.WriteAllText(output + ".build-summary.txt", summary + Environment.NewLine);
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException(summary);
            Debug.Log(summary);
        }
    }
}
