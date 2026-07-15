# PDFタイトル一括リネーム AI版

このアプリは、PDFプロパティからファイル名を生成する [PDF Title to Filename（非AI版）](https://github.com/fa-yoshinobu/PDF_Title_to_Filename) のAI版です。

非AI版がPDFの Title / Author / Subject / Keywords などのメタデータを使用するのに対し、本アプリはメタデータと現在のファイル名を判断材料に使いません。選択フォルダ直下のPDFについて、先頭2ページをWindows OCRで読み取り、ページ画像と文字レイアウトからCodexが正式タイトルを推定します。推定結果を確認・編集し、チェックしたPDFだけを一括リネームできます。

## 非AI版との使い分け

| 項目 | 非AI版 | このAI版 |
|---|---|---|
| 主な判断材料 | PDFメタデータ | 先頭2ページの画像とOCRレイアウト |
| 向いているPDF | Titleなどが正しく設定されたPDF | メタデータが空・不正確なPDF、スキャンPDF |
| AI通信 | なし | Codex経由でOpenAIへ送信 |
| 出力の性質 | 設定に基づく決定的な組み立て | 文書内容からの確率的な推定 |

PDFメタデータが信頼できる場合は非AI版、メタデータだけでは正式タイトルを判定できない場合はこのAI版が適しています。

詳細な機能要件、画面仕様、安全条件、制約、受入基準は [SPEC.md](SPEC.md) を参照してください。

## 判定に使う情報

- 先頭2ページのページ画像
- OCR文字列
- OCR文字ブロックの位置と大きさ

PDFのタイトルプロパティ、作成者、現在のファイル名はAIの判断材料に使用しません。現在のファイル名は画面上の対応付けにだけ使用します。

## 必要環境

- Windows 10 19041以降、またはWindows 11
- Codex CLI
- ChatGPTアカウントでのCodexログイン
- 日本語OCRを使う場合はWindowsの日本語言語機能

APIキーは不要です。ただしCodexによるタイトル判定ではOpenAIへの通信が発生し、利用中のChatGPTプランのCodex使用量を消費します。

## 初回準備

PowerShellでログインします。

```powershell
codex login
codex login status
```

アプリ上部の「ChatGPTにログイン」からもログインできます。

## 使い方

1. `PdfTitleRenamer.exe`を起動します。
2. PDFがあるフォルダを選択します。サブフォルダは検索しません。
3. 解析対象にチェックを付けて「OCR＋AIでタイトル推定」を押します。
4. 候補名、確信度、判断根拠を確認し、必要なら候補名を編集します。
5. リネーム対象だけにチェックを付けて「選択したPDFをリネーム」を押します。

同名ファイルがある場合は ` (2)`、` (3)` のような連番を付け、既存ファイルを上書きしません。Windowsで使えない文字は除去します。

## 開発用コマンド

```powershell
dotnet build .\PdfTitleRenamer.csproj -c Release
dotnet run --project ..\PdfTitleRenamer.Tests\PdfTitleRenamer.Tests.csproj -c Release
dotnet publish .\PdfTitleRenamer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish
```
