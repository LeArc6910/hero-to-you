using UnityEditor;
using UnityEngine;

namespace Game.Client.Editor
{
    // How the game's sound files are imported (Resources/Audio, Docs/Sound.md), set in code so the web build and the
    // local editor agree and no .meta has to be edited by hand: effects decompressed on load (short, played at once),
    // music and place sounds kept compressed and loaded in the background, all at a modest quality so the web demo
    // stays small.
    sealed class AudioImport : AssetPostprocessor
    {
        const string Root = "Assets/_Project/Resources/Audio/";

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root)) return;
            var importer = (AudioImporter)assetImporter;
            bool effect = assetPath.StartsWith(Root + "Sfx/");
            bool ambience = assetPath.StartsWith(Root + "Ambience/");
            importer.forceToMono = effect || ambience;
            importer.loadInBackground = !effect;
            var settings = importer.defaultSampleSettings;
            settings.loadType = effect ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.CompressedInMemory;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = effect ? 0.6f : 0.45f;
            settings.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;
            importer.defaultSampleSettings = settings;
            importer.SetOverrideSampleSettings("WebGL", settings);
        }
    }
}
