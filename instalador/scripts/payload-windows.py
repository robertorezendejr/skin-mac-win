# Monta instalador/payload no Windows, sem Mac: tudo baixado de projetos livres.
#   ícones: WhiteSur icon theme (GPL-3.0)   wallpaper: WhiteSur wallpapers Monterey (MIT)
#   cursores: apple_cursor (GPL-3.0)         fonte: Inter (OFL)
# Uso: python payload-windows.py <pasta payload>     Requisitos: pip install --user Pillow resvg-py
import io, os, sys, urllib.request, zipfile
from PIL import Image
import resvg_py

P = sys.argv[1]
ICONES = "https://raw.githubusercontent.com/vinceliuice/WhiteSur-icon-theme/73d8040da51a9ed74e47c7366e7e9ff437601a5c/src/"
WALL = "https://raw.githubusercontent.com/vinceliuice/WhiteSur-wallpapers/5c1d7ca20b8de0a7efe443792c19e49277262e02/4k/"

# nome usado pelo macskin.ps1/macskin.nsi -> arquivo do WhiteSur
MAPA = {
    "Finder": "apps/scalable/file-manager.svg",
    "Safari": "apps/scalable/safari.svg",
    "Google Chrome": "apps/scalable/google-chrome.svg",
    "Mail": "apps/scalable/internet-mail.svg",
    "Microsoft Word": "apps/scalable/ms-word.svg",
    "Microsoft Excel": "apps/scalable/ms-excel.svg",
    "Microsoft PowerPoint": "apps/scalable/ms-powerpoint.svg",
    "Visual Studio Code": "apps/scalable/visual-studio-code.svg",
    "TextEdit": "apps/scalable/accessories-text-editor.svg",
    "Calculator": "apps/scalable/calc.svg",
    "Terminal": "apps/scalable/terminal.svg",
    "Activity Monitor": "apps/scalable/utilities-system-monitor.svg",
    "Screenshot": "apps/scalable/accessories-screenshot.svg",
    "System Settings": "apps/scalable/preferences-system.svg",
    "Google Drive": "apps/scalable/google-drive.svg",
    "VLC": "apps/scalable/vlc.svg",
    "Claude": "apps/scalable/claude.svg",
    "Preview": "apps/scalable/accessories-document-viewer.svg",
    "Antigravity": "apps/scalable/antigravity.svg",
    "OneDrive": "places/scalable/folder-cloud.svg",
    "Microsoft Teams": "apps/scalable/teams-for-linux.svg",
    "App Store": "apps/scalable/system-software-install.svg",
    "Clock": "apps/scalable/preferences-system-time.svg",
    "Copilot": "apps/scalable/copilot.svg",
    "ChatGPT": "apps/scalable/openai.svg",
    "Remote Desktop": "apps/scalable/remote-desktop.svg",
    "Notes": "apps/scalable/basket.svg",
    "Calendar": "apps/scalable/calendar.svg",
    "Photos": "apps/scalable/eog.svg",
    "TrashIcon": "places/scalable/user-trash.svg",
    "FullTrashIcon": "places/scalable/user-trash-full.svg",
    "DocumentsFolderIcon": "places/scalable/folder-documents.svg",
    "DownloadsFolder": "places/scalable/folder-download.svg",
    "PicturesFolderIcon": "places/scalable/folder-images.svg",
    "MusicFolderIcon": "places/scalable/folder-music.svg",
    "MovieFolderIcon": "places/scalable/folder-videos.svg",
    "HomeFolderIcon": "places/scalable/user-home.svg",
    "GenericFolderIcon": "places/scalable/folder.svg",
    "GenericNetworkIcon": "apps/scalable/network-workgroup.svg",
    "DiscoInterno": "devices/scalable/drive-harddisk.svg",
}


def baixar(url):
    with urllib.request.urlopen(url, timeout=60) as r:
        return r.read()


def zip_extrair(url, filtro, destino):
    with zipfile.ZipFile(io.BytesIO(baixar(url))) as z:
        for n in z.namelist():
            novo = filtro(n)
            if novo:
                with open(os.path.join(destino, novo), "wb") as f:
                    f.write(z.read(n))


for d in ("icones", "wallpaper", "cursores", "fontes"):
    os.makedirs(os.path.join(P, d), exist_ok=True)

print("1/4 ícones (WhiteSur)")
for nome, arq in MAPA.items():
    png = resvg_py.svg_to_bytes(svg_string=baixar(ICONES + arq).decode("utf-8"), width=256, height=256)
    Image.open(io.BytesIO(png)).convert("RGBA").save(os.path.join(P, "icones", nome + ".ico"),
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print("   ", nome)

print("2/4 papel de parede Monterey (claro/escuro)")
for origem, nome in (("Monterey-light.jpg", "claro.jpg"), ("Monterey-dark.jpg", "escuro.jpg")):
    with open(os.path.join(P, "wallpaper", nome), "wb") as f:
        f.write(baixar(WALL + origem))

print("3/4 cursores macOS (apple_cursor)")
zip_extrair("https://github.com/ful1e5/apple_cursor/releases/download/v2.0.1/macOS-Windows.zip",
            lambda n: os.path.basename(n) if n.startswith("macOS-Regular-Windows/") and n.endswith((".cur", ".ani")) else None,
            os.path.join(P, "cursores"))

print("4/4 fonte Inter")
zip_extrair("https://github.com/rsms/inter/releases/download/v4.1/Inter-4.1.zip",
            lambda n: os.path.basename(n) if os.path.basename(n).startswith("InterVariable") and n.endswith(".ttf")
            else ("LICENSE-Inter.txt" if os.path.basename(n) == "LICENSE.txt" else None),
            os.path.join(P, "fontes"))
print("payload pronto")
