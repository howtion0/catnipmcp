#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd "$script_dir/.." && pwd)"
release_root="$repository_root/artifacts/release"
mac_app="$repository_root/artifacts/macos/Catnip.app"
checksums="$release_root/SHA256SUMS.txt"

case "$(uname -m)" in
  arm64)
    mac_runtime_id="macos-arm64"
    ;;
  x86_64)
    mac_runtime_id="macos-x64"
    ;;
  *)
    echo "Unsupported macOS architecture: $(uname -m)" >&2
    exit 1
    ;;
esac

mac_zip_name="Catnip-0.0.0-$mac_runtime_id.zip"
mac_zip="$release_root/$mac_zip_name"

"$script_dir/package-windows-x64.sh"
"$script_dir/package-macos-app.sh"

mkdir -p "$release_root"
ditto -c -k --norsrc --keepParent "$mac_app" "$mac_zip"

(
  cd "$release_root"
  shasum -a 256 \
    Catnip-0.0.0-win-x64.exe \
    catnip.exe \
    Catnip-0.0.0-win-x64.zip \
    "$mac_zip_name" \
    > SHA256SUMS.txt
)

echo "$release_root"
echo "$checksums"
