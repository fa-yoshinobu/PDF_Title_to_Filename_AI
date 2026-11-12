## Download
- [Windows x64 ZIP]({{DOWNLOAD_URL}})
- [SHA-256 checksum]({{CHECKSUM_URL}})

## Getting started
Extract the ZIP file and run `PdfTitleRenamer_AI.exe`. The ZIP contains only this executable. Full license texts are embedded and available in the app's license window.

Requirements: Windows 10 version 2004 or later, or Windows 11 (x64). The .NET runtime and PDFium are included. AI analysis requires Codex CLI and a signed-in account. AI analysis sends images of the selected first 1, 2 (default), or 5 PDF pages and OCR text to OpenAI through Codex; review your data handling requirements before using it.

## Changes
### Added
- Open one PDF or one folder by dragging it onto the window.
- Review confidence values, low-confidence warnings and colored status labels in the refreshed interface.
- Rename files when only letter case changes and inspect those changes in the final preview.

### Changed
- Distribute only one EXE, with the MIT license, third-party notices and all 23 third-party license documents embedded for in-app reading.
- Organize sources under `src`, the specification under `docs`, and build outputs under `artifacts`.
- Refresh the README and add lossless PNG screen examples made with fictional data.
- Run Codex from the temporary OCR image folder instead of the source PDF folder.

### Fixed
- Distinguish login and analysis timeouts from cancellation, and restart the App Server after interrupted or failed transport operations.
- Ignore notifications for other threads and retry an empty completed response once.
- Remove stale temporary OCR folders left by interrupted runs.
- Check that the distribution contains only the application EXE before creating the ZIP.

<!-- virustotal:start -->
{{VIRUSTOTAL_CONTENT}}
<!-- virustotal:end -->
