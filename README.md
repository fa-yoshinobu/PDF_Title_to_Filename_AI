# PDFタイトル一括リネーム AI版

このアプリは、PDFプロパティからファイル名を生成する [PDF Title to Filename（非AI版）](https://github.com/fa-yoshinobu/PDF_Title_to_Filename) のAI版です。

非AI版がPDFの Title / Author / Subject / Keywords などのメタデータを使用するのに対し、本アプリはメタデータと現在のファイル名を判断材料に使いません。選択フォルダ直下のPDFについて、PDFiumで先頭2ページを画像化してWindows OCRで読み取り、ページ画像と文字レイアウトからCodexが正式タイトルを推定します。推定結果を確認・編集し、チェックしたPDFだけを一括リネームできます。

## 非AI版との使い分け

| 項目 | 非AI版 | このAI版 |
|---|---|---|
| 主な判断材料 | PDFメタデータ | 先頭2ページの画像とOCRレイアウト |
| 向いているPDF | Titleなどが正しく設定されたPDF | メタデータが空・不正確なPDF、スキャンPDF |
| AI通信 | なし | Codex経由でOpenAIへ送信 |
| 出力の性質 | 設定に基づく決定的な組み立て | 文書内容からの確率的な推定 |

PDFメタデータが信頼できる場合は非AI版、メタデータだけでは正式タイトルを判定できない場合はこのAI版が適しています。

詳細な機能要件、画面仕様、安全条件、制約、受入基準は [SPEC.md](SPEC.md) を参照してください。

## AIへのデータ送信に関する注意

> **注意:** AI解析を実行すると、PDF先頭2ページの画像とOCR結果がCodex経由でOpenAIへアップロードされます。機密情報・個人情報を含むPDFは、所属組織や利用中のサービスの規則を確認してから処理してください。

PDFの現在のファイル名とメタデータはAIへ送信しません。ただし、ページ画像内に写っている文字、図、写真などはページ画像の一部として送信されます。この注意文はアプリのメイン画面にも常時表示します。

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

PDFiumはアプリに同梱されるため、PDF表示ソフトやPopplerの別途インストールは不要です。APIキーも不要ですが、Codexによるタイトル判定ではOpenAIへの通信が発生し、利用中のChatGPTプランのCodex使用量を消費します。

## PDF対応

- 通常PDFとスキャンPDFは、同じPDFium描画経路で処理します。
- 閲覧パスワードなしで開ける権限制限付きAES-256 PDFにも対応します。
- 開くためのパスワードが必要なPDFは、保護を解除してから処理してください。
- PDFiumが対応していないセキュリティ方式や破損PDFは、対象行を解析失敗として次のPDFへ進みます。

## Codex使用量の表示

解析中は画面上部に `利用枠 35%使用 / 今回 82,500 tokens` の形式で表示します。

- `今回` は、その一括解析でCodexへ送った入力、画像、推論、出力を含む合計トークン数です。
- 利用枠の使用率とリセット時刻は、契約プランから提供された場合だけ表示します。
- クレジット残高、個別上限、プラン種別は、提供された項目だけツールチップへ表示します。
- ChatGPTログイン方式のため、API料金の正確なドル・円換算は表示しません。

## 初回準備

PowerShellでログインします。

```powershell
codex login
codex login status
```

アプリ上部の「ChatGPTにログイン」からもログインできます。

## 使い方

1. `PdfTitleRenamer_AI.exe`を起動します。
2. PDFがあるフォルダを選択します。サブフォルダは検索しません。
3. 解析対象にチェックを付けて「OCR＋AIでタイトル推定」を押します。
4. 候補名、確信度、判断根拠を確認し、必要なら候補名を編集します。
5. リネーム対象だけにチェックを付けて「選択したPDFをリネーム」を押します。

画面上部の「ライセンス」から、アプリのバージョン、MIT License、同梱コンポーネントの第三者ライセンス表示を確認できます。

同名ファイルがある場合は ` (2)`、` (3)` のような連番を付け、既存ファイルを上書きしません。Windowsで使えない文字は除去します。

## 開発用コマンド

`build.bat`をダブルクリックすると、Releaseビルド、単一EXEの発行、ZIP作成をまとめて実行します。

```bat
build.bat
```

コマンドラインや自動処理から一時停止なしで実行する場合は、`build.bat --no-pause`を使用します。成果物は`publish\PdfTitleRenamer_AI.exe`と`PdfTitleRenamer_AI-win-x64.zip`です。

```powershell
dotnet build .\PdfTitleRenamer.csproj -c Release
dotnet format .\PdfTitleRenamer.csproj --verify-no-changes --no-restore
dotnet publish .\PdfTitleRenamer.csproj -c Release -o .\publish
```

アプリアイコン、単一EXE配布、高DPI設定は非AI版と共通の製品ファミリー構成です。

## ライセンス

本ソフトウェア本体は [MIT License](LICENSE) で公開します。アプリ上部の「ライセンス」から本文を表示できます。PDFium、PDFtoImage、SkiaSharpなど同梱コンポーネントには、それぞれのライセンスが適用されます。詳細はアプリ内の「第三者ライセンス」タブ、[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)、`ThirdPartyLicenses` を参照してください。
