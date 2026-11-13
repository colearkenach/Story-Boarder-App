# Play Console: answers for the app content forms

These are the forms under **Policy and programs → App content** (Play Console lists them on the dashboard as "Set up
your app"). The answers below match what the app actually does: boards stay on the device, the AdMob SDK collects
the data Google documents for it, and Google Play handles the payment. If the app changes (an account, cloud sync,
analytics, another ad network), these answers must change too.

## Privacy policy

`https://colearkenach.github.io/Story-Boarder-App/privacy-policy.html` (published from `docs/` with GitHub Pages;
the app links to it from ☰ → Privacy policy).

## App access

**All functionality is available without any access restrictions** (no login).

## Ads

**Yes, my app contains ads.**

## Content rating (IARC questionnaire)

- Email: your support email. Category: **All other app types** (it's a productivity tool, not a game).
- Violence, fear, sexuality, language, controlled substances, crude humor, gambling: **No** to all.
- Does the app allow users to interact or exchange content with other users? **No.** Boards are local files;
  sharing a file or a PNG through Android's share sheet happens in other apps.
- Does the app share the user's current physical location with other users? **No.**
- Does the app allow users to purchase digital goods? **Yes** (the one-time Premium purchase).
- Is the app a web browser or search engine? **No** (search only searches the board).
- Expected result: Everyone / PEGI 3 / USK 0, with "In-app purchases" shown.

## Target audience and content

- Target age groups: **13–15, 16–17 and 18+**. Don't tick any group under 13: that puts the app in Google's
  Families program, whose rules for ads and data are much stricter.
- Could the store listing unintentionally appeal to children? **No.**

## News app, COVID-19, government app, health, financial features

- News app: **No**. Government app: **No**. Health features: **none**.
- Financial features: **My app doesn't provide any financial features** (an in-app purchase isn't one).

## Advertising ID

**Yes, my app uses advertising ID.** Purposes: **Advertising or marketing**, **Analytics**, **Fraud prevention,
security, and compliance**. (The AdMob SDK adds the `AD_ID` permission.)

## Data safety

**Data collection and security**

| Question | Answer |
| --- | --- |
| Does your app collect or share any of the required user data types? | **Yes** |
| Is all of the user data collected by your app encrypted in transit? | **Yes** (the ads SDK uses TLS) |
| Which account creation methods does your app support? | **My app does not allow users to create an account** |
| Do you provide a way for users to request that their data be deleted? | **No** (the app keeps no data off the device; the advertising ID is reset or deleted in Android settings) |

**Data types.** Tick these four, from Google's disclosure for the Mobile Ads SDK
([source](https://developers.google.com/admob/android/privacy/play-data-disclosure)). For each, answer the
follow-up questions the same way:

| Data type (section) | Collected | Shared | Processed ephemerally? | Required or optional? | Purposes |
| --- | --- | --- | --- | --- | --- |
| Approximate location (Location), from the IP address | Yes | Yes | No | Required | Advertising or marketing, Analytics, Fraud prevention, security, and compliance |
| App interactions (App activity) | Yes | Yes | No | Required | the same three |
| Diagnostics (App info and performance) | Yes | Yes | No | Required | the same three |
| Device or other IDs (Device or other IDs) | Yes | Yes | No | Required | the same three |

**Not collected:** personal info, financial info, messages, photos and videos, files and docs, contacts, calendar,
health, audio, web browsing. Pictures and files you put on a board never leave the device.

**Purchase history:** the app itself doesn't send purchase data anywhere: Google Play processes the payment, and the
app only stores "Premium: yes" on the device. Google hasn't published guidance for the Billing Library, so if you
prefer to be conservative, also declare **Financial info → Purchase history**: collected, not shared, required,
purpose **App functionality**. Declaring more than you collect isn't a policy problem; declaring less is.

## Main store listing

Text and graphics: see `listing.md` and `graphics/`.

## Pricing and distribution

- **Free.** (A free app can never be changed to paid; Premium is an in-app purchase, so that's fine.)
- Countries: all. Devices: phones and tablets; Chromebooks can stay ticked.

## In-app product (after the first upload to a testing track)

Monetize with Play → Products → **One-time products** → Create:

| Field | Value |
| --- | --- |
| Product ID | `remove_ads` (must match `ProductId` in `StoryBoarder.Android/Billing.cs`; it can never be changed or reused) |
| Name | Premium: no ads |
| Description | Removes all ads, for good. A one-time purchase. |
| Price | $1.99 (Play Console converts it for other countries) |

Then **Activate** it.

## Applying for production (after the 14-day closed test)

Google asks about the closed test, the app and its readiness. Keep notes during the test so these are easy:

- How you recruited testers, and how easy it was.
- What testers used (notes, frames, arrows, pictures, import, export, the Premium purchase with license testers).
- Feedback you got and how you collected it (e.g. a Google Form, messages, the testing track's feedback channel).
- What you changed because of it (each new build uploaded during the test helps show this).
- Audience: writers, novelists, screenwriters, game masters and students planning stories. Value: a touch-first,
  offline whiteboard with no account. First-year installs: an honest guess.
