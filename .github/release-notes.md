## Download
- [Windows x64 ZIP]({{DOWNLOAD_URL}})
- [SHA-256 checksum]({{CHECKSUM_URL}})

## Getting started
Extract the ZIP file and run `PdfTitleRenamer_AI.exe`. The ZIP contains only this executable. Full license texts are embedded and available in the app's license window.

Requirements: Windows 10 version 2004 or later, or Windows 11 (x64). The .NET runtime and PDFium are included. AI analysis requires Codex CLI and a signed-in account. AI analysis sends images of the selected first 1 (default), 2, or 5 PDF pages and OCR text to OpenAI through Codex; review your data handling requirements before using it.

## Changes
### Changed
- Align local and CI executable, fixed-name distribution ZIP and SHA-256 output under `artifacts/release/`.
- Require the VirusTotal API SHA-256 to match every published executable before reporting a completed scan.

<!-- virustotal:start -->
{{VIRUSTOTAL_CONTENT}}
<!-- virustotal:end -->
