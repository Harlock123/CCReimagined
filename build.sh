#!/usr/bin/env bash
#
# Builds CCReimagined for every supported target.
#
#   ./build.sh                     every target, one self-contained file each
#   ./build.sh --mode aot          native builds, for this machine's OS only
#   ./build.sh --targets "win-x64 osx-arm64"
#   ./build.sh --version 1.2.0
#
# The default is a self-contained single-file publish: one executable per
# target with the .NET runtime and Avalonia's native libraries inside it,
# built for all six targets from whichever machine you happen to be on.
#
# Ahead-of-time compilation is available with --mode aot, and is faster to
# start and a good deal smaller - but it is NOT a single file. It leaves
# libSkiaSharp and libHarfBuzzSharp beside the executable, and the executable
# aborts on startup without them:
#
#   System.DllNotFoundException: Unable to load shared library 'libSkiaSharp'
#
# It also cannot cross operating systems - the .NET native compiler refuses
# outright, because it needs the target platform's own linker - so an AOT run
# only builds the targets matching this machine's OS. Crossing processor
# architectures within one OS does work, but needs a cross linker installed.
#
set -euo pipefail

PROJECT="src/CCReimagined.App/CCReimagined.App.csproj"
ALL_TARGETS=(linux-x64 linux-arm64 win-x64 win-arm64 osx-x64 osx-arm64)

MODE="single"
VERSION="1.0.0"
OUTPUT="dist"
TARGETS=("${ALL_TARGETS[@]}")

while [[ $# -gt 0 ]]; do
  case "$1" in
    --mode)    MODE="$2"; shift 2 ;;
    --targets) read -ra TARGETS <<< "$2"; shift 2 ;;
    --version) VERSION="$2"; shift 2 ;;
    --out)     OUTPUT="$2"; shift 2 ;;
    -h|--help) sed -n '2,26p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

if [[ "$MODE" != "aot" && "$MODE" != "single" ]]; then
  echo "--mode must be aot or single" >&2
  exit 2
fi

# Which operating system are we on, in runtime-identifier terms?
case "$(uname -s)" in
  Linux)                     HOST_OS="linux" ;;
  Darwin)                    HOST_OS="osx" ;;
  MINGW*|MSYS*|CYGWIN*)      HOST_OS="win" ;;
  *)                         HOST_OS="unknown" ;;
esac

rid_os() { echo "${1%%-*}"; }

mkdir -p "$OUTPUT"

built=(); skipped=(); failed=()

os_name() {
  case "$1" in
    linux) echo "Linux" ;;
    win)   echo "Windows" ;;
    osx)   echo "macOS" ;;
    *)     echo "$1" ;;
  esac
}

# zip where it exists, Python's zipfile where it does not, tar as a last resort.
make_zip() {
  local archive="$1" directory="$2"

  if command -v zip > /dev/null; then
    (cd "$directory" && zip -qr "$archive" .)
  elif command -v python3 > /dev/null; then
    python3 -m zipfile -c "$archive" "$directory"/*
  else
    return 1
  fi
}

echo "CCReimagined $VERSION - $MODE build on $HOST_OS"
echo

for rid in "${TARGETS[@]}"; do
  target_os="$(rid_os "$rid")"
  stage="$OUTPUT/$rid"

  if [[ "$MODE" == "aot" && "$target_os" != "$HOST_OS" ]]; then
    printf '  %-12s skipped - ahead-of-time compilation needs a %s machine\n' \
      "$rid" "$(os_name "$target_os")"
    skipped+=("$rid")
    continue
  fi

  printf '  %-12s building... ' "$rid"
  rm -rf "$stage"

  if [[ "$MODE" == "aot" ]]; then
    args=(-p:PublishAot=true)
  else
    # Compression is deliberately not used. It halves the file but has to unpack
    # the whole bundle before anything runs.
    args=(--self-contained true
          -p:PublishSingleFile=true
          -p:IncludeNativeLibrariesForSelfExtract=true
          -p:DebugType=none
          -p:DebugSymbols=false)
  fi

  if ! dotnet publish "$PROJECT" -c Release -r "$rid" \
        /p:Version="$VERSION" "${args[@]}" \
        --nologo -o "$stage" > "$stage.log" 2>&1; then
    echo "FAILED (see $stage.log)"

    # Same OS, different processor: the native compiler is there, the linker for
    # the other architecture is not.
    if [[ "$MODE" == "aot" && "$target_os" == "$HOST_OS" ]] &&
       grep -q "linker command failed" "$stage.log" 2>/dev/null; then
      echo "               ^ needs a cross-compilation toolchain for ${rid##*-}"
    fi

    failed+=("$rid")
    continue
  fi

  # Debug symbols are not part of what anyone downloads.
  rm -f "$stage"/*.pdb "$stage"/*.dbg "$stage"/*.dwarf
  rm -f "$stage.log"

  # One archive per target, so a release has one file per platform either way.
  archive="$OUTPUT/CCReimagined-$VERSION-$rid"

  if [[ "$target_os" == "win" ]] && make_zip "$(pwd)/$archive.zip" "$stage"; then
    archive="$archive.zip"
  else
    tar -czf "$archive.tar.gz" -C "$stage" .
    archive="$archive.tar.gz"
  fi

  printf 'done  (%s)\n' "$(du -h "$archive" | cut -f1)"
  built+=("$rid")
done

echo
echo "built:   ${built[*]:-none}"
[[ ${#skipped[@]} -gt 0 ]] && echo "skipped: ${skipped[*]}"
[[ ${#failed[@]}  -gt 0 ]] && echo "failed:  ${failed[*]}"

if [[ "$MODE" == "aot" ]]; then
  if [[ ${#built[@]} -gt 0 ]]; then
    echo
    echo "Note: an ahead-of-time build is not a single file. libSkiaSharp and"
    echo "      libHarfBuzzSharp must stay beside the executable, or it will abort"
    echo "      on startup. Ship the whole archive, not the executable alone."
  fi

  if [[ ${#skipped[@]} -gt 0 ]]; then
    echo
    echo "The skipped targets need a machine of that operating system. Run this script"
    echo "there, or drop --mode aot and build every target from here."
  fi
fi

[[ ${#failed[@]} -eq 0 ]]
