# Changelog

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

All notable changes are recorded from this point onward.

## [Unreleased]

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
