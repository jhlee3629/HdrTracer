# HdrTracer

[한국어](README.md) | English

<img src="docs/1.png" width="700" alt="HdrTracer main window">

<img src="docs/2.png" width="700" alt="Extension (txt) search results">

A fast file search tool for Windows that finds files on NTFS drives by name.

## Features

- Search by file name or extension (supports patterns like `*.jpg`, `*.png`)
- Powerful search syntax — exclusion (`-word`), folder filter (`D:\Backup\`), size (`>100MB`), date (`>2026-01`, `>week`), name wildcards (`IMG_*_edit`)
- Searches multiple drives at once (fixed disks like C, D, plus USB)
- Real-time updates via the USN journal — adding, deleting, or renaming a file is reflected in results immediately
- Results refresh automatically when a USB drive is plugged in or removed
- Pinned searches — pin frequent queries (📌) and run them instantly with `Ctrl+1~9`
- Work with results — right-click (open / show in folder / search in this folder / copy path),
  drag & drop into File Explorer, export to CSV, selection count & total size in the status bar
- Excluded folders — hide system-ish folders like WinSxS or node_modules from results
- Remembers your window size & position, column widths, and sort order
- Lives in the tray + optional "Start with Windows" (via Task Scheduler — no UAC prompt at sign-in, off by default)
- Dark theme, Korean / English support (first run follows your Windows display language)

## Requirements

- 64-bit Windows (Windows 11)
- NTFS file system (FAT32 and exFAT are not indexed)
- Administrator privileges

### Why administrator privileges are needed

Reading the MFT and USN journal directly requires low-level access to the volume.
Because of this, a UAC prompt appears each time the app starts.

This access is used **only to read** the volume for search indexing.
The app does not modify or delete any file without an explicit user action.

## Installation

Download `HdrTracer_Setup_*.exe` from the [latest release](../../releases/latest) and run it.

The installer and executable are not code-signed, so the first time you run them
Windows may show a "Windows protected your PC" (SmartScreen) warning.
Click **More info → Run anyway** to proceed.

The .NET runtime is bundled, so there is nothing else to install.

## Usage

1. On launch, the app indexes connected NTFS drives (once, takes a few seconds depending on drive size).
2. Type a file name or extension in the search box and press Enter.

### Search syntax

| Type | Meaning |
|---|---|
| `vacation photo` | names containing both words (space = AND) |
| `*.jpg *.png` | those extensions only |
| `report -draft` | skip names containing 'draft' (`-*.log` excludes an extension) |
| `photo D:\Backup\` | search only under that folder (wrap paths with spaces in `"quotes"`) |
| `*.mp4 >500MB` | size condition (unit required: KB MB GB TB · works alone) |
| `photo >2026-01` · `>week` | modified-date condition (year-month-day · today / week / month / year) |
| `IMG_*_edit` | whole-name shape match (`*` = any run, `?` = one character) |

Full details: app menu (HdrTracer ▼) → **Search help**.

### Pinned searches

Click the search box to see recent searches. Pin one with the 📌 button to keep it at the
top of the list, and run pinned searches instantly with `Ctrl+1~9` (top to bottom).

### Hidden / system items

Items with the hidden + system attribute that don't appear in File Explorer
(e.g. protection folders created by antivirus software, NTFS internal files)
are excluded from results by default. Turn on "Show hidden/system items" in
settings to include them.

## Settings

- Removable drive (USB) indexing on/off
- Close button behavior (minimize to tray / exit)
- Show hidden/system items
- Folder names to exclude from search (e.g. `WinSxS; node_modules`)
- Start with Windows (registered via Task Scheduler — runs at sign-in without a UAC prompt, off by default)

## Privacy & data

- No network communication. File names, paths, and contents are **never sent anywhere**, and no usage/telemetry data is collected. (No internet connection or account required.)
- Index and settings are stored **locally only**:
  - Index cache: `%LocalAppData%\HdrTracer\indexes\`
  - Settings: `%LocalAppData%\HdrTracer\settings.json`
- Deleting the `HdrTracer` folder above after uninstalling removes all stored data.

## Notes

- NTFS only. Other file systems are not indexed.
- No file content search. Only names and paths are searched.
- SmartScreen warning appears because the build is not code-signed.
- Deletion moves items to the **Recycle Bin** (recoverable), not a permanent delete. If system-critical paths are included, an extra warning is shown before deleting.

## License

The code is released under the MIT License.

The app icon was created by [Muhammad_Usman](https://www.flaticon.com/) and
obtained from [Flaticon](https://www.flaticon.com/) (free to use with attribution).
