; Mac Skin: instalador por usuário (sem admin). Compilar: makensis macskin.nsi
Unicode true
!include "MUI2.nsh"

Name "Mac Skin"
OutFile "../MacSkin-Setup.exe"
RequestExecutionLevel user
InstallDir "$LOCALAPPDATA\MacSkin"
SetCompressor /SOLID lzma
BrandingText "Mac Skin"

!define UNINST "Software\Microsoft\Windows\CurrentVersion\Uninstall\MacSkin"
!define PS 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$INSTDIR\macskin.ps1"'
!define PSHIDDEN '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "$INSTDIR\macskin.ps1"'

!define MUI_ICON "payload/icones/Finder.ico"
!define MUI_UNICON "payload/icones/TrashIcon.ico"
!define MUI_COMPONENTSPAGE_SMALLDESC
!define MUI_FINISHPAGE_TEXT "Visual macOS aplicado.$\r$\n$\r$\nO tema muda sozinho: claro às 07h, escuro às 19h. Para trocar na hora, use Iniciar › Mac Skin.$\r$\n$\r$\nPara desfazer tudo: Iniciar › Mac Skin › Desinstalar (ou Configurações › Aplicativos)."
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "PortugueseBR"

Var Comp

Section "Base: dock no centro, azul Apple, transparência, claro/escuro automático" SecBase
  SectionIn RO
  SetOutPath "$INSTDIR"
  File "macskin.ps1"
  StrCpy $Comp "base"
SectionEnd

Section "Dock do macOS com Liquid Glass (esconde a barra do Windows)" SecDock
  SetOutPath "$INSTDIR"
  !if /FileExists "MacSkin.exe"   ; compilado no Windows por app\compilar.cmd
    File "MacSkin.exe"
  !endif
  File "dock.ps1"
  File "barra.ps1"
  File "botoes.ps1"
  File "vigia.ps1"
  StrCpy $Comp "$Comp,dock"
SectionEnd

Section /o "Cursores do macOS" SecCur
  SetOutPath "$INSTDIR\cursores"
  File "payload/cursores/*.*"
  StrCpy $Comp "$Comp,cursores"
SectionEnd

Section "Papel de parede do macOS (claro e escuro)" SecWall
  SetOutPath "$INSTDIR\wallpaper"
  File "payload/wallpaper/*.jpg"
  StrCpy $Comp "$Comp,wallpaper"
SectionEnd

Section "Ícones do macOS (dock, Iniciar, pastas, Lixeira)" SecIco
  SetOutPath "$INSTDIR\icones"
  File "payload/icones/*.ico"
  StrCpy $Comp "$Comp,icones"
SectionEnd

Section "Fonte Inter (parecida com a SF Pro)" SecFont
  SetOutPath "$INSTDIR\fontes"
  File "payload/fontes/*.*"
  StrCpy $Comp "$Comp,fonte"
SectionEnd

Section "-Aplicar"
  SetOutPath "$INSTDIR"
  WriteUninstaller "$INSTDIR\Desinstalar.exe"
  WriteRegStr HKCU "${UNINST}" "DisplayName" "Mac Skin"
  WriteRegStr HKCU "${UNINST}" "UninstallString" '"$INSTDIR\Desinstalar.exe"'
  WriteRegStr HKCU "${UNINST}" "DisplayIcon" "$INSTDIR\Desinstalar.exe"
  WriteRegStr HKCU "${UNINST}" "InstallLocation" "$INSTDIR"
  WriteRegDWORD HKCU "${UNINST}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST}" "NoRepair" 1

  CreateDirectory "$SMPROGRAMS\Mac Skin"
  CreateShortCut "$SMPROGRAMS\Mac Skin\Modo claro.lnk" "powershell.exe" '${PSHIDDEN} -Acao claro' "$INSTDIR\Desinstalar.exe" 0 SW_SHOWMINIMIZED
  CreateShortCut "$SMPROGRAMS\Mac Skin\Modo escuro.lnk" "powershell.exe" '${PSHIDDEN} -Acao escuro' "$INSTDIR\Desinstalar.exe" 0 SW_SHOWMINIMIZED
  CreateShortCut "$SMPROGRAMS\Mac Skin\Desinstalar Mac Skin.lnk" "$INSTDIR\Desinstalar.exe"
  ; aplica o tema certo a cada login
  CreateShortCut "$SMSTARTUP\Mac Skin tema.lnk" "powershell.exe" '${PSHIDDEN} -Acao auto' "" 0 SW_SHOWMINIMIZED

  DetailPrint "Aplicando (componentes: $Comp)..."
  nsExec::ExecToLog '${PS} -Acao instalar -Componentes "$Comp"'
  Pop $0
  StrCmp $0 "0" +2
    MessageBox MB_ICONEXCLAMATION "O script terminou com código $0. Veja $INSTDIR\macskin.log"
SectionEnd

Section "Uninstall"
  DetailPrint "Restaurando o Windows original..."
  nsExec::ExecToLog '${PS} -Acao remover'
  Pop $0
  Delete "$SMSTARTUP\Mac Skin tema.lnk"
  RMDir /r "$SMPROGRAMS\Mac Skin"
  DeleteRegKey HKCU "${UNINST}"
  RMDir /r "$INSTDIR"
SectionEnd
