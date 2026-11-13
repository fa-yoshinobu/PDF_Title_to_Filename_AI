@echo off
setlocal
chcp 65001 >nul

set "NO_PAUSE="
if /I "%~1"=="--no-pause" set "NO_PAUSE=1"

pushd "%~dp0"
if errorlevel 1 (
    echo [ERROR] プロジェクトフォルダーを開けません。
    exit /b 1
)

set "PROJECT=src\PdfTitleRenamer\PdfTitleRenamer.csproj"
set "CONFIGURATION=Release"
set "PUBLISH_DIR=artifacts\release"
set "EXE=%PUBLISH_DIR%\PdfTitleRenamer_AI.exe"
set "ZIP=artifacts\release\PdfTitleRenamer_AI-win-x64.zip"
set "RESULT=0"

for %%I in ("%PUBLISH_DIR%") do set "PUBLISH_PATH=%%~fI"
for %%I in ("%ZIP%") do set "ZIP_PATH=%%~fI"
if /I not "%PUBLISH_PATH%"=="%CD%\artifacts\release" (
    echo [ERROR] 発行先がプロジェクト外です: %PUBLISH_PATH%
    set "RESULT=1"
    goto :finish
)
if /I not "%ZIP_PATH%"=="%CD%\artifacts\release\PdfTitleRenamer_AI-win-x64.zip" (
    echo [ERROR] ZIP出力先がプロジェクト外です: %ZIP_PATH%
    set "RESULT=1"
    goto :finish
)

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] dotnet SDKが見つかりません。
    echo https://dotnet.microsoft.com/download から.NET SDKをインストールしてください。
    set "RESULT=9009"
    goto :finish
)

where powershell.exe >nul 2>&1
if errorlevel 1 (
    echo [ERROR] powershell.exeが見つかりません。
    set "RESULT=9009"
    goto :finish
)

echo [1/4] NuGetパッケージを復元しています...
dotnet restore "%PROJECT%"
if errorlevel 1 goto :failed

echo [2/4] Releaseビルドを実行しています...
dotnet build "%PROJECT%" -c "%CONFIGURATION%" --no-restore
if errorlevel 1 goto :failed

echo [3/4] 自己完結型の単一EXEを発行しています...
dotnet publish "%PROJECT%" -c "%CONFIGURATION%" -o "%PUBLISH_DIR%" --no-restore
if errorlevel 1 goto :failed

if not exist "%EXE%" (
    echo [ERROR] 発行済みEXEが生成されませんでした: %EXE%
    set "RESULT=1"
    goto :finish
)

powershell.exe -NoProfile -File "%~dp0.github\scripts\package_release.ps1" -OutputDirectory "%PUBLISH_DIR%"
if errorlevel 1 goto :failed

echo.
echo ビルドが完了しました。
echo EXE: %CD%\%EXE%
echo ZIP: %CD%\%ZIP%
goto :finish

:failed
set "RESULT=%ERRORLEVEL%"
if "%RESULT%"=="0" set "RESULT=1"
echo.
echo [ERROR] ビルドに失敗しました。終了コード: %RESULT%

:finish
popd
if not defined NO_PAUSE pause
exit /b %RESULT%
