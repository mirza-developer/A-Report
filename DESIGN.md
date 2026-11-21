# A-Report Designer — Design System

The rules for the look, layout and behaviour of **AReport.Designer**, the Windows (WPF) app that creates `.art` report files.
Everything in `src/AReport.Designer` must follow this document. Rules are numbered (**R1…R16**) so reviews can cite them.
A test (`DesignSystemTests`) checks R1 and R2 automatically on every build.

## 1. Principles

| Principle | What it means in the designer |
|---|---|
| **Clear** | One obvious next step. Commands are labelled with words, not guessed icons. Nothing important is hidden in a menu. |
| **Calm** | Neutral chrome so the report page is the most colourful thing on screen. One accent colour, used for action and selection only. |
| **Precise** | Millimetres everywhere, a 1 mm snap grid, numeric properties for every element, no "approximately". |
| **Responsive** | The window can be 720 px or 2560 px wide; panels adapt instead of clipping (section 5). |
| **Accessible** | Keyboard-complete, visible focus, contrast that passes WCAG AA, names for screen readers. |

## 2. Tokens

Tokens are the **only** place values are defined. They live in `Themes/Tokens.xaml` under exactly these keys (R1).
Views reference them with `{DynamicResource Key}`; code reads them with `FindResource`.

### 2.1 Colour

| Token | Value | Use | Contrast |
|---|---|---|---|
| `Color.Primary` | `#1F3A5F` | Primary buttons, headings of selected state | 11.5 : 1 on white |
| `Color.PrimaryHover` | `#274B7A` | Hover of primary | 8.9 : 1 with white text |
| `Color.PrimaryPressed` | `#17304F` | Pressed of primary | — |
| `Color.Accent` | `#E05A47` | Brand accent in the logo and the "unsaved" dot only | — |
| `Color.Selection` | `#2563EB` | Selected element outline, handles, focus ring | 5.2 : 1 on white, 4.2 : 1 on canvas |
| `Color.Background` | `#F3F4F6` | App chrome (toolbar, panels) | — |
| `Color.Surface` | `#FFFFFF` | Inputs, cards, the report page | — |
| `Color.Canvas` | `#E5E7EB` | Workspace behind the page | — |
| `Color.Border` | `#D1D5DB` | Dividers (decorative) | — |
| `Color.BorderStrong` | `#8A94A3` | Outline of inputs and buttons | 3.1 : 1 (meets the 3 : 1 rule for controls) |
| `Color.TextPrimary` | `#111827` | Body text | 17.7 : 1 on white |
| `Color.TextSecondary` | `#4B5563` | Labels, hints | 7.6 : 1 on white |
| `Color.TextDisabled` | `#9CA3AF` | Disabled text only (exempt from contrast rules) | 2.5 : 1 |
| `Color.TextOnPrimary` | `#FFFFFF` | Text on primary | 11.5 : 1 |
| `Color.Danger` | `#B91C1C` | Errors, destructive actions | 6.5 : 1 |
| `Color.Success` | `#15803D` | Confirmation | 5.0 : 1 |
| `Color.Warning` | `#B45309` | Warnings | 5.0 : 1 |
| `Color.Guide` | `#93C5FD` | Page-margin guides on the canvas | — |

Every colour token has a matching brush named `Brush.<Name>` (for example `Brush.Primary`).

### 2.2 Typography

Font: **Segoe UI Variable** with fallback **Segoe UI** (`Font.Family`). Sizes are device-independent pixels.

| Token | Size | Weight | Use |
|---|---|---|---|
| `Font.Size.Caption` | 12 | Regular | Status bar, hints |
| `Font.Size.Body` | 13 | Regular | Everything by default |
| `Font.Size.Subtitle` | 15 | SemiBold | Panel headings |
| `Font.Size.Title` | 20 | SemiBold | Dialog titles |

### 2.3 Spacing, size and shape

Spacing follows a **4 px grid** (R3): only these values appear in margins and padding.

| Token | Value |
|---|---|
| `Space.1` / `Space.2` / `Space.3` / `Space.4` / `Space.6` / `Space.8` | 4 / 8 / 12 / 16 / 24 / 32 |
| `Inset.1` … `Inset.8` | Uniform `Thickness` equal to the matching space step (for `Margin`/`Padding`) |
| `Inset.H2` / `Inset.H3` | Horizontal-only padding of 8 / 12 (inputs, buttons) |
| `Size.Control` | 32 (height of buttons, inputs, tool buttons) |
| `Size.Toolbar` | 48 |
| `Size.StatusBar` | 28 |
| `Size.PropertiesWidth` | 300 |
| `Size.ToolboxWide` / `Size.ToolboxCompact` | 148 / 56 |
| `Size.Handle` | 8 |
| `Size.CanvasMin` | 320 (the canvas never gets narrower, R5) |
| `Radius.Control` | 4 |
| `Radius.Panel` | 6 |

## 3. Components

| Component | Rules |
|---|---|
| **Button** | 32 px high, 4 px radius, 1 px `BorderStrong` outline. Three variants: *Primary* (filled `Primary`, one per window), *Secondary* (default), *Ghost* (no outline, for toolbars). Hover, pressed, disabled and keyboard-focus states are all styled. |
| **Tool button** | Toolbox entries: 32 px high, icon-free in v1 — a text label (hidden below 1100 px, where the first letter is shown) so the meaning is never ambiguous. |
| **Input** | 32 px high, `Surface` fill, `BorderStrong` outline, 2 px `Selection` outline on focus. Numeric inputs accept `,` or `.` as decimal separator. |
| **Panel** | `Background` fill, 1 px `Border` divider on the side that touches the canvas, `Space.4` inner padding, `Subtitle` heading. |
| **Property row** | Label (120 px, `TextSecondary`, `Caption`) above or beside its input. One row per property, millimetres labelled in the name (`X mm`). |
| **Status bar** | 28 px, `Caption` text: status message (left), zoom, page size and selection (right). Errors use `Danger` plus the word "Error" — colour is never the only signal. |
| **Dialog** | Modal, centred on the owner, `Title` heading, primary action on the right, `Esc` cancels, `Enter` confirms. |

## 4. Canvas

* The page is drawn at true size: **1 mm = 96 / 25.4 DIP** × zoom. Zoom range 25 %–400 %, default 100 %.
* The page is `Surface` with a soft shadow on the `Canvas` workspace; margins are drawn as dashed `Guide` lines.
* Selected element: 1.5 px `Selection` outline and eight 8 px square handles (white fill, `Selection` border).
* **Snap:** moves and resizes snap to whole millimetres. Arrow keys nudge 1 mm, `Shift`+arrow 10 mm.
* Hit tolerance is 4 px so thin lines are selectable.
* Cursors: move (`SizeAll`) over an element, the matching resize cursor over a handle, arrow elsewhere.
* Text placeholders such as `{Customer.Name}` are shown exactly as typed; **Preview PDF** shows the result with real or generated sample data.
* Tables show a header and as many sample rows as fit in the element's height; the layout engine ignores that height and grows with the data.

## 5. Responsive behaviour

Breakpoints are measured on the window's width in DIPs. Minimum window size is **720 × 520**.

| Mode | Width | Toolbox | Properties panel | Toolbar |
|---|---|---|---|---|
| **Compact** | < 900 | 56 px, first letter of each tool | Hidden; the **Properties** toggle in the toolbar slides it over the canvas (300 px, right edge, shadow) | Zoom buttons are hidden (use `Ctrl`+wheel or `Ctrl`+`+`/`-`); the Properties toggle appears |
| **Regular** | 900 – 1399 | 148 px with labels | Docked, 300 px | Full |
| **Wide** | ≥ 1400 | 148 px with labels | Docked, 300 px; the canvas centres the page | Full |

R4: layouts are built with `Grid` and star sizing — no fixed window-sized widths. R5: the canvas never shrinks below 320 px; panels give way first.

## 6. Interaction and keyboard

| Action | Shortcut |
|---|---|
| New / Open / Save / Save as | `Ctrl+N` / `Ctrl+O` / `Ctrl+S` / `Ctrl+Shift+S` |
| Undo / Redo | `Ctrl+Z` / `Ctrl+Y` |
| Duplicate / Delete | `Ctrl+D` / `Delete` |
| Preview PDF | `F5` |
| Zoom in / out / 100 % | `Ctrl++` / `Ctrl+-` / `Ctrl+0`; `Ctrl`+mouse wheel |
| Nudge | Arrow keys (1 mm), `Shift`+arrows (10 mm) |

R6: every command is reachable by keyboard. R7: every change goes through one undo step (a drag is one step). R8: destructive actions on unsaved work ask first; everything else is undoable instead of confirmed.

## 7. Accessibility

* R9: text contrast ≥ 4.5 : 1, control outlines ≥ 3 : 1 (the table above is computed from these exact values).
* R10: every focusable control shows a 2 px `Selection` focus ring (`FocusVisual.Ring`). Never remove it.
* R11: every control that is not self-describing sets `AutomationProperties.Name`.
* R12: target size ≥ 32 × 32 px.
* R13: colour is never the only carrier of meaning (errors say "Error", unsaved changes show "●" *and* the word "unsaved" in the title bar tooltip/status).

## 8. Content and language

R14: sentence case ("Save as", not "Save As"); verbs for commands ("Preview PDF"); measurements always with unit in the label; error messages say what happened and what to do ("The file is not a valid .art document. Open a file saved by A-Report.").

## 9. Rules checklist for contributors

| # | Rule | Enforced |
|---|---|---|
| R1 | Every colour, size and font comes from a token in `Tokens.xaml`; no hex colours or magic sizes in views or code. | **Test** |
| R2 | Every token named in this document exists in `Tokens.xaml`. | **Test** |
| R3 | Margins and paddings use `Space.*` (4 px grid). | Review |
| R4 | `Grid` + star sizing; no fixed window-sized layouts. | Review |
| R5 | The canvas keeps ≥ 320 px; panels collapse first. | Review |
| R6 | Every command has a keyboard route. | Review |
| R7 | One undo step per user action. | Review |
| R8 | Confirm only before losing unsaved work. | Review |
| R9 | Contrast ≥ 4.5 : 1 text, ≥ 3 : 1 controls. | Review |
| R10 | Keep the focus ring. | Review |
| R11 | Name icon-only or ambiguous controls for screen readers. | Review |
| R12 | Targets ≥ 32 px. | Review |
| R13 | Colour is never the only signal. | Review |
| R14 | Sentence case, verbs, units, helpful errors. | Review |
| R15 | The designer edits the Core model directly; it never has its own copy of the file format. | Review |
| R16 | Dark theme: add a second dictionary with the same keys — views must not need changes (that is what R1 buys). | Roadmap |

## 10. Out of scope for v1

Dark theme (R16), right-to-left UI, icon set, TrueType/Unicode text editing (the engine currently draws the 14 standard PDF fonts with Latin text).
