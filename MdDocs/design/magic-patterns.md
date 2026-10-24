# UI Design Brief & Prompts — Kulture Compass
### For Magic Patterns or Claude Design (same prompts work in both)

How to use:
1. Start a new project. Paste **§1 Global context** into the project instructions (or as the first message).
2. Generate **§3 Component sheet** first, then screens one at a time from **§4**. Every screen prompt starts with a one-line
   domain reminder — keep it; it stops the tool drifting.
3. If anything comes back looking like a music/streaming app, paste **§5 Correction prompt**.
4. Export React + Tailwind into `frontend/design-exports/<nn-screen>/`, then run FE slice FE-3.

> **Why you got a music mockup:** words like *culture, taste, artists, affinities, Afrobeats* pull design tools toward
> Spotify-style apps. This version anchors the product as a **marketing research & strategy workspace for clothing brands**,
> bans music UI patterns explicitly, and keeps music-flavoured examples out of the prompts.

---
## 1. Global context (paste first)
```
PRODUCT
"Kulture Compass" is a B2B marketing research and strategy web app (a PWA) for small Nigerian CLOTHING brands —
streetwear labels, fashion designers and lifestyle stores. The founder fills in a brand brief (what they sell, who they sell to,
their marketing goal and budget). The app researches cultural data and produces a written GROWTH PLAN REPORT with:
insights, partnership ideas, campaign ideas and measurable marketing experiments. Every recommendation cites the data it came from.

WHAT IT IS NOT — very important
It is NOT a music app, streaming service, playlist app, artist profile, concert/events app, social network or chatbot.
Do NOT draw: album covers, play/pause buttons, audio players, waveforms, equalisers, "now playing" bars, playlists, track lists,
headphones, vinyl, microphones, concert crowds, follower feeds or chat bubbles.

VISUAL REFERENCES
Think: Notion + Linear + a HubSpot/Semrush research report, styled with the warmth of a Lagos fashion lookbook.
Clean white cards on a warm off-white background, generous whitespace, clear typography, tables and structured cards.
Imagery (sparingly, landing page and empty states only): clothing flat-lays — graphic t-shirts, hoodies, caps, tote bags,
folded fabric, hand-dyed textile swatches, a small boutique rail. Never people performing music.

CORE UI IDEA — "evidence vs interpretation"
Every recommendation shows two clearly labelled parts:
  • "Retrieved data" — teal #0F766E, database icon, label "Retrieved from Qloo"
  • "AI interpretation" — violet #6D28D9, sparkle icon, label "AI-generated"
Evidence items appear as small chips like  [ev_004 · Sample Brand B]  that open a details drawer.
Evidence strength is a pill: Strong / Moderate / Weak. Never show % confidence scores, follower counts or invented metrics.

SAMPLE CONTENT (fictional only)
Brand: "Eko Thread Co." — Lagos streetwear label selling graphic tees, hoodies and caps; goal: grow online sales among 18–30s;
budget ₦250k–₦500k. Data entities: "Sample Brand B" (brand), "Sample Creator C" (person), "Sample Store D" (place),
"Sample Show E" (TV), "minimalist", "streetwear", "thrift culture" (themes).

STYLE
Mobile-first responsive (390 / 768 / 1440). Left sidebar on desktop, bottom tab bar on mobile. Light + dark mode.
Fonts: Space Grotesk (headings), Inter (body), JetBrains Mono (evidence keys). Icons: lucide. Tailwind CSS.
Colours: background #FAF7F2, cards #FFFFFF, text #14122B, primary buttons terracotta #C2410C, accent amber #F2A20C (decoration only),
teal #0F766E = retrieved data, violet #6D28D9 = AI-generated, amber #FEF3C7 background = limitations/warnings.
Accessible: 4.5:1 contrast, visible focus rings, labels on all inputs, icons always with text.
```

## 2. Design tokens (for FE-3 implementation)
| Token | Light | Dark | Use |
|---|---|---|---|
| `--bg` | `#FAF7F2` | `#0F0E1F` | app background |
| `--surface` | `#FFFFFF` | `#1A1833` | cards |
| `--surface-muted` | `#F3EEE6` | `#24213F` | sections, table header |
| `--border` | `#E7E1D8` | `#34305A` | |
| `--ink` | `#14122B` | `#F4F1EA` | primary text |
| `--ink-muted` | `#5B5880` | `#B9B5D6` | secondary text |
| `--primary` | `#C2410C` | `#FB923C` | primary buttons, active nav |
| `--primary-ink` | `#FFFFFF` | `#14122B` | text on primary |
| `--accent` | `#F2A20C` | `#FBBF24` | decoration only (not text on white) |
| `--evidence` / `--evidence-bg` | `#0F766E` / `#E6F4F2` | `#2DD4BF` / `#0F2E2B` | Retrieved from Qloo |
| `--ai` / `--ai-bg` | `#6D28D9` / `#F1EBFD` | `#A78BFA` / `#251A45` | AI-generated |
| `--strong` / `--moderate` / `--weak` | `#15803D` / `#B45309` / `#6B7280` | `#4ADE80` / `#FBBF24` / `#9CA3AF` | strength pills |
| `--warning-bg` | `#FEF3C7` | `#3A2A05` | limitations |
| `--danger` | `#B91C1C` | `#F87171` | errors |
| `--focus` | `#2563EB` | `#93C5FD` | focus ring |
| Radius | 12px cards · 10px inputs · 999px pills | | |
| Spacing | 4px grid; gutter 16px mobile / 32px desktop; max width 1200px | | |
| Type scale | 12/14/16/18/22/28/36/48 (Space Grotesk 600 for ≥ 22) | | |

Logo: minimal compass rose whose north needle forms a stylised "K" — amber needle on an ink circle; wordmark "Kulture Compass".
Motif: abstract geometric line grid inspired by hand-dyed textile (adire-style), 6 % opacity, landing hero + empty states only.

## 3. Component sheet (generate first)
```
(Reminder: marketing research & strategy app for clothing brands — not a music app.)
Create a component sheet for Kulture Compass, light and dark:
1. Buttons: primary (terracotta), secondary outline, ghost, destructive; sm/md/lg; loading + disabled.
2. ProvenanceLabel: "Retrieved from Qloo" (teal, database icon) and "AI-generated" (violet, sparkle icon).
3. EvidenceChip: mono key "ev_004" + name "Sample Brand B" + small type icon (shirt-tag for brand, user for person, map-pin for place,
   tv for show, hash for theme). Teal outline. Hover and focus states.
4. StrengthPill: Strong / Moderate / Weak with tooltip "Based on how the data was retrieved — not a probability."
5. AffinityBar: thin bar with numeric label "Affinity 0.87".
6. Report cards:
   - InsightCard: title; teal panel "What the data shows" with evidence chips; violet panel "Why it matters for your brand";
     strength pill; collapsible Assumptions.
   - PartnershipCard: partner name + type + evidence chip; connection; format (e.g. capsule collection, co-branded pop-up);
     "Why it fits"; "How to test it".
   - CampaignCard: objective, angle, numbered execution steps, success metric, effort badge (Low/Medium/High).
   - ExperimentCard: hypothesis, audience segment, metric, period, success threshold with "Proposed default" badge,
     decision rule (If it works → … / If not → …), status chip, buttons "Start experiment" / "Record result".
7. Banners: Sample data, Mock data, Offline, Limitations (amber), Error (red with Retry).
8. Vertical progress stepper: pending / active / done / failed with a small detail line.
9. Form fields: text, textarea with counter, select, selectable goal cards, chip input with type dropdown.
10. Empty state, skeleton loader, toast, side drawer (desktop) / bottom sheet (mobile), data table.
```

## 4. Screen prompts

### 4.1 Landing page
```
(Reminder: marketing research & strategy app for clothing brands — not a music app.)
Public landing page, desktop 1440 + mobile 390.
1 Hero: headline "Know your customers' culture. Grow your clothing brand."; subcopy "Turn your brand brief into a growth plan
  backed by real cultural data — insights, partnership ideas, campaigns and experiments you can measure.";
  buttons "Get started free" (primary) and "See a sample plan". Right side: a tilted preview of the growth-plan REPORT
  (cards with teal evidence chips and violet AI labels) next to a flat-lay photo-style illustration of folded graphic tees and a cap.
2 How it works — 3 numbered steps with simple icons: (1) Describe your brand and goal → (2) We research cultural data with Qloo
  → (3) Get a plan with evidence, then test it.
3 "Evidence, not guesswork" — side-by-side card showing a "Retrieved from Qloo" block and an "AI-generated" block, with the note
  "Shared tastes are signals, not guaranteed sales."
4 Who it's for — three cards: Streetwear labels · Fashion designers · Lifestyle & concept stores (Lagos, Abuja and beyond).
5 From plan to results — mini timeline: Plan → Launch experiment → Measure → Decide.
6 Install banner: "Works on your phone — saved plans even offline."
7 Footer: product links, privacy, "Powered by Qloo cultural intelligence".
No testimonials, no customer logos, no statistics, no music imagery.
```

### 4.2 Sign in / Create account
```
(Reminder: marketing research & strategy app for clothing brands — not a music app.)
Split layout desktop, single column mobile. Left panel: logo, line "Cultural research for clothing brands", textile line pattern.
Right: card with email + password (show/hide). Create account adds optional display name and a live password checklist
(10+ characters, mix of letters, numbers, symbols). Loading button state. Links: switch sign in / create account;
"View a sample plan without an account". Error states: "Email or password is incorrect", "Too many attempts — try again in
15 minutes", "An account with this email already exists". No social login, no forgot-password link.
```

### 4.3 App shell + Saved plans
```
(Reminder: marketing research & strategy app for clothing brands — not a music app.)
App shell: desktop left sidebar — logo, "Plans", "New brief", "Experiments", "Evaluation" (small "dev" badge), account menu
(name, email, Install app, Remove offline copies, Sign out). Mobile bottom tab bar: Plans · New · Experiments · Account.
Top bar: "7 of 10 plans left today" and an offline indicator.
Saved plans page: grid of REPORT cards (like documents, not media tiles) — brand name, one-line plan headline, status chip
(Queued / Running / Completed / Completed with limitations / Failed), date, counts "5 insights · 3 partnerships · 2 campaigns ·
2 experiments", optional badges "Offline copy" and "MOCK". Filters: brief, status. Pagination.
Empty state: compass + textile illustration, "No plans yet", button "Create your brand brief".
```

### 4.4 Brand brief form (5 steps)
```
(Reminder: marketing research & strategy app for clothing brands — not a music app.)
Multi-step form; horizontal stepper on desktop, "Step 2 of 5" on mobile; Back / Continue buttons.
1 Brand: brand name, category (Streetwear, Fashion, Lifestyle, Beauty, Footwear & Accessories, Other + text), short description,
  brand identity (tone & aesthetic).
2 Products & market: what you sell (textarea, 1000-char counter), target customers, main location with chips
  (Lagos, Abuja, Port Harcourt, Ibadan, Kano, Enugu).
3 Goal & budget: goal as selectable cards with icons (Awareness, Engagement, Sales, Launch, Community, Partnerships), notes,
  budget band (Under ₦100k · ₦100k–₦500k · ₦500k–₦2m · Over ₦2m · Not sure).
4 Cultural references (optional): chip input "Brands, creators, places or shows your customers love", each chip with a type
  dropdown (Brand, Person, Place, Show, Other). Helper: "We'll look these up in Qloo — anything we can't match is listed, not guessed."
5 Review: summary cards with Edit links; buttons "Save brief" and "Save & generate plan" (primary).
Show inline field errors and a top error banner state.
```

### 4.5 Generate + research progress
```
(Reminder: marketing research & strategy app for clothing brands — not a music app.)
(a) Generate screen: brief summary card, options (include partnership ideas toggle, number of campaigns 1–3), quota note,
primary button "Generate growth plan". Offline variant: button disabled with "Generating needs an internet connection".
(b) Progress screen: vertical stepper of real stages — Reading your brief · Planning research · Searching Qloo data
("4 of 9 requests complete") · Checking the evidence · Writing recommendations · Verifying every claim · Designing experiments · Saving.
Note: "Usually 30–90 seconds. You can leave this page — we'll keep working." No percentage bar, no spinner-only screen.
Failed variant: plain-language reason + buttons "Edit brief" and "Try again".
```

### 4.6 Growth plan — Overview
```
(Reminder: marketing research & strategy app for clothing brands — not a music app. This page is a REPORT.)
Growth plan for "Eko Thread Co.". Layout like a research report/document workspace.
Header: brand name, plan headline, status chip, date; actions: "Make available offline" toggle, "Regenerate".
Tabs: Overview · Insights · Partnerships · Campaigns · Experiments · Evidence.
Overview, desktop two columns:
 left — "Summary" paragraph with AI-generated label; "Top insights" (3 compact InsightCards); "Recommended next step" card
        pointing to the first experiment.
 right — "Evidence at a glance": three counters Strong 12 · Moderate 9 · Weak 4 (numbers only, no chart);
         "Limitations" amber panel ("Little data was found for Lagos stores, so local pop-up venues were left out.").
Variants: "Completed with limitations" amber banner; "Sample plan · synthetic data" banner.
```

### 4.7 Insights & Partnerships
```
(Reminder: marketing research & strategy app for clothing brands — not a music app.)
Insights tab: stacked InsightCards. Example: title "Your customers' tastes overlap with minimalist streetwear brands";
teal "What the data shows" panel with chips [ev_003 · Sample Brand B] [ev_007 · minimalist]; violet "Why it matters for your brand"
panel; strength pill; collapsible Assumptions & Limitations.
Partnerships tab: PartnershipCards — partner "Sample Brand B" (brand) with evidence chip, connection, format "Co-branded capsule
of 50 tees", "Why it fits" (AI-generated), "How to test it" box, strength pill.
Bottom: grey box "AI suggestions not found in Qloo" listing names with explanation they were not verified.
```

### 4.8 Campaigns & Experiments (plan → launch → measure)
```
(Reminder: marketing research & strategy app for clothing brands — not a music app.)
Campaigns tab: CampaignCards — title, objective, target hypothesis, cultural insight with evidence chips, angle,
numbered execution steps (e.g. "Shoot a 6-look lookbook", "Run Instagram story poll"), success metric, effort badge, assumptions.
Experiments tab: ExperimentCards as mini test briefs — Hypothesis · Audience segment · Campaign · Primary metric
(e.g. "Instagram link-click → purchase rate") · Period "14 days" · Success threshold "+20% vs previous 14 days" with
"Proposed default" badge · Decision rule (If it works → …, If not → …).
Lifecycle shown with status chip Proposed → Running → Concluded and buttons "Start experiment" (sets start date) and
"Record result" (dialog: observed result, notes, outcome Concluded/Abandoned). Concluded card shows result next to threshold and
highlights the matching decision.
```

### 4.9 Evidence tab + drawer
```
(Reminder: marketing research & strategy app for clothing brands — not a music app.)
Evidence tab: filter bar (type, strength, search); desktop data table / mobile cards with columns: key (mono), name, type,
affinity (bar + number, or "—"), popularity, strength pill, source. Above: simple horizontal bar chart "Top affinity scores
returned by Qloo" with numeric labels.
Evidence drawer (right sheet / bottom sheet): name, type, key, "Retrieved from Qloo" label, affinity & popularity, tags,
strength pill with reasons ("from your reference: Sample Brand B", "affinity 0.91", "seen in 2 searches"), retrieved time,
"Used in" links to recommendations.
```

### 4.10 Experiments hub (cross-plan)
```
(Reminder: marketing research & strategy app for clothing brands — not a music app.)
Experiments page listing experiments across all plans as a simple kanban or grouped list: Proposed · Running · Concluded.
Each item: hypothesis (one line), brand, metric, days remaining (Running), result vs threshold (Concluded).
Empty state: "No experiments started yet — open a plan and start one."
```

### 4.11 Evaluation (judges & developers)
```
(Reminder: marketing research & strategy app for clothing brands — not a music app.)
Page labelled "Evaluation mode (for judges & developers)". Pick a brief → "Run comparison".
Neutral comparison table "Qloo-grounded plan" vs "AI-only plan": named entities, traceable to retrieved data, unverified names,
recommendations with evidence, numbers not found in inputs, entities per recommendation, brief coverage. No winner badge.
Caveat: "The AI-only plan cannot cite retrieved data by design." Below: both plans side by side with unverified names highlighted.
Reviewer notes textarea.
```

### 4.12 Offline, 404, install & update
```
(Reminder: marketing research & strategy app for clothing brands — not a music app.)
Offline page: "You're offline" + list of saved offline plans + note that generating needs a connection.
404 page with compass motif. Install card "Install Kulture Compass for quick access" (Install / Not now).
iOS sheet: Share icon → "Add to Home Screen". Toast: "A new version is available — Reload".
```

## 5. Correction prompt (if it drifts to music/media UI)
```
This is wrong — Kulture Compass is not a music or media app. Remove all album art, play buttons, audio players, playlists,
track lists, waveforms, headphones and artist-profile layouts. Redesign this screen as a marketing research REPORT / strategy
workspace for a clothing brand: white document-style cards, section headings, data tables, teal "Retrieved from Qloo" evidence
chips and violet "AI-generated" interpretation blocks, terracotta primary buttons. Use clothing imagery (tees, hoodies,
fabric) only where imagery is needed. Keep the same content and structure I asked for.
```

## 6. Export checklist
- Export React + Tailwind per screen → `frontend/design-exports/<nn-screen>/`.
- Don't install tool-specific packages; FE-3 re-implements with Radix + our tokens.
- Check: all names fictional, no invented metrics, no music/media UI, provenance colours consistent, every page has
  empty / loading / error states.
