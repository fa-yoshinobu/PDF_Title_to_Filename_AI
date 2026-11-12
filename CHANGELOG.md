# Changelog

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

All notable changes are recorded from this point onward.

## [Unreleased]

## [0.4.0] - 2026-11-13

### Added

- Offer Light, Standard and Detailed analysis profiles, defaulting to Light. Profiles control page count, image dimensions, OCR line limits and reasoning effort together.

### Changed

- Prioritize larger OCR text lines when selecting title evidence, and retain their page coordinates while limiting the text sent to Codex.
- Show the selected profile's limits in the interface and update the README, specification and lossless screen example. Heavier profiles are selected explicitly, without automatic reanalysis.

## [0.3.0] - 2026-11-13

### Added

- Open one PDF or one folder by dragging it onto the window.
- Show confidence values, low-confidence warnings and colored status labels in the refreshed review screens.
- Rename files when only letter case changes, including the final rename preview.

### Changed

- Distribute a ZIP containing only `PdfTitleRenamer_AI.exe`; embed the MIT license, third-party notices and all 23 third-party license documents for in-app reading.
- Move application sources to `src/PdfTitleRenamer`, the specification to `docs`, and build outputs to named folders under `artifacts`.
- Update the README with current instructions and lossless PNG screen examples using fictional data.
- Use the temporary OCR image folder as the Codex working directory instead of the source PDF folder.

### Fixed

- Distinguish login and analysis timeouts from user cancellation, and restart the App Server after interrupted or failed transport operations.
- Ignore notifications for other threads and retry an empty completed response once.
- Remove stale temporary OCR folders left by interrupted runs.
- Reject distribution builds containing companion files instead of silently packaging more than one EXE.

## [0.2.0] - 2026-11-08

### Added

- Added a single-PDF selection mode for analyzing and renaming one file without loading its siblings.

### Changed

- Updated the bundled .NET runtime to 10.0.14 and PDF rendering dependencies to PDFtoImage 5.4.0, SkiaSharp 4.155.0 and PDFium 159.0.8138.
- Added a persistent notice that PDF page content, including confidential or personal information, is sent to OpenAI through Codex for analysis.
- Refined the WPF workflow with Digital Agency design guidance, a candidate detail editor, selection counts, and a full rename preview with final collision-safe names.
- Added 1/2/5-page analysis, skipping existing candidates by default and preserving old candidates on failed or cancelled reanalysis.
- Improved title instructions to distinguish document titles from chapter headings and identify supporting pages.
- Fixed tall-page OCR bounds, directory/name collisions, reserved Windows filenames, and Unicode truncation; added executable regression checks.

- Replaced the application icon with a PDF page and filename field design with an AI badge, with 9 sizes from 16 to 256 px, so it can be told apart from the non-AI edition.

## [0.1.7] - 2026-10-27

### Changed

- Standardized fixed-name Windows ZIP assets, ZIP SHA-256 checksums and English release notes.

### Fixed

- Unified VirusTotal completion checks and preserved unrelated release notes during rescans.
