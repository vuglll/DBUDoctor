#!/usr/bin/env bash
# Builds one PyPI wheel per release archive in dist/ into wheels/. Usage: tools/wheels/build_all.sh <version>
set -euo pipefail
version="$1"
mkdir -p wheels extract
for archive in dist/dbudoctor-*.tar.gz dist/dbudoctor-*.zip; do
  [ -e "$archive" ] || continue
  rid="$(basename "$archive")"; rid="${rid#dbudoctor-}"; rid="${rid%.tar.gz}"; rid="${rid%.zip}"
  mkdir -p "extract/$rid"
  case "$archive" in
    *.zip) unzip -q -o "$archive" -d "extract/$rid"; binary="extract/$rid/dbudoctor.exe" ;;
    *) tar -xzf "$archive" -C "extract/$rid"; binary="extract/$rid/dbudoctor" ;;
  esac
  python3 tools/wheels/build_wheel.py --rid "$rid" --binary "$binary" --version "$version" \
    --readme README.md --license LICENSE --out wheels
done
count="$(ls wheels/*.whl | wc -l)"
if [ "$count" -ne 4 ]; then
  echo "::error::expected 4 wheels, built $count"
  exit 1
fi
