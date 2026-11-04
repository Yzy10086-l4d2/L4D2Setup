@echo off
rem ============================================================
rem  Build the installer exe with the C# compiler shipped with
rem  Windows. No SDK, no NuGet, no network. Keep this pure ASCII.
rem ============================================================
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [X] csc.exe not found - needs .NET Framework 4.x
  exit /b 1
)

set "OUT=%~dp0..\..\..\..\L4D2Setup.exe"
set "ICONARG="
if exist "%~dp0app.ico" set "ICONARG=/win32icon:%~dp0app.ico"
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /out:"%OUT%" %ICONARG% ^
  /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll ^
  "%~dp0AssemblyInfo.cs" "%~dp0Theme.cs" "%~dp0Program.cs" "%~dp0Core.cs" "%~dp0Launch.cs" "%~dp0Dup.cs" "%~dp0Catalog.cs" "%~dp0Parts.cs" "%~dp0Tweaks.cs" "%~dp0Work.cs" "%~dp0Mods.cs" "%~dp0Identify.cs" "%~dp0SelfTest.cs" "%~dp0CatTree.cs" "%~dp0CatView.cs" "%~dp0Ui.cs"
if errorlevel 1 (
  echo.
  echo [X] Build failed.
  exit /b 1
)
echo [OK] Built: %OUT%
