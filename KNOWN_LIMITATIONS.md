# Known limitations

Started in BE-5; BE-10 completes this file. Each item says what is missing and what happens instead.

## Auth (BE-5)
- **Not in the MVP:** email confirmation, password reset by email, MFA, social login (02-security §1).
- **No endpoint to change a password yet.** Changing the security stamp does invalidate access tokens (tested),
  but nothing in the API triggers that today.
- **Security-stamp check is cached for 5 minutes per user**, so a revoked access token can still work for up to
  5 minutes. Access tokens expire after 15 minutes anyway.
- **No cleanup job for refresh tokens yet** (02-security §1 "daily background job"). Expired or revoked rows
  pile up in `refresh_tokens`. They're never accepted, so this costs storage only.
- **Rate limiting:** only the `auth` (10/min) and `auth-refresh` (30/min) per-IP policies exist so far. `global`,
  `generation` and `public` arrive with their endpoints (BE-7/BE-9). Behind a reverse proxy the client IP needs
  forwarded-headers config (BE-9); until then all proxied traffic shares one bucket.
- **The 429 rate-limit response is unverified by tests.** It's configured, but the tests raise the limits so
  they don't trip it.

## Qloo
- See `docs/backend/qloo-verified-contract.md` §3 and §7. Demographics (R8) stays off; error bodies aren't parsed.
