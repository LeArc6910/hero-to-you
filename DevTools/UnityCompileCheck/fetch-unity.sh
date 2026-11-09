#!/usr/bin/env bash
# Extracts Unity's engine and editor assemblies for the project's Unity version into DevTools/UnityCompileCheck/.unity,
# so the Unity-side scripts can be compiled without installing the editor. Streams the Linux editor archive (about
# 4 GB) and keeps only the managed DLLs (about 70 MB).
# Then does the same for the web player's engine assemblies (.unity/WebGL, from the 1.6 GB WebGL module). They lack
# the editor-only API that the editor's assemblies have (GUIStyleState.scaledBackgrounds, for one), so compiling
# against them catches code that would only fail in the web build. Each part does nothing if it is already there.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
version_line="$(grep m_EditorVersionWithRevision "$root/ProjectSettings/ProjectVersion.txt")"
version="$(echo "$version_line" | sed -E 's/.*: ([^ ]+) \(([0-9a-f]+)\).*/\1/')"
revision="$(echo "$version_line" | sed -E 's/.*: ([^ ]+) \(([0-9a-f]+)\).*/\2/')"
target="$here/.unity"
web="$target/WebGL"
downloads="https://download.unity3d.com/download_unity/$revision"
mkdir -p "$target"

if [ -f "$target/version" ] && [ "$(cat "$target/version")" = "$version" ]; then
    echo "Unity $version assemblies already in $target"
else
    rm -rf "$target/Managed" "$target/version"
    url="$downloads/LinuxEditorInstaller/Unity-$version.tar.xz"
    echo "Fetching Unity $version assemblies from $url"
    curl -fsSL "$url" | xz -dc -T0 | tar -x -C "$target" --strip-components=2 --wildcards 'Editor/Data/Managed/UnityEngine/*.dll'
    echo "$version" > "$target/version"
    echo "Done: $(ls "$target/Managed/UnityEngine" | wc -l) assemblies"
fi

if [ -f "$web/version" ] && [ "$(cat "$web/version")" = "$version" ]; then
    echo "Unity $version web player assemblies already in $web"
else
    rm -rf "$web"
    mkdir -p "$web"
    url="$downloads/LinuxEditorTargetInstaller/UnitySetup-WebGL-Support-for-Editor-$version.tar.xz"
    echo "Fetching Unity $version web player assemblies from $url"
    curl -fsSL "$url" | xz -dc -T0 \
        | tar -x -C "$web" --strip-components=8 --wildcards './Editor/Data/PlaybackEngines/WebGLSupport/Variations/il2cpp/Managed/UnityEngine*.dll'
    echo "$version" > "$web/version"
    echo "Done: $(ls "$web"/*.dll | wc -l) web player assemblies"
fi
