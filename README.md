## Logging

Crash and runtime logs are written continuously to:

`%LocalAppData%\MdViewer\logs\mdviewer-YYYYMMDD-HHmmss.log`

Open them from **Tools → Open Log Folder**. Every write is flushed immediately so the last lines survive unexpected exits. Unhandled UI/AppDomain/task exceptions are captured automatically.

## Build and run

```bash
dotnet run --project src/MdViewer
```

Open a file or folder from the command line:

```bash
dotnet run --project src/MdViewer -- path\to\notes.md
dotnet run --project src/MdViewer -- path\to\docs\
```

Publish a standalone build (recommended before registering as default opener):

```bash
dotnet publish src/MdViewer -c Release -r win-x64 --self-contained false -o publish
```

Then run `publish\MdViewer.exe`, and use **Tools → Set as Default .md Viewer**.

Run the tests:

```bash
dotnet test
```

## Requirements

- .NET 8 SDK
- Windows 10/11
- [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (usually pre-installed on Windows 11)

## Features

- **File operations** — New, Open, Open Folder, Save, Save As
- **Recent files** — last 10 opened files
- **Export** — HTML and PDF
- **View modes** — Editor, Viewer, or Split (resizable)
- **Sidebar** — recursive `.md` tree for the current folder
- **Find** — `Ctrl+F` search in the editor (AvalonEdit SearchPanel)
- **Syntax highlighting** — light/dark markdown highlighting
- **Dark / light theme** — UI, editor, and preview
- **Scroll sync** — editor ↔ preview
- **Interactive task lists** — check/uncheck `- [ ]` / `- [x]` items in preview; updates the markdown source
- **External changes** — a clean file reloads when it changes on disk; unsaved work asks before replacing the editor
- **CLI open** — pass a `.md` file or folder path
- **File association** — register/unregister as default `.md` opener (per user, no admin)

## Keyboard shortcuts

| Action | Shortcut |
|--------|----------|
| New | Ctrl+N |
| Open | Ctrl+O |
| Open Folder | Ctrl+Shift+O |
| Save | Ctrl+S |
| Save As | Ctrl+Shift+S |
| Find | Ctrl+F |
| Replace | Ctrl+H |
| Format document | Ctrl+Shift+F |
| Bold / Italic / Link | Ctrl+B / Ctrl+I / Ctrl+K |
| Inline code / Strikethrough | Ctrl+Shift+C / Ctrl+Shift+X |
| Heading 1–3 | Ctrl+1 / Ctrl+2 / Ctrl+3 |
| Find next / prev | Enter / Shift+Enter |
| Close find | Esc |

## External changes

MdViewer watches every open file.

- A tab you have not edited reloads the editor and preview when the file changes on disk. The view stays where you were reading. A caret at the end of the file follows text appended there.
- Unsaved edits raise a bar: **Reload** takes the copy on disk, **Keep** stays on yours. The same disk revision is not asked about again until the file changes again.
- Saving writes your version and clears the bar. If you save while the bar is open, the status line reads that your version was saved.
- If the file is deleted, the bar lets you keep the open copy. Closing the tab asks whether to save it back.
- A burst of writes (another editor, a sync tool, or a script) is applied once, after the file settles. A brief truncate-and-rewrite does not flash an empty document.
- A file that is no longer plain text is not loaded on its own; the bar asks first.

## Writing aids

- **Focus mode** — dims paragraphs outside the current one
- **Regex find/replace** — toggle `.*` in the find bar
- **Live stats** — words, characters, paragraphs, reading time
- **Code fence highlighting** — language-aware coloring inside \`\`\` blocks
- **KaTeX + Mermaid** — math and diagrams in preview (requires network for CDN)
- **Image drag & drop** — copies into `./assets` and inserts markdown
- **Formatter** — More → Format document (or Ctrl+Shift+F)

## Tech stack

- WPF (.NET 8)
- [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) — editor, highlighting, find
- [Markdig](https://github.com/xoofx/markdig) — Markdown parsing
- [WebView2](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) — preview + PDF export
