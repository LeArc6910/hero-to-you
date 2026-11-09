using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Client.Editor
{
    // Builds the web demo: the scenes in the build list, for WebGL, into Builds/Web. The Web demo workflow calls
    // RunBatch (.github/scripts/build-web.sh) with the output folder as -customBuildPath. Before building it sets the
    // project up (a fresh checkout has no scene or settings yet) and writes the commit into Resources/BuildInfo.txt
    // for the title screen (Client/BuildInfo.cs).
    public static class WebBuild
    {
        const string DefaultPath = "Builds/Web";
        const string BuildInfoPath = SceneSetup.ProjectFolder + "/Resources/BuildInfo.txt";

        [MenuItem("Game/Build Web Demo")]
        public static void BuildFromMenu()
        {
            var report = Build(DefaultPath);
            if (report.summary.result == BuildResult.Succeeded) EditorUtility.RevealInFinder(DefaultPath);
        }

        public static void RunBatch()
        {
            try
            {
                ProjectSetup.SetUp();
                WriteBuildInfo();
                var report = Build(Argument("-customBuildPath") ?? DefaultPath);
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        static void WriteBuildInfo()
        {
            string commit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "";
            File.WriteAllText(BuildInfoPath, commit.Trim() + "\n");
            AssetDatabase.ImportAsset(BuildInfoPath);
        }

        static BuildReport Build(string path)
        {
            ProjectSetup.UseWebSettings();
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) throw new InvalidOperationException("No scenes in the build list. Run Game > Set Up Project first.");

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
            var summary = report.summary;
            Debug.Log($"Web build {summary.result} at {path}: {summary.totalSize / (1024f * 1024f):0.0} MB, {summary.totalErrors} errors, took {summary.totalTime}");
            return report;
        }

        // The value after a command line flag, or null.
        static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
