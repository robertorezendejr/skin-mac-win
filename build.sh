#!/bin/bash
# Gera MacSkin-Setup.exe num Mac.
# Ícones e wallpaper são extraídos do SEU macOS na hora (não vão para o repositório: são da Apple).
# Requisitos: Xcode Command Line Tools (swift), python3 + Pillow, makensis (brew install makensis), curl, unzip.
set -euo pipefail
cd "$(dirname "$0")"
P=instalador/payload
T=$(mktemp -d)
trap 'rm -rf "$T"' EXIT
mkdir -p "$P"/{icones,cursores,wallpaper,fontes} "$T/png"

echo "1/5 ícones do macOS"
ARGS=()
for a in /System/Applications/*.app /System/Applications/Utilities/{Terminal,"Activity Monitor","Disk Utility",Screenshot,Console}.app /Applications/*.app /System/Library/CoreServices/Finder.app; do
  [ -e "$a" ] || continue
  n=$(basename "$a" .app); [[ "$n" == *Uninstaller* ]] && continue
  ARGS+=("$n=$(cd "$a" && pwd -P)")   # pwd -P: Safari é link, sem isso sai com a seta de atalho
done
R=/System/Library/CoreServices/CoreTypes.bundle/Contents/Resources
for i in TrashIcon FullTrashIcon GenericFolderIcon OpenFolderIcon DocumentsFolderIcon DownloadsFolder DesktopFolderIcon PicturesFolderIcon MusicFolderIcon MovieFolderIcon HomeFolderIcon ApplicationsFolderIcon UtilitiesFolder GenericNetworkIcon GenericDocumentIcon GenericApplicationIcon UserIcon; do
  ARGS+=("$i=$R/$i.icns")
done
ARGS+=("DiscoInterno=/System/Library/Extensions/IOStorageFamily.kext/Contents/Resources/Internal.icns")
swift instalador/scripts/extrair.swift "$T/png" "${ARGS[@]}" > /dev/null

echo "2/5 wallpaper Sonoma (claro/escuro)"
swift instalador/scripts/frames.swift "/System/Library/Desktop Pictures/Sonoma.heic" "$T" > /dev/null

python3 - "$T" "$P" <<'EOF'
import os, sys
from PIL import Image
t, p = sys.argv[1], sys.argv[2]
for f in os.listdir(f"{t}/png"):
    Image.open(f"{t}/png/{f}").convert("RGBA").save(f"{p}/icones/{f[:-4]}.ico",
        sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
for i, n in [(0, "claro"), (1, "escuro")]:   # Sonoma.heic: quadro 0 = claro, 1 = escuro
    Image.open(f"{t}/q{i}.jpg").resize((3840, 3840), Image.LANCZOS).save(f"{p}/wallpaper/{n}.jpg", quality=88)
EOF

echo "3/5 cursores macOS (apple_cursor, GPL-3.0)"
curl -sfL -o "$T/cur.zip" https://github.com/ful1e5/apple_cursor/releases/download/v2.0.1/macOS-Windows.zip
unzip -oq "$T/cur.zip" -d "$T/cur"
cp "$T"/cur/macOS-Regular-Windows/*.{cur,ani} "$P/cursores/"

echo "4/5 fonte Inter (OFL)"
curl -sfL -o "$T/inter.zip" https://github.com/rsms/inter/releases/download/v4.1/Inter-4.1.zip
unzip -oq "$T/inter.zip" -d "$T/inter"
cp "$T"/inter/InterVariable*.ttf "$P/fontes/"
cp "$T/inter/LICENSE.txt" "$P/fontes/LICENSE-Inter.txt"

echo "5/5 compilando"
(cd instalador && makensis -V2 macskin.nsi)
ls -lh MacSkin-Setup.exe
