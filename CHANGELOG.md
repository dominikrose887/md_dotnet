# Changelog

All notable changes to MdViewer are documented in this file.

## [1.1.8] — 2026-09-30

### Fixed
- **Blank preview** — WebView2 `NavigateToString` loads via `data:text/html` URIs; link interception no longer cancels those navigations (regression in 1.1.7 that left Preview empty).

## [1.1.7] — 2026-09-30

### Fixed
- **External links** — http(s) links in the preview open in the system browser instead of navigating the in-app WebView (which previously trapped you on a page you could not leave).
- **Markdown cross-links** — relative `.md` links open as document tabs with proper rendered preview (no more raw markdown text). Use **Back** / **Alt+Left** to return to the previous document.
- **Sidebar layout gap** — closing the Files sidebar no longer leaves an empty strip on the left; editor/preview-only modes no longer reserve phantom column space.
- **Heading / anchor jumps** — in-document `#` links are more reliable (instant scroll, sync suppress, slug fallback).
- **Split view sync** — preview↔editor scroll and click-to-navigate are more stable (longer ownership window, ignore link clicks, editor click jumps preview, preview click moves caret).

## [1.1.6] — 2026-08-08

### Fixed
- **Preview ↔ editor scroll sync** — replaced unreliable height-ratio sync with VS Code-style source-line mapping (`data-line` / `code-line`). Editor and preview now track the same markdown block, including after view-mode switches and preview clicks.

## [1.1.5] — 2026-08-08

### Fixed
- **Preview ↔ editor scroll sync** — switching between Preview, Split, and Editor now restores the active scroll position. Preview scroll is queried live on mode change, and both panes stay aligned even when one is hidden.
- **Status bar document stats** — word / character / paragraph / reading-time counts update when opening or switching tabs (no longer stuck at zeros).

### Removed
- **Typewriter mode** — removed from the toolbar, settings, and editor services.

## [1.1.2] — 2026-07-23

### Notes
- Prior GitHub release (`V1.1.2`). See release assets for installer.
