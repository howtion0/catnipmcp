#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd "$script_dir/.." && pwd)"
machine_arch="$(uname -m)"

case "$machine_arch" in
  arm64)
    runtime_id="osx-arm64"
    ;;
  x86_64)
    runtime_id="osx-x64"
    ;;
  *)
    echo "Unsupported macOS architecture: $machine_arch" >&2
    exit 1
    ;;
esac

output_root="$repository_root/artifacts/macos"
app_bundle="$output_root/Catnip.app"
staging_root="$(mktemp -d "${TMPDIR:-/tmp}/catnip-macos-package.XXXXXX")"
desktop_publish_dir="$staging_root/desktop"
demo_api_publish_dir="$staging_root/demo-api"
runtime_publish_dir="$staging_root/runtime"
workbuddy_bridge_publish_dir="$staging_root/workbuddy-bridge"
staged_bundle="$staging_root/Catnip.app"

cleanup() {
  if [[ -d "$staging_root" ]]; then
    find "$staging_root" -depth -delete
  fi
}
trap cleanup EXIT

dotnet publish \
  "$repository_root/src/Catnip.Desktop.Mac/Catnip.Desktop.Mac.csproj" \
  -c Release \
  -r "$runtime_id" \
  --self-contained true \
  -p:UseAppHost=true \
  -o "$desktop_publish_dir"

dotnet publish \
  "$repository_root/src/Catnip.DemoApi/Catnip.DemoApi.csproj" \
  -c Release \
  -r "$runtime_id" \
  --self-contained true \
  -p:UseAppHost=true \
  -o "$demo_api_publish_dir"

dotnet publish \
  "$repository_root/src/Catnip.Runtime/Catnip.Runtime.csproj" \
  -c Release \
  -r "$runtime_id" \
  --self-contained true \
  -p:UseAppHost=true \
  -o "$runtime_publish_dir"

dotnet publish \
  "$repository_root/src/Catnip.WorkBuddyBridge/Catnip.WorkBuddyBridge.csproj" \
  -c Release \
  -r "$runtime_id" \
  --self-contained true \
  -p:UseAppHost=true \
  -o "$workbuddy_bridge_publish_dir"

mkdir -p \
  "$staged_bundle/Contents/MacOS" \
  "$staged_bundle/Contents/Resources/DemoApi" \
  "$staged_bundle/Contents/Resources/Runtime" \
  "$staged_bundle/Contents/Resources/WorkBuddyBridge"
ditto "$desktop_publish_dir" "$staged_bundle/Contents/MacOS"
ditto "$demo_api_publish_dir" "$staged_bundle/Contents/Resources/DemoApi"
ditto "$runtime_publish_dir" "$staged_bundle/Contents/Resources/Runtime"
ditto "$workbuddy_bridge_publish_dir" "$staged_bundle/Contents/Resources/WorkBuddyBridge"
cp "$repository_root/packaging/macos/Info.plist" "$staged_bundle/Contents/Info.plist"
chmod 755 "$staged_bundle/Contents/MacOS/Catnip.Desktop.Mac"
chmod 755 "$staged_bundle/Contents/Resources/DemoApi/Catnip.DemoApi"
chmod 755 "$staged_bundle/Contents/Resources/Runtime/Catnip.Runtime"
chmod 755 "$staged_bundle/Contents/Resources/WorkBuddyBridge/Catnip.WorkBuddyBridge"

mkdir -p "$output_root"
if [[ -d "$app_bundle" ]]; then
  find "$app_bundle" -depth -delete
fi
mv "$staged_bundle" "$app_bundle"

echo "$app_bundle"
