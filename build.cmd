@echo off
REM Ultimate JSON Mod Manager — build script (Windows, .NET Framework 4.x)
REM
REM Requires the Roslyn C# compiler. Install Visual Studio Build Tools or set
REM CSC env var to a Roslyn-capable csc.exe (langversion 7+).

setlocal
set "OUT=Ultimate JSON Mod Manager.exe"
set "SRC=src\Program.cs"

if not defined CSC (
    if exist "C:\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe" (
        set "CSC=C:\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
    ) else (
        for /f "delims=" %%i in ('where csc 2^>nul') do set "CSC=%%i"
    )
)

if not defined CSC (
    echo ERROR: Roslyn csc.exe not found.
    echo Install Visual Studio Build Tools or set the CSC environment variable
    echo to a Roslyn-capable csc.exe ^(C# 7+ language support^).
    exit /b 1
)

echo Building with: %CSC%

"%CSC%" /nologo ^
    /target:winexe ^
    /platform:anycpu ^
    /optimize+ ^
    /langversion:7.3 ^
    /out:"%OUT%" ^
    /reference:System.dll ^
    /reference:System.Core.dll ^
    /reference:System.Drawing.dll ^
    /reference:System.Windows.Forms.dll ^
    /reference:System.Web.Extensions.dll ^
    /reference:System.IO.Compression.dll ^
    "%SRC%"

if %ERRORLEVEL% neq 0 (
    echo Build failed.
    exit /b %ERRORLEVEL%
)

echo Build OK: %OUT%
