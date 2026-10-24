# ADR 004 — Self-hosted JWT auth (ASP.NET Core Identity + HS256 access + rotating refresh)

**Decision:** No third-party identity provider. ASP.NET Core Identity (`AddIdentityCore<AppUser>`)
owns user storage, password hashing and lockout. We issue our own 15-minute HS256 JWT access tokens
(`JsonWebTokenHandler`, kept in memory only on the client) plus opaque, rotating refresh tokens
(SHA-256 hashed at rest, family-based reuse detection) in an `HttpOnly; Secure; SameSite=Strict`
cookie scoped to `/api/v1/auth`. Resource ownership = JWT `sub`; non-owned resources return 404.

**Why:** A hackathon project has no budget/need for Auth0/Entra External ID account creation and
callback-URL juggling. Identity already handles the crypto-sensitive parts (password hashing, lockout,
security stamps) so nothing is home-grown there; only token issuance/rotation is custom, and that is a
well-understood, testable pattern.

**Consequences:** No social login, no MFA, no email verification/password-reset-by-email in MVP
(documented in `KNOWN_LIMITATIONS.md`). Same-origin deployment is required for the refresh cookie to
work (`SameSite=Strict`) — see `docs/shared/02-security-and-config.md` §1.
