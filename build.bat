@echo off
rem ============================================================
rem  Build the installer exe with the C# compiler shipped with
rem  Windows. No SDK, no NuGet, no network. Keep this pure ASCII.
rem ============================================================
chcp 65001 >nul
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [X] csc.exe not found - needs .NET Framework 4.x
  exit /b 1
)

set "OUT=%~dp0..\..\..\..\L4D2Setup.exe"
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /out:"%OUT%" ^
  /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  "%~dp0Program.cs" "%~dp0Core.cs" "%~dp0Work.cs" "%~dp0Mods.cs" "%~dp0Ui.cs"
if errorlevel 1 (
  echo.
  echo [X] Build failed.
  exit /b 1
)
echo [OK] Built: %OUT%