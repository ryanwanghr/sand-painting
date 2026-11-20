using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#elif UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Mandala.Game
{
    internal sealed class MandalaHaptics
    {
        // Keep these values in sync with MandalaFeedback in MandalaHaptics.mm.
        enum Feedback { Selection = 0, Pour = 1, Vacuum = 2, Sweep = 3, Rake = 4 }

        float nextPulse;

        public void Select()
        {
            Play(Feedback.Selection);
            nextPulse = Mathf.Max(nextPulse, Time.unscaledTime + .05f);
        }

        public void Tool(SandTool tool, int activity, float now)
        {
            if (activity <= 0 || now < nextPulse) return;

            var (feedback, interval) = tool switch
            {
                SandTool.Pour => (Feedback.Pour, .095f),
                SandTool.Vacuum => (Feedback.Vacuum, .14f),
                SandTool.Sweep => (Feedback.Sweep, .12f),
                SandTool.Rake => (Feedback.Rake, .075f),
                _ => throw new System.ArgumentOutOfRangeException(nameof(tool))
            };
            nextPulse = now + interval;
            Play(feedback);
        }

        static void Play(Feedback feedback)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidApplication.InvokeOnUIThread(() => PlayAndroid(feedback));
#elif UNITY_IOS && !UNITY_EDITOR
            MandalaPlayHaptic(feedback);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        const int VirtualKey = 1;
        const int KeyboardTap = 3;
        const int ClockTick = 4;
        const int ContextClick = 6;
        const int SegmentTick = 26;
        const int SegmentFrequentTick = 27;
        static int androidApiLevel;

        static void PlayAndroid(Feedback feedback)
        {
            using var window = AndroidApplication.currentActivity?.Call<AndroidJavaObject>("getWindow");
            using var view = window?.Call<AndroidJavaObject>("getDecorView");
            if (view == null || !view.Call<bool>("hasWindowFocus")) return;

            if (androidApiLevel == 0)
            {
                using var version = new AndroidJavaClass("android.os.Build$VERSION");
                androidApiLevel = version.GetStatic<int>("SDK_INT");
            }

            int effect = feedback switch
            {
                Feedback.Selection => androidApiLevel >= 34 ? SegmentTick : VirtualKey,
                Feedback.Pour => androidApiLevel >= 34 ? SegmentFrequentTick : ClockTick,
                Feedback.Vacuum => ContextClick,
                Feedback.Sweep => KeyboardTap,
                _ => ClockTick
            };
            // View feedback follows the device's touch feedback setting.
            view.Call<bool>("performHapticFeedback", effect);
        }
#elif UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void MandalaPlayHaptic(Feedback feedback);
#endif
    }
}
