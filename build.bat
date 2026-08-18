@echo off
setlocal
chcp 65001 >nul 2>nul

REM ============================================
REM   Hugo - Markdown Client Build Script
REM ============================================

REM Change to script directory
cd /d "%~dp0"

REM Check dotnet availability
where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet not found. Please install .NET SDK 8.0
    echo Download: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

echo [1/4] Cleaning old publish directory...
if exist "publish" (
    rmdir /s /q "publish"
    echo       Removed old publish directory
)

echo [2/4] Building project...
dotnet build Huge.csproj -c Release
if errorlevel 1 (
    echo [ERROR] Build failed!
    pause
    exit /b 1
)

echo [3/4] Publishing single-file exe...
REM EnableCompressionInSingleFile=true compresses the single-file exe (saves ~30-40MB)
dotnet publish Huge.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
if errorlevel 1 (
    echo [ERROR] Publish failed!
    pause
    exit /b 1
)

echo [4/4] Cleaning up...
REM Remove PDB debug symbols (not needed for end users)
if exist "publish\*.pdb" (
    del /q "publish\*.pdb"
    echo       Removed PDB debug symbols
)

echo [5/5] Embedding Hugo executable (optional)...
REM Ask whether to embed hugo.exe (adds ~50MB, but no Hugo install needed on target machines)
set /p embedHugo="Embed hugo.exe into publish folder? (Y/N, default N): "
if /i "%embedHugo%"=="Y" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0embed-hugo.ps1"
    if errorlevel 1 (
        echo       [WARN] Hugo embedding failed, app will fall back to Hugo in system PATH.
    )
) else (
    echo       Skipped Hugo embedding. Users can select hugo.exe manually when starting the server.
)

echo.
echo ============================================
echo   Build complete!
set "OUTPUT=%~dp0publish\Huge.exe"
echo   Output: %OUTPUT%
echo ============================================
echo.

REM Ask to launch
set /p launch="Launch Huge.exe now? (Y/N): "
if /i "%launch%"=="Y" (
    start "" "publish\Huge.exe"
)

endlocal
pause