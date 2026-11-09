using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Game.Client.Editor
{
    // The project settings Unity would otherwise have someone click through after the first open: product name, 2D
    // editor mode, input (the game reads only IMGUI events, so the old Input Manager is enough), orientations, and
    // the web build settings. Then it builds the scene with SceneSetup. Safe to run again. The web build calls it
    // before building (WebBuild.RunBatch), so a fresh checkout with no ProjectSettings/ builds the same game.
    public static class ProjectSetup
    {
        const string ProductName = "HeroToYou";
        const string CompanyName = "LeArc6910";
        // The web page: Assets/WebGLTemplates/Fit.
        const string WebTemplate = "PROJECT:Fit";
        // Player settings value for "Input Manager (Old)".
        const int OldInputManager = 0;

        [MenuItem("Game/Set Up Project")]
        public static void SetUp()
        {
            PlayerSettings.productName = ProductName;
            PlayerSettings.companyName = CompanyName;
            EditorSettings.defaultBehaviorMode = EditorBehaviorMode.Mode2D;
            UseOldInputManager();
            // Landscape first, but a phone held upright still works (Docs/Design.md 7.5).
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.runInBackground = true;
            UseWebSettings();
            AssetDatabase.SaveAssets();
            SceneSetup.SetUp();
            Debug.Log("Project setup done.");
        }

        // Entry point for batch mode (-executeMethod). Exits with 1 when anything throws.
        public static void RunBatch()
        {
            try
            {
                SetUp();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.Exit(0);
        }

        // Files gzipped with the decompression fallback on, because GitHub Pages does not send the Content-Encoding
        // header Unity's loader would otherwise need; cached in the browser after the first visit. No splash, and
        // IL2CPP code made for size (the game is turn-based and light).
        public static void UseWebSettings()
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.template = WebTemplate;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL, Il2CppCodeGeneration.OptimizeSize);
        }

        // Project Settings > Player > Active Input Handling has no public API; this is the serialized property the
        // Input System package itself changes. Nothing happens when it is not there.
        static void UseOldInputManager()
        {
            var settings = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            var handler = settings.FindProperty("activeInputHandler");
            if (handler == null || handler.intValue == OldInputManager) return;
            handler.intValue = OldInputManager;
            settings.ApplyModifiedProperties();
        }
    }
}
