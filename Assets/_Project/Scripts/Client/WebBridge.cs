using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Game.Client
{
    // What the browser knows that Unity does not tell (Plugins/WebGL/Display.jslib). Elsewhere: no scaling, and touch
    // when the device has a touch screen and no mouse.
    static class WebBridge
    {
        // Set by the playtests to see the touch layout on a desktop browser (Playtest.cs "touch on").
        internal static bool? ForceTouch { get; set; }

        public static float PixelRatio()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return Mathf.Clamp(GetDevicePixelRatio(), 1, 4);
#else
            // Phone apps: about one point per 160 dpi, like the browser's ratio. Desktop: one pixel a point.
            return Application.isMobilePlatform && Screen.dpi > 0 ? Mathf.Clamp(Screen.dpi / 160f, 1, 4) : 1;
#endif
        }

        public static bool TouchInput()
        {
            if (ForceTouch.HasValue) return ForceTouch.Value;
#if UNITY_WEBGL && !UNITY_EDITOR
            return IsTouchInput() != 0;
#else
            return Application.isMobilePlatform;
#endif
        }

        // Puts text on the clipboard (the bug report). False when the browser refused.
        public static bool Copy(string text)
        {
            GUIUtility.systemCopyBuffer = text;
#if UNITY_WEBGL && !UNITY_EDITOR
            return CopyText(text) != 0;
#else
            return true;
#endif
        }

        // Saves text as a file: a download in the browser, a file next to the save elsewhere.
        public static void Download(string name, string text)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            DownloadText(name, text);
#else
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(Application.persistentDataPath, name), text);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Could not write the report: " + e.Message);
            }
#endif
        }

        // A short buzz (hits, the dice) when the settings allow; phones only.
        public static void Buzz(int ms)
        {
            if (!Settings.Vibration) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            Vibrate(ms);
#else
            if (Application.isMobilePlatform) Handheld.Vibrate();
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void Vibrate(int ms);

        [DllImport("__Internal")]
        static extern int CopyText(string text);

        [DllImport("__Internal")]
        static extern void DownloadText(string name, string text);

        [DllImport("__Internal")]
        static extern float GetDevicePixelRatio();

        [DllImport("__Internal")]
        static extern int IsTouchInput();
#endif
    }
}
