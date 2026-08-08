# Changelog

All notable changes to MdViewer are documented in this file.

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
