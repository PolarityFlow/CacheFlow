@echo off
rem ============================================================================
rem  CacheFlow build script - compiles src\Program.cs to CacheFlow.exe
rem  Uses the C# compiler that ships with Windows (.NET Framework 4.x).
rem  No SDK, no NuGet, no internet required.
rem ============================================================================
setlocal
cd /d "%~dp0"

set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%FW%\csc.exe" (
    echo ERROR: .NET Framework 4.x compiler not found.
    exit /b 1
)

"%FW%\csc.exe" /nologo /target:winexe /platform:anycpu /optimize+ ^
    /win32icon:CacheFlow.ico ^
    /resource:assets\AppIcon_256.png,appicon.png ^
    /out:CacheFlow.exe ^
    /r:"%FW%\WPF\PresentationFramework.dll" ^
    /r:"%FW%\WPF\PresentationCore.dll" ^
    /r:"%FW%\WPF\WindowsBase.dll" ^
    /r:"%FW%\System.Xaml.dll" ^
    src\Program.cs

if errorlevel 1 (
    echo BUILD FAILED
    exit /b 1
)
echo BUILD OK: CacheFlow.exe
