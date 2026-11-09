#!/usr/bin/env bash
# Runs inside GameCI's Unity image (unityci/editor) for the Web demo workflow: activates a Unity Personal license
# with the account's email and password, builds the web demo into the folder given as $1, and returns the license
# however the build ends.
#
# GameCI's own activation (unity-builder) always passes a serial, taking it from a ULF file for Personal licenses.
# Unity 6 Personal licenses have no serial and Hub leaves no ULF, so that fails with "serial invalid". The Licensing
# Client that ships with the editor can activate Personal directly: --activate-ulf without --serial.
#
# Needs UNITY_EMAIL and UNITY_PASSWORD in the environment. UNITY_LICENSING_CLIENT and UNITY_EDITOR override the
# image's paths (for testing outside the image).
set -euo pipefail

output="${1:?usage: build-web.sh <output folder>}"
client="${UNITY_LICENSING_CLIENT:-${UNITY_PATH:-/opt/unity}/Editor/Data/Resources/Licensing/Client/Unity.Licensing.Client}"
# GameCI's wrapper: adds -batchmode and runs the editor under a virtual display.
editor="${UNITY_EDITOR:-unity-editor}"
client_log="$HOME/.config/unity3d/Unity/Unity.Licensing.Client.log"
: "${UNITY_EMAIL:?UNITY_EMAIL is not set}"
: "${UNITY_PASSWORD:?UNITY_PASSWORD is not set}"

personal_active() {
    "$client" --showEntitlements | grep -q "Product Name: Unity Personal"
}

return_license() {
    echo "Returning the license"
    "$client" --return-ulf || echo "::warning::Could not return the Unity license"
}

# Set before activating, so a license that activated but did not check out is returned too.
trap return_license EXIT

# Activation talks to Unity's servers, so retry a few times before giving up.
for attempt in 1 2 3; do
    echo "Activating a Unity Personal license (attempt $attempt)"
    if "$client" --activate-ulf --include-personal --username "$UNITY_EMAIL" --password "$UNITY_PASSWORD" && personal_active; then
        break
    fi
    if [ "$attempt" = 3 ]; then
        echo "::error::Unity Personal license activation failed. Check that UNITY_EMAIL and UNITY_PASSWORD sign in at https://id.unity.com (no social login or two-step verification)."
        if [ -f "$client_log" ]; then tail -n 40 "$client_log"; fi
        exit 1
    fi
    sleep $((attempt * 20))
done

build() {
    "$editor" \
        -quit \
        -projectPath "$PWD" \
        -buildTarget WebGL \
        -executeMethod Game.Client.Editor.WebBuild.RunBatch \
        -customBuildPath "$PWD/$output" \
        -logFile /dev/stdout
}

# The editor is sometimes killed (exit 137) a second after it starts, before it has done anything; it happened in
# three of the first dozen builds (2026-10-28) and a second start has always worked. Only that is retried.
for attempt in 1 2 3; do
    status=0
    build || status=$?
    if [ "$status" -ne 137 ] || [ "$attempt" = 3 ]; then break; fi
    echo "::warning::The editor was killed (exit 137) on attempt $attempt; starting it again"
    sleep 10
done
if [ "$status" -ne 0 ]; then exit "$status"; fi

if [ ! -f "$output/index.html" ]; then
    echo "::error::The build finished without $output/index.html"
    exit 1
fi
