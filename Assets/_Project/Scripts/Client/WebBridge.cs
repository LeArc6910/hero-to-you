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

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern float GetDevicePixelRatio();

        [DllImport("__Internal")]
        static extern int IsTouchInput();
#endif
    }
}
