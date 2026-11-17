using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Mandala.Game.Editor
{
    /// <summary>Creates and saves a fixed-resolution preview for the active Game view platform.</summary>
    public static class MandalaPreview
    {
        const int Width = 1080;
        const int Height = 2260;
        const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [MenuItem("Tools/Mandala/Preview at 1080 x 2260")]
        public static void SelectHighResolution()
        {
            // Unity exposes no public Game view preset API. Keep the version-specific
            // reflection here, isolated from gameplay (project pinned to 6000.6.9f1).
            var assembly = typeof(UnityEditor.Editor).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes", true);
            var singletonType = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = singletonType.GetProperty("instance").GetValue(null);
            var group = sizesType.GetProperty("currentGroup", Members).GetValue(sizes);
            var groupType = group.GetType();
            var countMethod = groupType.GetMethod("GetTotalCount", Members);
            var getSize = groupType.GetMethod("GetGameViewSize", Members);
            int count = (int)countMethod.Invoke(group, null);
            int index = -1;
            for (int i = 0; i < count; i++)
            {
                var candidate = getSize.Invoke(group, new object[] { i });
                var type = candidate.GetType();
                if ((int)type.GetProperty("width", Members).GetValue(candidate) == Width
                    && (int)type.GetProperty("height", Members).GetValue(candidate) == Height
                    && type.GetProperty("sizeType", Members).GetValue(candidate).ToString() == "FixedResolution")
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                var sizeType = assembly.GetType("UnityEditor.GameViewSize", true);
                var modeType = assembly.GetType("UnityEditor.GameViewSizeType", true);
                var size = Activator.CreateInstance(sizeType, new[]
                {
                    Enum.Parse(modeType, "FixedResolution"), (object)Width, Height, "Mandala High Resolution"
                });
                groupType.GetMethod("AddCustomSize", Members).Invoke(group, new[] { size });
                index = count;
            }

            // Adding a preset alone only changes memory; explicitly persist the list.
            sizesType.GetMethod("SaveToHDD", Members).Invoke(sizes, null);
            var viewType = assembly.GetType("UnityEditor.GameView", true);
            var view = EditorWindow.GetWindow(viewType);
            viewType.GetProperty("selectedSizeIndex", Members).SetValue(view, index);
            view.Repaint();
            Debug.Log("Selected and saved Mandala's 1080 x 2260 Game view preset.");
        }
    }
}
