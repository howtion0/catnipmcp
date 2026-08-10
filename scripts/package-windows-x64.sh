#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd "$script_dir/.." && pwd)"
version="0.0.0"
runtime_id="win-x64"
release_root="$repository_root/artifacts/release"
staging_root="$(mktemp -d "${TMPDIR:-/tmp}/catnip-windows-package.XXXXXX")"
package_root="$staging_root/Catnip-$version-$runtime_id"
payload_zip="$staging_root/Catnip-$version-$runtime_id.zip"
bootstrapper_publish_dir="$staging_root/bootstrapper"

cleanup() {
  if [[ -d "$staging_root" ]]; then
    find "$staging_root" -depth -delete
  fi
}
trap cleanup EXIT

mkdir -p \
  "$package_root" \
  "$package_root/DemoApi" \
  "$package_root/Runtime" \
  "$package_root/WorkBuddyBridge" \
  "$release_root"

dotnet publish \
  "$repository_root/src/Catnip.Desktop/Catnip.Desktop.csproj" \
  -c Release \
  -r "$runtime_id" \
  --self-contained true \
  -p:UseAppHost=true \
  -o "$package_root"

dotnet publish \
  "$repository_root/src/Catnip.DemoApi/Catnip.DemoApi.csproj" \
  -c Release \
  -r "$runtime_id" \
  --self-contained true \
  -p:UseAppHost=true \
  -o "$package_root/DemoApi"

dotnet publish \
  "$repository_root/src/Catnip.Runtime/Catnip.Runtime.csproj" \
  -c Release \
  -r "$runtime_id" \
  --self-contained true \
  -p:UseAppHost=true \
  -o "$package_root/Runtime"

dotnet publish \
  "$repository_root/src/Catnip.WorkBuddyBridge/Catnip.WorkBuddyBridge.csproj" \
  -c Release \
  -r "$runtime_id" \
  --self-contained true \
  -p:UseAppHost=true \
  -o "$package_root/WorkBuddyBridge"

cp "$repository_root/packaging/windows/README-WINDOWS.md" "$package_root/README-WINDOWS.md"

(cd "$package_root" && /usr/bin/zip -qry "$payload_zip" .)

dotnet publish \
  "$repository_root/packaging/windows/bootstrapper/Catnip.WindowsBootstrapper.csproj" \
  -c Release \
  -r "$runtime_id" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PayloadZipPath="$payload_zip" \
  -o "$bootstrapper_publish_dir"

cp \
  "$bootstrapper_publish_dir/Catnip-$version-$runtime_id.exe" \
  "$release_root/Catnip-$version-$runtime_id.exe"
cp \
  "$bootstrapper_publish_dir/Catnip-$version-$runtime_id.exe" \
  "$release_root/catnip.exe"
cp "$payload_zip" "$release_root/Catnip-$version-$runtime_id.zip"

echo "$release_root/Catnip-$version-$runtime_id.exe"
echo "$release_root/catnip.exe"
echo "$release_root/Catnip-$version-$runtime_id.zip"
