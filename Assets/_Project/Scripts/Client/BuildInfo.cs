using UnityEngine;

namespace Game.Client
{
    // Which commit this build is: the web build writes Resources/BuildInfo.txt (Editor/WebBuild.cs) from the commit
    // the workflow builds. The title shows it, so a phone screenshot tells which version it was.
    static class BuildInfo
    {
        static string _version;

        public static string Version
        {
            get
            {
                if (_version != null) return _version;
                var asset = Resources.Load<TextAsset>("BuildInfo");
                string text = asset != null ? asset.text.Trim() : "";
                // A build without a commit (the editor) says so in the title's language; it is not kept.
                if (text.Length < 7) return Strings.Get("title.dev_build");
                _version = text.Substring(0, 7);
                return _version;
            }
        }
    }
}
