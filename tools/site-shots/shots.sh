#!/usr/bin/env bash
# The site's pictures, site/images/<scene>-<dark|light>.webp, retaken from the sample solution beside this script:
# just site-shots [SCENE...] (every scene when none is named). Build first (just build): it runs the Debug build's
# --screenshot. The sample is copied to a folder of its own under TMPDIR and given a git history there; its restore and
# its builds' NuGet stay under artifacts/site-shots and Passenger's state beside the copy, never the person's.
# SITE_SHOTS_FLAGS (--window-buttons by default, so macOS's are in every main-window picture) is added to every
# capture's command line; SITE_SHOTS_REUSE=1 takes the pictures in
# the copy the last run made, without making it again, and SITE_SHOTS_THEMES=dark (or light) takes one theme's.
# A step's point is in the window's logical units: half the picture's pixels.
set -euo pipefail

repo=$(cd "$(dirname "$0")/../.." && pwd -P)
passenger="$repo/artifacts/bin/Passenger.App/debug/Passenger"
work="$repo/artifacts/site-shots"
images="$repo/site/images"
# Each picture at about the size of the ones it replaces (100–150 KB at 2400x1500): at this quality, or at the most
# bytes when a busy one would take more.
quality=82
most=150000
read -r -a extra <<< "${SITE_SHOTS_FLAGS---window-buttons}"

if [[ ! -x "$passenger" ]]; then
    echo "site-shots: $passenger is not there; run just build first" >&2
    exit 2
fi
command -v cwebp > /dev/null || { echo "site-shots: cwebp is not on the PATH (brew install webp)" >&2; exit 2; }

all_scenes=(editor debugger git find-in-files profiler xaml project-files web live-tree)
scenes=("$@")
if [[ ${#scenes[@]} -eq 0 ]]; then
    scenes=("${all_scenes[@]}")
fi
for scene in "${scenes[@]}"; do
    if [[ " ${all_scenes[*]} " != *" $scene "* ]]; then
        echo "site-shots: no scene '$scene'; the scenes are ${all_scenes[*]}" >&2
        exit 2
    fi
done

# Everything the sample's tools write goes under artifacts/site-shots: NuGet's packages, caches and user settings
# (DOTNET_CLI_HOME is the home NuGet reads its own config from), npm's, and an empty git configuration in place of the
# person's, which Passenger's git inherits too.
mkdir -p "$work/home" "$work/nuget" "$work/npm" "$work/png"
: > "$work/gitconfig"
: > "$work/npm/npmrc"
export DOTNET_CLI_HOME="$work/home"
export NUGET_PACKAGES="$work/nuget/packages"
export NUGET_HTTP_CACHE_PATH="$work/nuget/http-cache"
export NUGET_PLUGINS_CACHE_PATH="$work/nuget/plugins-cache"
export npm_config_cache="$work/npm/cache"
export npm_config_userconfig="$work/npm/npmrc"
export GIT_CONFIG_GLOBAL="$work/gitconfig"
export GIT_CONFIG_NOSYSTEM=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export AVALONIA_TELEMETRY_OPTOUT=1
# The sample's Avalonia app draws its window off-screen (its Program.cs), so nothing shows while a picture is taken.
export GREENHOUSE_HEADLESS=1
# The log shows its fixed times as they are wherever the pictures are taken.
export TZ=UTC

# A canonical path, since MSBuild and Roslyn report resolved ones (macOS's TMPDIR is under the /var symbolic link).
tmp=$(realpath "${TMPDIR:-/tmp}")
folder="$tmp/passenger-site-shots/Greenhouse"

# One commit of the sample's history, at a fixed time.
history() {
    local when=$1 message=$2
    shift 2
    git -C "$folder" add -- "$@"
    GIT_AUTHOR_DATE="$when" GIT_COMMITTER_DATE="$when" git -C "$folder" commit -q -m "$message"
}

# The copy, the same every time: the sample, a history with a branch merged back, two files changed since the last
# commit (their committed text is under before/) and one never added; then built once, so XAML Preview has the app's
# build and Debug and Profile start at once.
prepare() {
    rm -rf "$folder"
    mkdir -p "$folder"
    cp -R "$repo/tools/site-shots/sample/." "$folder/"
    export GIT_AUTHOR_NAME="Rosa Field" GIT_AUTHOR_EMAIL="rosa@greenhouse.example"
    export GIT_COMMITTER_NAME="Rosa Field" GIT_COMMITTER_EMAIL="rosa@greenhouse.example"
    git -C "$folder" init -q -b main
    mv "$folder/web/styles/dashboard.css" "$work/dashboard.css"
    local changed
    for changed in src/Greenhouse.Cli/Program.cs src/Greenhouse.Core/WateringPlan.cs; do
        cp "$folder/$changed" "$work/$(basename "$changed").final"
        cp "$repo/tools/site-shots/before/$changed" "$folder/$changed"
    done
    history "2026-10-05T09:12:00+00:00" "A greenhouse of plants and when each needs water" \
        .gitignore .editorconfig nuget.config Directory.Build.props Directory.Packages.props Greenhouse.slnx \
        src/Greenhouse.Core/Plant.cs src/Greenhouse.Core/WateringPlan.cs src/Greenhouse.Core/Greenhouse.Core.csproj
    history "2026-10-05T15:40:00+00:00" "Tests for the watering plan" tests
    git -C "$folder" checkout -q -b forecast
    history "2026-10-06T10:05:00+00:00" "How long the rain tank lasts" src/Greenhouse.Core/Forecast.cs
    history "2026-10-06T11:30:00+00:00" "The forecast on the command line" src/Greenhouse.Cli
    git -C "$folder" checkout -q main
    history "2026-10-07T09:20:00+00:00" "The greenhouse in a window" src/Greenhouse.App
    GIT_AUTHOR_DATE="2026-10-07T14:02:00+00:00" GIT_COMMITTER_DATE="2026-10-07T14:02:00+00:00" \
        git -C "$folder" merge -q --no-ff -m "Merge the tank forecast" forecast
    history "2026-10-08T16:45:00+00:00" "The greenhouse's page" web
    cp "$work/Program.cs.final" "$folder/src/Greenhouse.Cli/Program.cs"
    cp "$work/WateringPlan.cs.final" "$folder/src/Greenhouse.Core/WateringPlan.cs"
    cp "$work/dashboard.css" "$folder/web/styles/dashboard.css"
    unset GIT_AUTHOR_NAME GIT_AUTHOR_EMAIL GIT_COMMITTER_NAME GIT_COMMITTER_EMAIL
    dotnet build "$folder/Greenhouse.slnx" -nologo -v:q -clp:ErrorsOnly
}

if [[ -z ${SITE_SHOTS_REUSE:-} || ! -d "$folder/.git" ]]; then
    prepare
fi

# One picture: a fresh state of its own (layout, breakpoints, logs), the window at the site's size, the scene's steps.
shot() {
    local name=$1 theme=$2
    shift 2
    # Under TMPDIR rather than the repository, since the status bar names a profile's snapshot by its path there.
    local state="$tmp/passenger-site-shots/state/$name-$theme" png="$work/png/$name-$theme.png"
    rm -rf "$state"
    mkdir -p "$state"
    PASSENGER_SETTINGS="$state" "$passenger" "$folder/Greenhouse.slnx" --theme "$theme" --look passenger \
        --size 1200x750 --scale 2 ${extra[@]+"${extra[@]}"} "$@" --screenshot "$png"
    cwebp -quiet -q "$quality" -m 6 "$png" -o "$images/$name-$theme.webp"
    if (($(wc -c < "$images/$name-$theme.webp") > most)); then
        cwebp -quiet -size "$most" -pass 6 -m 6 "$png" -o "$images/$name-$theme.webp"
    fi
    echo "site-shots: $images/$name-$theme.webp ($(($(wc -c < "$images/$name-$theme.webp") / 1024)) KB)"
}

scene() {
    local name=$1 theme=$2
    case $name in
        editor)
            # Completion after a dot, parameters' names before the literals, and the change bars of uncommitted lines.
            shot "$name" "$theme" --file src/Greenhouse.Cli/Program.cs --caret 14:2 --key enter --key enter \
                --type "plan" --type "." --wait 1500 ;;
        debugger)
            # Stopped at a breakpoint in the library, called from the command line's program: Resume twice, to day 3's
            # round. The pointer rests off the toolbar last, where Debug's button has become Hot Reload's, with its tip.
            shot "$name" "$theme" --file src/Greenhouse.Core/WateringPlan.cs --caret 27:9 --key F9 \
                --click-name DebugButton --wait 6000 --key F5 --wait 1500 --key F5 --wait 1500 --hover 180,400 ;;
        git)
            # The Commit window from its stripe button, then the Git window's log, widened past its dates (its splitter
            # dragged right), with a commit on the merged branch chosen and its details.
            shot "$name" "$theme" --file src/Greenhouse.Core/WateringPlan.cs --caret 26:9 --click 20,95 --key "meta+9" \
                --drag 808,600,920,600 --click 414,602 --hover 700,200 ;;
        find-in-files)
            # The popup's matches across the solution and the first one's preview.
            shot "$name" "$theme" --file src/Greenhouse.Cli/Program.cs --key "meta+shift+F" --type "Litres" \
                --wait 1000 ;;
        profiler)
            # The command-line program profiled to its end (about 2 s): its timeline and call tree.
            shot "$name" "$theme" --file src/Greenhouse.Core/Forecast.cs --click-name ProfileButton --wait 15000 ;;
        xaml)
            # XAML Preview beside the file (the tool windows hidden for room), then completion asked for after a binding
            # path's first letter, from its x:DataType's view model: nothing typed, so the preview stays live.
            shot "$name" "$theme" --file src/Greenhouse.App/MainWindow.axaml --caret 16:40 --key "meta+shift+F12" \
                --key "meta+shift+A" --type "XAML Preview" --key enter --wait 8000 --key "control+SPACE" --wait 1500 ;;
        project-files)
            # The hover card of a property used in Directory.Packages.props: its value and where it is set.
            shot "$name" "$theme" --file Directory.Packages.props --hover 832,214 --wait 2000 ;;
        web)
            # TypeScript's completion of a type from another file.
            shot "$name" "$theme" --file web/src/dashboard.ts --caret 12:20 --key enter --type "plant" --type "." \
                --wait 1500 ;;
        live-tree)
            # The app chosen in the toolbar's menu and debugged (headless, GREENHOUSE_HEADLESS), the tool windows hidden
            # for the Live Visual Tree, the summary's TextBlock chosen in it, its binding values after the XAML's lines.
            shot "$name" "$theme" --file src/Greenhouse.App/MainWindow.axaml --caret 16:1 \
                --click-name RunConfigButton --click 890,74 --click-name DebugButton --wait 10000 \
                --key "meta+shift+F12" --key "meta+shift+A" --type "Live Visual Tree" --key enter --wait 3000 \
                --click 994,363 --wait 2000 --hover 600,600 ;;
    esac
}

read -r -a themes <<< "${SITE_SHOTS_THEMES:-dark light}"
for scene_name in "${scenes[@]}"; do
    for theme in "${themes[@]}"; do
        scene "$scene_name" "$theme"
    done
done
