## Download
- [Windows x64 ZIP]({{DOWNLOAD_URL}})
- [SHA-256 checksum]({{CHECKSUM_URL}})

## Getting started
Extract the ZIP file and run `PdfTitleRenamer_AI.exe`. The ZIP contains only this executable. Full license texts are embedded and available in the app's license window.

Requirements: Windows 10 version 2004 or later, or Windows 11 (x64). The .NET runtime and PDFium are included. AI analysis requires Codex CLI and a signed-in account. AI analysis sends images of the selected first 1 (default), 2, or 5 PDF pages and OCR text to OpenAI through Codex; review your data handling requirements before using it.

## Changes
### Added
- Choose Light, Standard or Detailed analysis in that order, with Light selected by default.
- Switch page count, image size, OCR text limits and reasoning effort together, with the selected limits explained in the interface.
- Light uses one page, images up to 1600 pixels on the longest side, up to 60 OCR lines per page and low reasoning effort.
- Standard uses two pages, up to 2400 pixels, 150 OCR lines per page and medium reasoning effort. Detailed uses five pages, up to 2400 pixels, 300 OCR lines per page and high reasoning effort.

### Changed
- Prioritize larger OCR text lines when reducing the text sent to Codex, preserving title evidence and page coordinates.
- Refresh the README, specification and lossless fictional-data screen example for the new profile selector.
- Heavier profiles require explicit selection; the app does not automatically repeat analysis at a higher setting. Actual token savings and accuracy depend on the document and model and have not been benchmarked against live AI requests.

<!-- virustotal:start -->
{{VIRUSTOTAL_CONTENT}}
<!-- virustotal:end -->
