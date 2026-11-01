# Story Boarder

A small whiteboard for planning stories on Windows (C# / .NET 9 / WPF): sticky notes, frames, text,
pictures and arrows on an infinite, zoomable board, with an importer for Miro's **Export to CSV** files.

## Run it

```bash
dotnet run --project StoryBoarder
```

Or open `StoryBoarder.sln` in Visual Studio / Rider and press F5.

## Importing a Miro CSV

Click **Import CSV** (Ctrl+I), or drop a `.csv` onto the window. Miro's export has one text item
per row with a blank row between frames, so the importer:

- turns each block of rows into a **frame**, titled with the block's first row;
- lays the remaining rows out as **sticky notes** in a grid inside the frame, sized to their text;
- colors notes that share a label (e.g. every `Class: …`, `Power: …`, `Manifest: …`) so related notes stand out;
- restores bullet lists Miro flattened (`- one - two` → one bullet per line) and fixes `&amp;`-style entities.

The CSV doesn't carry positions, colors or connectors, so the layout is generated. Rearrange things
and **Save** to keep your layout as a `.storyboard` file (plain JSON; pictures are stored inside it).
Boards saved as `.mlboard` (from when the app was called Miro Lite) still open.

## Controls

| Action | How |
| --- | --- |
| Pan | Drag empty space, Space+drag or middle-drag anywhere, or arrow keys with nothing selected |
| Zoom | Mouse wheel, Ctrl+= / Ctrl+-, Ctrl+0 to fit |
| Add | Sticky note: double-click the board or **N** · Text: **T** · Frame: **F** · Picture: toolbar **Image**, paste (Ctrl+V) or drop files |
| Move | Drag a note, picture or text, or a frame anywhere on its body (its contents come along); arrow keys nudge (Shift = further) |
| Resize | Drag the handle at the bottom-right of a selected item (pictures keep their shape) |
| Arrows | Select a note, then drag one of the dots on its sides onto another note, or onto empty space to create a new connected note. Click an arrow to select it; right-click to reverse or delete |
| Edit text | Double-click a note, text or frame title, or Enter; Esc / Ctrl+Enter to finish |
| Select | Click; Ctrl+click to add; Shift+drag for a selection box; Ctrl+A for all |
| Color | Pick a swatch in the toolbar (applies to selected notes and to new notes) |
| Copy / cut / paste | Ctrl+C / Ctrl+X / Ctrl+V (pasting text from another app makes a sticky note) |
| Duplicate / delete | Ctrl+D / Delete |
| Undo / redo | Ctrl+Z / Ctrl+Y |
| Search | Ctrl+F, type to highlight matches, Enter / Shift+Enter to jump between them |
| Jump to a frame | **Frames ▾** in the toolbar |
| Export a picture | **Export** (Ctrl+E) saves the selection, or the whole board if nothing is selected, as a PNG |
| More | Right-click a note, frame, arrow or the board |

## Building the installer

```bash
dotnet build Installer -c Release
```

This publishes a single self-contained `StoryBoarder.exe` (no .NET install needed on the target PC) and
packages it with [WiX Toolset v5](https://wixtoolset.org/) into
`Installer/bin/Release/StoryBoarder-Setup.msi`. The installer:

- installs to `C:\Program Files\Story Boarder` (asks for admin once);
- adds Start Menu and desktop shortcuts;
- makes `.storyboard` files open in Story Boarder when double-clicked;
- shows up in Settings → Apps, where it can be uninstalled;
- replaces an earlier Miro Lite install, if there is one.

To ship an update, bump `<Version>` in `StoryBoarder/StoryBoarder.csproj` and rebuild; installing the new MSI
replaces the old version. The MSI isn't code-signed, so Windows SmartScreen will warn on first run
("More info" → "Run anyway") until you sign it with a certificate.

## Project layout

```
StoryBoarder/
  MainWindow.xaml(.cs)        board UI: toolbar, canvas, pan/zoom, dragging, editing, shortcuts
  ViewModels/                 board state, selection, undo/redo (whole-board snapshots)
  Services/MiroCsvImporter.cs CSV parsing and auto-layout
  Services/BoardFile.cs       .storyboard load/save (JSON)
  Services/ImageStore.cs      pictures, kept out of undo snapshots
  ViewModels/ConnectorViewModel.cs  arrows and their geometry
  Controls/FitText.cs         sticky-note text that shrinks to fit
Installer/
  Package.wxs                 what the MSI installs
```
