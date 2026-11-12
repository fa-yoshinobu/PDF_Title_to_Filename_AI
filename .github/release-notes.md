## Download
- [Windows x64 ZIP]({{DOWNLOAD_URL}})
- [SHA-256 checksum]({{CHECKSUM_URL}})

## Getting started
Extract the ZIP file and run `PdfTitleRenamer_AI.exe`. The ZIP contains only this executable. Full license texts are embedded and available in the app's license window.

Requirements: Windows 10 version 2004 or later, or Windows 11 (x64). The .NET runtime and PDFium are included. AI analysis requires Codex CLI and a signed-in account. AI analysis sends images of the selected first 1, 2 (default), or 5 PDF pages and OCR text to OpenAI through Codex; review your data handling requirements before using it.

## Changes
### Added
- Select a single PDF for analysis and renaming without loading other PDFs in its folder. Folder selection remains available.
- Choose the first 1, 2 (default), or 5 pages for title analysis.
- Review and edit a candidate in a dedicated detail pane, open its source PDF, and inspect every final filename before renaming.
- Keep a visible warning that PDF page images and OCR text, including any confidential or personal information they contain, are sent to OpenAI through Codex.

### Changed
- Refined the interface with reference to the Digital Agency design system and refreshed the application icon.
- Skip existing candidates by default; explicitly opt in to reanalysis when needed. Failed or cancelled reanalysis preserves the previous candidate with a visible status.
- Improved title instructions to distinguish document titles from chapter headings and cite supporting page numbers.
- Updated the bundled .NET runtime to 10.0.14 and rendering libraries to PDFtoImage 5.4.0, SkiaSharp 4.155.0 and PDFium 159.0.8138.

### Fixed
- Bound both image dimensions for Windows OCR, including tall portrait pages.
- Avoid directory and batch filename collisions, Windows reserved names and splitting Unicode surrogate pairs.
- Update selection counts and action buttons immediately; preserve candidates when loading a new selection fails.

<!-- virustotal:start -->
{{VIRUSTOTAL_CONTENT}}
<!-- virustotal:end -->
