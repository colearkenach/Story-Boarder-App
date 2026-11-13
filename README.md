# Story Boarder

A small whiteboard for planning stories on Windows and Android phones and tablets (C# / .NET 10 /
[Avalonia](https://avaloniaui.net/)): sticky notes, frames, text, pictures and arrows on an infinite, zoomable board,
with an importer for whiteboards exported as CSV. The Android app shows a banner ad (AdMob); the Windows app has no ads.

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

## Importing a CSV export

Click **Import CSV** (Ctrl+I), or drop a `.csv` onto the window. The importer reads whiteboard exports that
have one text item per row with a blank row between frames, and:

- turns each block of rows into a **frame**, titled with the block's first row;
- lays the remaining rows out as **sticky notes** in a grid inside the frame, sized to their text;
- colors notes that share a label (e.g. every `Class: …`, `Power: …`, `Manifest: …`) so related notes stand out;
- restores bullet lists the export flattened (`- one - two` → one bullet per line) and fixes `&amp;`-style entities.

The CSV doesn't carry positions, colors or connectors, so the layout is generated. Rearrange things
and **Save** to keep your layout as a `.storyboard` file (plain JSON; pictures are stored inside it).
Boards saved as `.mlboard` (the app's older file extension) still open.

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

There are two IDs, and they go in different places:

| ID | Looks like | Where it goes |
| --- | --- | --- |
| App ID | `ca-app-pub-…~…` (with a `~`) | `StoryBoarder.Android/Properties/AndroidManifest.xml` (`com.google.android.gms.ads.APPLICATION_ID`) |
| Banner ad unit ID | `ca-app-pub-…/…` (with a `/`) | `StoryBoarder.Android/Ads.cs` (`BannerUnitId`, in the Release branch) |

Debug builds always use Google's sample banner unit, which only shows test ads, so testing on your own phone can't
load or click real ads (AdMob can suspend an account for that). Release builds, the ones uploaded to Google Play, use
your banner unit. Your app ID is used by both. Still to do in AdMob:

1. In AdMob, set up the GDPR message under **Privacy & messaging** (the consent form shows what you configure there).
2. Publish `store/app-ads.txt` at the root of your developer website (the one in your Play Store listing). See
   `store/README.md`.

To have one of your own devices always get test ads (even from Release builds), run the app on it, find
`setTestDeviceIds(Arrays.asList("…"))` in the device log, and add that ID to `TestDeviceIds` in `Ads.cs`.

## Premium (no ads)

A one-time purchase through Google Play Billing removes the ads for good: ☰ → **Remove ads ($1.99)**, with the price
shown in the user's own currency. Premium users also skip the consent form, since no ads load at all. The code is in
`StoryBoarder.Android/Billing.cs`.

- Ownership is checked with Google Play every time the app starts or comes back to the foreground. The purchase
  belongs to the Google account, so it carries over to reinstalls and new phones, and a refund brings the ads back.
- Each purchase is acknowledged straight away; Google refunds purchases that aren't acknowledged within 3 days.
- Debug builds add ☰ → **Debug: pretend to have Premium**, to try the ad-free app before the product exists (and
  for screenshots without ads). It stays on, even after the app restarts, until you turn it off.

To switch it on in Play Console (once a build with this code is on a testing track):

1. Set up a **payments profile** (Settings → Payments profile), if you haven't.
2. Create a one-time product (Monetize with Play → Products → One-time products) with the product ID **`remove_ads`**
   (it must match `ProductId` in `Billing.cs`), a name such as "Premium: no ads", and the price, then activate it.
3. Add yourself and your testers as **license testers** (Settings → License testing). Their purchases are free test
   transactions, so the whole flow can be tried without paying.

Until the product exists, "Remove ads" says Premium isn't available yet.

## Publishing on Google Play

The full launch checklist (account, testing requirements, AdMob, store listing) is in
[`store/README.md`](store/README.md), with the listing text, form answers and graphics next to it. Building and
uploading a version:

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
4. Upload it in Play Console, with Play App Signing turned on.

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
- replaces an earlier install, including one from before the app was renamed.

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
StoryBoarder.Android/         Android app: activity, AdMob banner and consent (Ads.cs, AdBanner.cs), Premium
                              purchase (Billing.cs), icons
Installer/
  Package.wxs                 what the MSI installs
docs/                         website and privacy policy, published with GitHub Pages
store/                        Google Play launch checklist, listing text, form answers, graphics, app-ads.txt
```
