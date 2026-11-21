# Changelog

All notable changes are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/) and the project uses [Semantic Versioning](https://semver.org/).

## [1.0.0] - Unreleased

### Added
- `.art` report format (versioned JSON, millimetres) with text, rectangle, line, image and table elements.
- `AReport.Core`: serializer, design validator, `{placeholder}` template engine, value resolver (objects, dictionaries, `DataTable`), layout engine with table pagination and repeating elements, dependency-free PDF 1.4 writer (standard fonts, JPEG/PNG images with alpha, clipping, compression).
- `ReportExporter`, a drop-in replacement for the old `PdfExporter` (same call shape, same silent skip of missing images, same output checks).
- `AReport.Blazor`: `AddAReport()`, `IReportBrowser` (download / blob URL), `<ReportViewer>`.
- `AReport.Designer`: WPF designer following `DESIGN.md` (tokens, responsive layout, undo/redo, keyboard, PDF preview).
- Test suites for Core, the designer view-model and the Blazor registration; design-system rules enforced by tests.

### Known limitations
- Latin (Windows-1252) text only; see the README.
