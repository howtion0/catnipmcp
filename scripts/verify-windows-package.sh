#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd "$script_dir/.." && pwd)"
release_root="$repository_root/artifacts/release"
windows_exe="$release_root/Catnip-0.0.0-win-x64.exe"
simple_exe="$release_root/catnip.exe"
windows_zip="$release_root/Catnip-0.0.0-win-x64.zip"
inspection_root="$(mktemp -d "${TMPDIR:-/tmp}/catnip-windows-inspection.XXXXXX")"

cleanup() {
  if [[ -d "$inspection_root" ]]; then
    find "$inspection_root" -depth -delete
  fi
}
trap cleanup EXIT

test -s "$windows_exe"
test -s "$simple_exe"
test -s "$windows_zip"
file "$windows_exe" | rg -q 'PE32\+ executable \(GUI\) x86-64, for MS Windows'
file "$simple_exe" | rg -q 'PE32\+ executable \(GUI\) x86-64, for MS Windows'
cmp -s "$windows_exe" "$simple_exe"

unzip -q "$windows_zip" -d "$inspection_root"
test -s "$inspection_root/Catnip.Desktop.exe"
test -s "$inspection_root/DemoApi/Catnip.DemoApi.exe"
test -s "$inspection_root/Runtime/Catnip.Runtime.exe"
test -s "$inspection_root/WorkBuddyBridge/Catnip.WorkBuddyBridge.exe"

if find "$inspection_root" -type f \( \
  -name '*.db' -o \
  -name '*.db-shm' -o \
  -name '*.db-wal' -o \
  -name '*.jsonl' -o \
  -name '*.masterkey' -o \
  -name 'mcp.json' \
\) | rg -q '.'; then
  echo "Windows package contains forbidden runtime data." >&2
  exit 1
fi

find "$inspection_root" -type f \( -name '*.json' -o -name '*.config' -o -name '*.md' -o -name '*.txt' \) -print0 \
  | xargs -0 rg -n -i \
    '(api[_ -]?key|app[_ -]?secret|authorization)["'"'"']?\s*[:=]\s*["'"'"'][^"'"'"']{8,}' \
    && {
      echo "Windows package may contain a plaintext credential." >&2
      exit 1
    }

shasum -a 256 "$windows_exe" "$simple_exe" "$windows_zip"
