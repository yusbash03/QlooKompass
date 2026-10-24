# ADR 005 — Gemini free tier as primary LLM behind `ILlmClient`

**Decision:** `ILlmClient` is a narrow Application abstraction (`GenerateJsonAsync`) with three
Infrastructure implementations: `GeminiLlmClient` (Google AI Studio free tier, primary),
`OpenAiCompatibleLlmClient` (Groq/OpenRouter/OpenAI-compatible fallback), and a deterministic
`MockLlmClient` that is always available and requires no key.

**Why:** Gemini's free tier is the only major-provider option with a usable free quota and native
JSON-schema-constrained output, which the grounded-synthesis step depends on. The abstraction means a
quota exhaustion or policy change only requires a config switch (`Llm:Provider`), not a code change,
and development/tests never require a live key.

**Consequences:** Documented trade-offs (small daily quota, no guarantee of data privacy on the free
tier, EEA/UK/CH usage restrictions) live in `README.md`/`KNOWN_LIMITATIONS.md`. No personal data is
ever sent in prompts regardless of provider.
