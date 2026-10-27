@echo off
rem Compila o MacSkin.exe com o compilador C# que já vem no Windows (.NET Framework 4.8): sem instalar nada, sem admin.
rem Saída: instalador\MacSkin.exe (fora do git, como todo .exe).
chcp 65001 >nul
setlocal
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
set "WPF=%FW%\WPF"
set "OUT=%~dp0..\instalador\MacSkin.exe"
set "ICO="
if exist "%~dp0..\instalador\payload\icones\Finder.ico" set "ICO=/win32icon:%~dp0..\instalador\payload\icones\Finder.ico"

"%FW%\csc.exe" /nologo /target:winexe /optimize+ /platform:x64 /out:"%OUT%" %ICO% ^
  /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\WindowsBase.dll" /r:"%FW%\System.Xaml.dll" ^
  /r:"%WPF%\UIAutomationClient.dll" /r:"%WPF%\UIAutomationTypes.dll" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Microsoft.VisualBasic.dll /r:System.Management.dll ^
  "%~dp0*.cs"
if errorlevel 1 (echo Falha ao compilar o MacSkin.exe. & exit /b 1)
echo MacSkin.exe compilado.
