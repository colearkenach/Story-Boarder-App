# Launching on Google Play

Everything needed for the Play Store listing lives in this folder; the website with the privacy policy is in
`docs/`. The steps are in order, starting with what can be done for free before the Play Console account exists.

## What's ready

| What | Where |
| --- | --- |
| Store listing text (name, short and full description, category) | `listing.md` |
| Answers for the App content forms (Data safety, content rating, ads, target audience…) | `play-console-answers.md` |
| App icon, 512×512 | `graphics/icon-512.png` |
| Feature graphic, 1024×500 | `graphics/feature-graphic.png` (edit `graphics/source/feature-graphic.html`, then run `graphics/source/render.ps1`) |
| Phone screenshots (5, 1080×1920) and tablet screenshots (4, 2560×1440) | `graphics/screenshots/` (see "Screenshots" below) |
| AdMob `app-ads.txt` | `app-ads.txt` |
| Website and privacy policy (GitHub Pages) | `../docs/` |
| Privacy policy link inside the app | ☰ → Privacy policy (`MainApplication.cs`) |

The Release bundle (`dotnet publish StoryBoarder.Android -c Release`) has been checked against Play's technical
rules: target API 36, minimum Android 7.0 (API 24), native code for arm64-v8a and x86_64 (phones, tablets,
Chromebooks), and 16 KB memory-page support.

## 1. Before paying the $25 (all free)

1. **Contact email.** Replace `REPLACE-WITH-CONTACT-EMAIL` (twice) in `docs/privacy-policy.html` with the support
   address you'll give Play Console. It's shown publicly on the store page anyway.
2. **Publish the website.** Push, then on GitHub: the repository's Settings → Pages → Build and deployment →
   *Deploy from a branch* → `main`, folder `/docs`. A minute later these should load:
   - <https://colearkenach.github.io/Story-Boarder-App/> (the "Website" in the store listing)
   - <https://colearkenach.github.io/Story-Boarder-App/privacy-policy.html> (the "Privacy policy")
3. **app-ads.txt.** AdMob looks for it at the root of the developer website's host, `https://colearkenach.github.io/app-ads.txt`,
   which a project site can't serve. Create a public repository named exactly **`colearkenach.github.io`**, put
   `app-ads.txt` from this folder at its root, and turn on Pages for it the same way (branch `main`, folder `/`).
   (If Black Flame Studios gets its own domain later, put the file at that domain's root and use that domain as the
   website instead.)
4. **AdMob consent messages.** AdMob → Privacy & messaging → create and publish a **European regulations** (GDPR)
   message for Story Boarder, and optionally a **US state regulations** message. The app already shows whatever is
   published there. Without the GDPR message, users in Europe get no consent form and very few ads.
5. **Upload key.** Make it once with the `keytool` command in the main README (section "Publishing on Google Play"),
   and keep the `.keystore` file and its password together somewhere safe outside the repository, with a backup
   (a password manager or cloud drive). If it's ever lost, Play Console can reset it, but that takes a few days.
6. **Line up testers.** New personal developer accounts must run a closed test with **at least 12 testers who stay
   opted in for 14 days in a row** before the app can go public. Ask 15–20 people (some drop out), and collect the
   Gmail addresses of their Google accounts; each needs an Android phone or tablet. Ask them to really use the app
   and send feedback: Google asks about it afterwards. Writing groups, friends and family, and tester-swap
   communities are the usual sources.

## 2. The day the account is paid for

1. **Create the developer account** (personal). Google verifies your identity, which can take a few days.
   *Organization* accounts skip the 12-tester rule but need a registered business with a D-U-N-S number.
2. **Create the app:** name "Story Boarder", default language English (United States), App, Free.
3. **Fill in the App content forms** from `play-console-answers.md` and the **main store listing** from `listing.md`
   and `graphics/`.
4. **Payments profile** (Settings → Payments profile), needed to sell Premium.
5. **Upload a build to Internal testing.** Bump `ApplicationVersion` in `StoryBoarder.Android.csproj` for every
   upload, build the signed bundle (main README), upload it, and add yourself as a tester. Internal testing has no
   review wait, and the first upload is what unlocks creating in-app products.
6. **Create the `remove_ads` product** (details in `play-console-answers.md`) and activate it. Add yourself under
   Settings → **License testing**, install from the internal testing link, and buy Premium. License testers aren't
   charged. Check that the ads disappear, and come back after reinstalling.
7. **Start the closed test:** Testing → Closed testing → create a track, add the testers (an email list, or a Google
   Group they join), release the same build to it, and send them the opt-in link. Add them as license testers too, so
   they can try Premium for free. The 14 days count from when 12 testers have opted in.

## 3. After 14 days

1. Dashboard → **Apply for production**. Google asks about the closed test (see the end of
   `play-console-answers.md`); review usually takes up to a week.
2. Release to **Production**. A staged rollout (e.g. 20% first) is optional.

## 4. After launch

- AdMob → Apps → Story Boarder → App settings → **Add app store details**, to link the AdMob app to the Play listing.
  AdMob then reviews the app; until it's linked and approved, ads are served only in limited numbers.
- AdMob checks `app-ads.txt` within about a day of the link and shows the result under Apps → app-ads.txt.
- Keep your own phone in `TestDeviceIds` (`Ads.cs`), so you only ever see test ads: clicking your own real ads can
  get the AdMob account suspended.

## Screenshots

Ready to upload, in the order to upload them:

| Play Console slot | Files |
| --- | --- |
| Phone screenshots | `graphics/screenshots/phone-1-plot.png` … `phone-5-overview.png` (1080×1920) |
| 7-inch tablet screenshots | `graphics/screenshots/tablet-1-overview.png` … `tablet-4-search.png` (2560×1440) |
| 10-inch tablet screenshots | the same four tablet files |

Google's rules: PNG or JPEG without transparency; phones need 2 to 8 (4 or more to be eligible for featuring), each
side 320–3840 px; tablets 4 to 8, 16:9 or 9:16, each side 1080–7680 px. These meet all of them.

They're real screenshots of the app on a Pixel Fold (outer screen for the phone, inner screen for the tablet),
showing the sample board `graphics/source/sample.storyboard`, a made-up novel plan. The raw screenshots, cropped to the
app without Android's status bar and taskbar, are in `graphics/screenshots/raw/`; `graphics/source/make_screenshots.py`
puts them on the purple background with the captions (edit the captions there and run it again).

To take new ones after the app changes: install a Debug build (`dotnet build StoryBoarder.Android -t:Install`), open
`sample.storyboard` with ☰ → Open, and turn on ☰ → **Debug: pretend to have Premium** so there's no test ad. Take
the screenshots (Power + Volume down), crop them like the raw ones, and run the script.
