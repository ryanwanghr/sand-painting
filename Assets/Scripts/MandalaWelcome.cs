using UnityEngine;

namespace Mandala
{
    // Temporary welcome screen until the first playable prototype is ready.
    public sealed class MandalaWelcome : MonoBehaviour
    {
        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Max(16, Mathf.RoundToInt(Screen.height * 0.035f)),
                wordWrap = true
            };
            style.normal.textColor = new Color(0.9f, 0.85f, 0.72f);
            GUI.Label(new Rect(24, 0, Mathf.Max(0, Screen.width - 48), Screen.height),
                "Mandala\nA sand-making game\n\nUnity project ready — gameplay port comes next.", style);
        }
    }
}
