using UnityEditor;
using UnityEngine;

namespace Mandala.Game.Editor
{
    public static class MandalaAudioPreview
    {
        [MenuItem("Tools/Mandala/Test Completion Sound")]
        public static void PlayCompletion() => Preview(false);

        [MenuItem("Tools/Mandala/Test Completion Pattern")]
        public static void PlayPattern() => Preview(true);

        static void Preview(bool fullPattern)
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.Log("Enter Play Mode, then choose Tools > Mandala > Test Completion Sound.");
                return;
            }
            var audio = Object.FindAnyObjectByType<MandalaAudio>();
            if (audio == null) { Debug.LogWarning("Open the Main scene to test the completion sound."); return; }
            if (!audio.Enabled) { Debug.LogWarning("Turn Sound on in the game to hear the completion sound."); return; }
            if (EditorUtility.audioMasterMute) { Debug.LogWarning("Turn off Mute Audio in the Game view to hear the completion sound."); return; }
            if (fullPattern) { audio.ResetPattern(); audio.Celebrate(8); }
            else audio.Celebrate();
        }
    }
}
