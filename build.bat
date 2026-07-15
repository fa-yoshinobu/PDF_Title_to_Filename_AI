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

set "PROJECT=PdfTitleRenamer.csproj"
set "CONFIGURATION=Release"
set "PUBLISH_DIR=publish"
set "EXE=%PUBLISH_DIR%\PdfTitleRenamer_AI.exe"
set "ZIP=PdfTitleRenamer_AI-win-x64.zip"
set "LEGACY_ZIP=PdfTitleRenamer-win-x64.zip"
set "RESULT=0"

for %%I in ("%PUBLISH_DIR%") do set "PUBLISH_PATH=%%~fI"
for %%I in ("%ZIP%") do set "ZIP_PATH=%%~fI"
for %%I in ("%LEGACY_ZIP%") do set "LEGACY_ZIP_PATH=%%~fI"
if /I not "%PUBLISH_PATH%"=="%CD%\publish" (
    echo [ERROR] 発行先がプロジェクト外です: %PUBLISH_PATH%
    set "RESULT=1"
    goto :finish
)
if /I not "%ZIP_PATH%"=="%CD%\PdfTitleRenamer_AI-win-x64.zip" (
    echo [ERROR] ZIP出力先がプロジェクト外です: %ZIP_PATH%
    set "RESULT=1"
    goto :finish
)
if /I not "%LEGACY_ZIP_PATH%"=="%CD%\PdfTitleRenamer-win-x64.zip" (
    echo [ERROR] 旧ZIPの場所がプロジェクト外です: %LEGACY_ZIP_PATH%
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
if exist "%PUBLISH_PATH%" rmdir /s /q "%PUBLISH_PATH%"
if exist "%ZIP_PATH%" del /q "%ZIP_PATH%"
if exist "%LEGACY_ZIP_PATH%" del /q "%LEGACY_ZIP_PATH%"
dotnet publish "%PROJECT%" -c "%CONFIGURATION%" -o "%PUBLISH_DIR%" --no-restore
if errorlevel 1 goto :failed

if not exist "%EXE%" (
    echo [ERROR] 発行済みEXEが生成されませんでした: %EXE%
    set "RESULT=1"
    goto :finish
)

echo [4/4] ZIPファイルを作成しています...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '.\%PUBLISH_DIR%\*' -DestinationPath '.\%ZIP%' -CompressionLevel Optimal -Force"
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
