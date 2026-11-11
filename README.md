# Story Boarder

A small whiteboard for planning stories on Windows and Android phones and tablets (C# / .NET 10 /
[Avalonia](https://avaloniaui.net/)): sticky notes, frames, text, pictures and arrows on an infinite, zoomable board,
with an importer for Miro's **Export to CSV** files. The Android app shows a banner ad (AdMob); the Windows app has no ads.

## Run it

Windows (also runs on macOS and Linux):

```bash
dotnet run --project StoryBoarder.Desktop
```

Or open `StoryBoarder.sln` in Visual Studio / Rider and run `StoryBoarder.Desktop`.

Android, on a running emulator or a phone connected with USB debugging:

```bash
dotnet build StoryBoarder.Android -t:Run
```

### What you need

- The [.NET 10 SDK](https://dotnet.microsoft.com/download).
- For Android: the .NET Android workload (`dotnet workload install android`, from an admin terminal), and an
  Android SDK with platform 36. If Android Studio is installed, the build uses its JDK and Android SDK on its own;
  `dotnet build StoryBoarder.Android -t:InstallAndroidDependencies -p:AcceptAndroidSdkLicenses=True` fetches
  anything missing.

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

On Android, the open board is also kept in the app's private storage whenever the app goes into the background, so
nothing is lost if Android closes it; it reopens where you left off, still linked to the file it came from.

## Controls

With a mouse and keyboard:

| Action | How |
| --- | --- |
| Pan | Drag empty space, Space+drag or middle-drag anywhere, or arrow keys with nothing selected |
| Zoom | Mouse wheel, Ctrl+= / Ctrl+-, Ctrl+0 to fit |
| Add | Sticky note: double-click the board or **N** · Text: **T** · Frame: **F** · Picture: toolbar **Image**, paste (Ctrl+V) or drop files |
| Move | Drag a note, picture or text, or a frame anywhere on its body or title (its contents come along); arrow keys nudge (Shift = further) |
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

On a touch screen:

| Action | How |
| --- | --- |
| Pan / zoom | Drag empty space with one finger; pinch with two (they pan too) |
| Select / move | Tap an item; drag it to move it |
| Add / edit | Double-tap the board for a new note; double-tap a note, text or frame title to edit it. Back finishes editing |
| Resize / arrows | Drag the square at a selected item's corner, or one of the dots around a selected note |
| Select several | Turn on the select-area button in the toolbar, then drag across the board |
| Selection actions | The bar that appears over a selection: edit, color, duplicate, delete and more |
| More | Long-press a note, frame, arrow or the board |

On phones, New / Open / Save / Import / Export are under the ☰ menu at the top left.

## Ads (Android)

The Android app shows an [AdMob](https://admob.google.com/) adaptive banner under the board. Before showing ads it
asks for consent where the law requires it (EEA, UK, Switzerland) with Google's consent form (UMP); those users can
change their choice later from ☰ → **Ad privacy choices**. The banner takes no space until an ad has loaded.

The project ships with **Google's sample IDs, which only show test ads**. Before publishing:

1. Create the app in AdMob and a **banner** ad unit.
2. Put the app ID in `StoryBoarder.Android/Properties/AndroidManifest.xml` (`com.google.android.gms.ads.APPLICATION_ID`)
   and the ad unit ID in `StoryBoarder.Android/Ads.cs` (`BannerUnitId`).
3. In AdMob, set up the GDPR message under **Privacy & messaging** (the consent form shows what you configure there).
4. Publish an `app-ads.txt` on your developer website (the one in your Play Store listing).

Never tap real ads in your own app while testing; AdMob can suspend the account for it. Use the sample IDs, or add
your phone as a test device in AdMob.

## Publishing on Google Play

1. Bump `<Version>` in `Directory.Build.props` if you like, and **always** bump `ApplicationVersion` in
   `StoryBoarder.Android/StoryBoarder.Android.csproj` (every upload needs a higher number).
2. Make an upload key once, and keep it (and its passwords) safe, outside the repository:

   ```bash
   keytool -genkeypair -v -keystore storyboarder-upload.keystore -alias upload -keyalg RSA -keysize 2048 -validity 10000
   ```

   (`keytool` is in `C:\Program Files\Android\Android Studio\jbr\bin`.)
3. Build the signed app bundle:

   ```bash
   dotnet publish StoryBoarder.Android -c Release -p:AndroidKeyStore=true -p:AndroidSigningKeyStore=C:\path\to\storyboarder-upload.keystore -p:AndroidSigningKeyAlias=upload -p:AndroidSigningKeyPass=env:STORYBOARDER_KEY_PASS -p:AndroidSigningStorePass=env:STORYBOARDER_KEY_PASS
   ```

   with the password in the `STORYBOARDER_KEY_PASS` environment variable. The `.aab` lands in
   `StoryBoarder.Android/bin/Release/net10.0-android/publish/`.
4. Upload it in Play Console, with Play App Signing turned on. The store listing also needs a privacy policy URL,
   the **Contains ads** declaration, the Data safety form (the ads SDK collects the advertising ID and device data),
   a content rating, a 512×512 icon and screenshots for phones and tablets.

The Android app ID is `com.blackflamestudios.storyboarder` (`ApplicationId` in the Android project). It can't be
changed after the first upload.

## Building the Windows installer

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
- replaces an earlier Story Boarder or Miro Lite install, if there is one.

To ship an update, bump `<Version>` in `Directory.Build.props` and rebuild; installing the new MSI
replaces the old version. The MSI isn't code-signed, so Windows SmartScreen will warn on first run
("More info" → "Run anyway") until you sign it with a certificate.

## Project layout

```
Directory.Build.props         version number and Avalonia version, shared by the projects
StoryBoarder.Core/            the whole app, shared by Windows and Android
  Views/MainView.axaml(.cs)   board UI: toolbar, canvas, pan/zoom, mouse and touch input, editing, menus, files
  Views/MainWindow.axaml      the desktop window around it
  Views/Icons.axaml           toolbar icons (drawn shapes, so they work on every platform)
  ViewModels/                 board state, selection, undo/redo (whole-board snapshots), arrows
  Services/MiroCsvImporter.cs CSV parsing and auto-layout
  Services/BoardFile.cs       .storyboard load/save (JSON)
  Services/ImageStore.cs      pictures, kept out of undo snapshots
  Controls/FitText.cs         sticky-note text that shrinks to fit
  Controls/SelectionAdorner.cs selection outlines and handles, drawn at screen size
StoryBoarder.Desktop/         Windows (and macOS/Linux) app: Program.cs, icon, publish profile for the installer
StoryBoarder.Android/         Android app: activity, AdMob banner and consent (Ads.cs, AdBanner.cs), icons
Installer/
  Package.wxs                 what the MSI installs
```
