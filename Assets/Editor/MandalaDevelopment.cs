using System;
using System.IO;
using System.Linq;
using Neovim.Editor;
using Unity.CodeEditor;
using UnityEditor;
using UnityEngine;

namespace Mandala.Editor
{
    public static class MandalaDevelopment
    {
        private const string SetupRequest = "UserSettings/ConfigureNeovim";

        [InitializeOnLoadMethod]
        private static void ApplyRequestedSetup()
        {
            // This local, ignored marker requests setup on this machine only.
            if (File.Exists(SetupRequest))
                EditorApplication.delayCall += ConfigureRequestedSetup;
        }

        private static void ConfigureRequestedSetup()
        {
            ConfigureNeovim();
            File.Delete(SetupRequest);
        }

        [MenuItem("Tools/Development/Configure Neovim (Linux)")]
        public static void ConfigureNeovim()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var executable = new[]
            {
                Path.Combine(home, ".local/bin/nvim"),
                "/usr/local/bin/nvim",
                "/usr/bin/nvim",
                "/snap/bin/nvim"
            }.FirstOrDefault(File.Exists);

            if (executable == null)
                throw new FileNotFoundException("Install Neovim before configuring the Unity editor.");

            var config = NeovimCodeEditor.s_Config;
            config.NvimExecutablePath = executable;
            config.ProcessTimeout = 3000;
            config.TermLaunchCmd = "gnome-terminal";
            config.TermLaunchArgs = "--title \"nvimunity-{instanceId}\" --working-directory \"{projectRootDir}\" -- {app} \"{filePath}\" --listen {serverSocket}";
            config.Save();
            CodeEditor.SetExternalScriptEditor(executable);
            UnityEditor.Compilation.CompilationPipeline.codeOptimization = UnityEditor.Compilation.CodeOptimization.Debug;
            CodeEditor.CurrentEditor.SyncAll();
            Debug.Log("Neovim configured. Generated C# projects for language-server support.");
        }

        [MenuItem("Tools/Development/Regenerate C# Projects")]
        public static void RegenerateProjects()
        {
            CodeEditor.CurrentEditor.SyncAll();
        }
    }
}
