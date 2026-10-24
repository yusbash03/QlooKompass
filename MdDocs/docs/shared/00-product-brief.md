# 00 — Product Brief

## 0. Core flow
Landing → (sign in) → business brief → validate → plan research → search Qloo → retrieve insights & affinities → validate & organise evidence → Gemini interprets → generate & validate plan → display results → save → launch experiments → measure results. Full chart: 05-user-stories-and-flows §2.0.

## 1. One-liner
**Kulture Compass** turns a small brand's business brief into an evidence-grounded **Cultural Growth Plan**:
cultural insights, collaboration candidates, campaign concepts and measurable experiments — each traceable
to Qloo-retrieved evidence.

### Naming
| Option | Notes |
|---|---|
| **Kulture Compass** ✅ chosen | Navigation metaphor; "K" spelling feels at home in Nigerian pop culture; domain-friendly |
| CulturePilot | Original working name; generic, crowded "Pilot" naming space |
| Vibe Atlas | Playful, strong for streetwear; slightly less B2B-credible |
| Signal & Scene | Editorial; weaker as a product noun |

Tagline candidates: *"Find your culture. Grow your brand."* · *"Cultural intelligence for brands that move culture."*

## 2. Problem
Small brands lack affordable cultural/audience research. They don't know which cultural affinities matter for
their products, which collaborations are worth exploring, or how to turn research into measurable tests.
Generic LLM marketing advice sounds plausible but is unanchored.

## 3. Value proposition (every feature must serve this)
Business brief → plan containing:
1. Cultural/audience insights supported by available Qloo data.
2. Relevant entities (brands, artists, places, media) and cross-domain connections.
3. Collaboration opportunities grounded in retrieved entities.
4. Campaign concepts grounded in retrieved evidence.
5. Measurable experiment plans (hypothesis → metric → threshold → decision).
6. Explicit evidence, assumptions, limitations and next steps.

**Differentiator to demonstrate:** an *evaluation mode* comparing LLM-only vs Qloo-grounded output on the same
brief, scored on traceability and unsupported claims — reported honestly.

## 4. Personas
| Persona | Need |
|---|---|
| **Ada — founder, Lagos streetwear label** (primary) | "Who else does my customer love? Which artist/brand could I partner with? What should I test next month on a ₦300k budget?" |
| **Tunde — marketing freelancer for 3 SMEs** | Repeatable research he can show clients with sources |
| **Judge / evaluator** | See Qloo is necessary, evidence is traceable, app runs end-to-end |

## 5. Scope
### MVP (must)
- A. Business profile onboarding (create/edit, partial info allowed)
- B. Generate plan (async, real progress stages, idempotent)
- C. Cultural insights view (retrieved entities, connections, metadata, limitations)
- D. Collaboration opportunities
- E. Campaign concepts
- F. Experiment plans
- G. Save & revisit plans (JWT-authenticated email/password accounts, owner-scoped)
- I. Experiment tracking: mark an experiment Running (launched by the user outside the app), then record the observed result and decide next step
- H. Evaluation mode (LLM-only vs grounded) — feature-flagged
- PWA: installable, app-shell offline, honest offline states, opt-in offline copies of saved plans
- Public synthetic **sample plan** for judges who don't sign in

### Stretch
- Export plan as PDF/Markdown
- Compare two plans

### Out of scope (MVP)
Publishing campaigns, ad spend, contacting third parties, payments, team workspaces, React Native app,
Redis, message brokers, multi-region.

## 6. Honesty rules for the product
- Qloo affinities are **aggregate taste associations**, not purchase intent, causation, or verified demographics.
- Nigerian coverage in Qloo may be thin; the app **shows coverage gaps** rather than filling them.
- Experiment thresholds are labelled **"proposed default"** unless the user supplied them.
- No fake charts. Charts only plot values Qloo actually returned (e.g. affinity scores).

## 7. Glossary
| Term | Meaning |
|---|---|
| **Business profile** | The brand's saved brief (name, category, products, market, objective, references, budget) |
| **Growth plan** | One generation run for a profile: status, evidence set, recommendations, experiments |
| **Evidence item** | A normalized Qloo result kept with provenance; key `ev_###` unique per plan |
| **Seed entity** | User-referenced brand/artist/place resolved via Qloo search and used as a signal |
| **Evidence quality** | `Strong` / `Moderate` / `Weak` — deterministic rules in BE-03 §7 |
| **Recommendation** | Generated item of kind `Insight`, `Collaboration` or `Campaign`, citing evidence keys |
| **Experiment** | Proposed test derived from a recommendation |
| **Limitation** | Explicit statement of missing coverage/assumption shown to the user |
| **Mock mode** | Deterministic Qloo and/or LLM providers for dev/tests; always labelled |

## 8. Success criteria (hackathon)
- End-to-end live run on a real Nigerian streetwear brief produces a plan where **100 % of evidence refs resolve**.
- Evaluation mode shows the measurable difference (traceable claims vs unsupported claims).
- A judge can install the PWA, view the sample plan offline, sign in, and generate a plan in < 2 minutes.
