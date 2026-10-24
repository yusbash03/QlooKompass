#!/usr/bin/env bash
# Kulture Compass - BE-0 Qloo probe (bash + curl + jq).
# Runs the fixed request matrix from docs/backend/BE-03-qloo-integration.md section 2 against a real
# Qloo key, saves raw responses + headers under qloo-probe-output/ (gitignored), and prints a
# summary table. The key is read from env and is never echoed, logged, or written to any file
# other than the Authorization header of the outbound request.
set -euo pipefail

: "${QLOO_API_KEY:?QLOO_API_KEY is required (export it - never pass it as a command-line argument)}"
: "${QLOO_BASE_URL:?QLOO_BASE_URL is required, e.g. https://hackathon.api.qloo.com}"

command -v curl >/dev/null || { echo "curl is required" >&2; exit 1; }
command -v jq   >/dev/null || { echo "jq is required"   >&2; exit 1; }

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT_DIR="$SCRIPT_DIR/../qloo-probe-output"
mkdir -p "$OUT_DIR"

SLEEP_SECONDS=0.3 # <= 4 rps
ROWS=()

encode() { jq -rn --arg v "$1" '$v|@uri'; }

# probe <n> <slug> <path-with-query> [api-key-override]
probe() {
  local n="$1" slug="$2" path="$3" key="${4:-$QLOO_API_KEY}"
  local body_file="$OUT_DIR/${n}-${slug}.json"
  local headers_file="$OUT_DIR/${n}-${slug}.headers"
  local url="${QLOO_BASE_URL%/}${path}"

  local start_ms end_ms elapsed status
  start_ms=$(date +%s%3N)
  status=$(curl -sS -o "$body_file" -D "$headers_file" -w '%{http_code}' \
    -H "X-Api-Key: ${key}" -H 'Accept: application/json' \
    "$url" 2>/dev/null) || status="000"
  end_ms=$(date +%s%3N)
  elapsed=$((end_ms - start_ms))

  local count=0 top3="" rl=""
  if [[ "$status" =~ ^2 ]] && [[ -s "$body_file" ]]; then
    count=$(jq '(.results.entities // .results.tags // []) | length' "$body_file" 2>/dev/null || echo 0)
    top3=$(jq -r '(.results.entities // .results.tags // []) | map(.name) | .[0:3] | join(", ")' "$body_file" 2>/dev/null || echo "")
  fi
  rl=$(grep -i '^x-\(second\|month\)-ratelimit' "$headers_file" 2>/dev/null | tr -d '\r' | paste -sd ';' - || true)

  ROWS+=("${n}|${slug}|${status}|${count}|${top3}|${rl}|${elapsed}ms")
  printf 'Request %-4s %-30s status=%s count=%-3s elapsed=%sms\n' "$n" "$slug" "$status" "$count" "$elapsed" >&2

  sleep "$SLEEP_SECONDS"
}

id_of() { jq -r '(.results.entities // [])[0].entity_id // empty' "$OUT_DIR/$1" 2>/dev/null || true; }
tagid_of() { jq -r '(.results.tags // [])[0].id // empty' "$OUT_DIR/$1" 2>/dev/null || true; }

echo "Probing ${QLOO_BASE_URL} ..." >&2

probe 1 search-artist-shorttype "/search?query=$(encode 'Burna Boy')&types=artist&take=3"
probe 2 search-artist-urntype   "/search?query=$(encode 'Burna Boy')&types=urn:entity:artist&take=3"
probe 3 search-brand            "/search?query=$(encode 'Nike')&types=brand&take=3"
probe 4 search-untyped          "/search?query=$(encode 'Wizkid')&take=5"
probe 5 search-brand-ng         "/search?query=$(encode 'Orange Culture')&types=brand&take=5"
probe 6 search-place            "/search?query=$(encode 'Lagos')&types=destination,place&take=5"
probe 7 tags-streetwear         "/v2/tags?filter.query=$(encode 'streetwear')"
probe 8 tags-afrobeats          "/v2/tags?filter.query=$(encode 'afrobeats')"

ARTIST_ID=$(id_of "1-search-artist-shorttype.json")
BRAND_ID=$(id_of "3-search-brand.json")
TAG_ID=$(tagid_of "7-tags-streetwear.json")

if [[ -n "$ARTIST_ID" ]]; then
  probe 9 insights-brand-from-artist "/v2/insights?filter.type=urn:entity:brand&signal.interests.entities=$(encode "$ARTIST_ID")&take=10&feature.explainability=true"
else
  echo "skip #9: no artist id resolved from #1" >&2
fi

if [[ -n "$BRAND_ID" ]]; then
  probe 10 insights-artist-from-brand "/v2/insights?filter.type=urn:entity:artist&signal.interests.entities=$(encode "$BRAND_ID")&take=10"
else
  echo "skip #10: no brand id resolved from #3" >&2
fi

if [[ -n "$ARTIST_ID" ]]; then
  probe 11 insights-cultural-themes "/v2/insights?filter.type=urn:tag&filter.tag.types=urn:tag:keyword:qloo,urn:tag:genre:qloo&signal.interests.entities=$(encode "$ARTIST_ID")&take=15"
  probe 12 insights-local-places "/v2/insights?filter.type=urn:entity:place&filter.location.query=$(encode 'Lagos, Nigeria')&signal.interests.entities=$(encode "$ARTIST_ID")&take=10"
  probe 14 insights-demographics "/v2/insights?filter.type=urn:demographics&signal.interests.entities=$(encode "$ARTIST_ID")"
  probe 17a insights-podcast "/v2/insights?filter.type=urn:entity:podcast&signal.interests.entities=$(encode "$ARTIST_ID")&take=5"
  probe 17b insights-tv_show "/v2/insights?filter.type=urn:entity:tv_show&signal.interests.entities=$(encode "$ARTIST_ID")&take=5"
else
  echo "skip #11,#12,#14,#17: no artist id resolved from #1" >&2
fi

if [[ -n "$TAG_ID" ]]; then
  probe 13 insights-tag-only-signal "/v2/insights?filter.type=urn:entity:brand&signal.interests.tags=$(encode "$TAG_ID")&take=10"
else
  echo "skip #13: no tag id resolved from #7" >&2
fi

probe 15 insights-no-signal-400 "/v2/insights?filter.type=urn:entity:brand"
probe 16 search-wrong-key-401 "/search?query=$(encode 'Burna Boy')&types=artist&take=1" "invalid-test-key-0000000000"

echo >&2
printf '%-4s %-28s %-6s %-6s %-30s %-28s %s\n' "#" "Request" "Status" "Count" "Top 3 names" "Rate-limit headers" "Elapsed"
printf -- '-%.0s' {1..130}; echo
for row in "${ROWS[@]}"; do
  IFS='|' read -r n slug status count top3 rl elapsed <<< "$row"
  printf '%-4s %-28s %-6s %-6s %-30s %-28s %s\n' "$n" "$slug" "$status" "$count" "$top3" "$rl" "$elapsed"
done

echo
echo "Raw responses + headers saved under: $OUT_DIR (gitignored). Do not copy values from them into committed files."
