# Status of round 2 (what is done, what is not)

## Done and verified (377 Core tests + 60 designer view-model tests pass locally; see README for how that was run)
| Feature | Where |
|---|---|
| Report header (first page), report footer (last page, after the content), page header, page footer | `ReportDesign.ReportHeader/ReportFooter/PageHeader/PageFooter`, `LayoutEngine` |
| Data bands (repeater): header / row / footer, header repeats per page, totals, rows that grow (`CanGrow`, `GrowToHeight`), zebra | `RepeaterElement` |
| Variables, data sources, images (dictionary); variable defaults applied at run time | `ReportDictionary` |
| Aggregates in templates: `Sum/Avg/Min/Max(Source.Field)`, `Count(Source)`, `{Line}`, row fields | `TemplateEngine` |
| QR code, Data Matrix, Code 128 | `Barcodes/` - decoded back with an independent decoder (zxing-cpp): 200/200 |
| Shapes: rectangle, rounded rectangle, ellipse, triangle, right triangle, diamond, pentagon, hexagon, star, 4 arrows | `ShapeElement` |
| Text: Persian, English, Chinese; fonts, sizes, bold/italic, colours, borders, fill, rotation, Persian/Arabic digits, RTL/LTR | `Fonts/` (TrueType parser + subsetter, Arabic shaping, bidi, fallback), `TextShaper` |
| Stimulsoft `.mrt` import | `Import/StimulsoftImporter` - all 26 real reports of silo-sync import and render; every text/image/barcode/line/rectangle is carried over |

## Not done yet
* **Designer UI for the new features** (band editing on the canvas, dictionary panel, font/colour pickers, barcode and shape tools, "Import Stimulsoft…" command, better UI/UX). The designer still works with the previous feature set; the new element types already appear in its property panel generically, but there are no tools to add them.
* **Committed tests** for: layout of bands and data bands, the importer, new PDF output (embedding, shapes, rotation), template aggregates, validator additions. These areas were verified with throw-away scripts and rendered/decoded output, but are not yet in the test projects.
* `DESIGN.md`, README and the sample invoice still describe the previous version.
* Nothing from this round was compiled on .NET 9 or Windows; the WPF and Blazor projects were not touched.
