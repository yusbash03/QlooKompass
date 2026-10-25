#!/usr/bin/env bash
# Kulture Compass — API demo (04-deployment §3 flow, terminal edition until the PWA exists).
#
#   scripts/demo.sh [base-url] [profile-json]
#   defaults: http://localhost:8080  backend/samples/lagos-streetwear-sample.json
#
# Needs: bash, curl, python3. Creates a throwaway synthetic account. Prints summaries only — never tokens or keys.
set -euo pipefail

BASE="${1:-http://localhost:8080}"
PROFILE_JSON="${2:-$(dirname "$0")/../backend/samples/lagos-streetwear-sample.json}"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
JSON='Content-Type: application/json'
py() { python3 -I -c "$1"; }

step() { printf '\n\033[1m%s\033[0m\n' "$1"; }

step "0. Health and mode"
curl -sf "$BASE/api/v1/health/ready" | py 'import sys,json;d=json.load(sys.stdin);print("  ready:",d["status"])'
curl -sf "$BASE/api/v1/meta" | py 'import sys,json;d=json.load(sys.stdin);print("  qloo:",d["qlooMode"],"| llm:",d["llmProvider"],"| mock mode:",d["isMockMode"])'

step "1. Public sample plan (no sign-in) — retrieved evidence vs AI-generated sections"
curl -sf "$BASE/api/v1/sample-plan" | py '
import sys,json;d=json.load(sys.stdin)
print("  isSample:",d["isSample"],"| isMock:",d["isMock"])
print("  RETRIEVED  evidence:",len(d["evidence"]),"items, e.g.",d["evidence"][0]["key"],d["evidence"][0]["name"])
print("  GENERATED  insights",len(d["insights"]),"| collaborations",len(d["collaborations"]),"| campaigns",len(d["campaigns"]),"| experiments",len(d["experiments"]))'

step "2. Register a throwaway account"
EMAIL="demo-$(date +%s)-$RANDOM@example.test"
curl -sf -X POST "$BASE/api/v1/auth/register" -H "$JSON" \
  -d "{\"email\":\"$EMAIL\",\"password\":\"Demo-Passw0rd-$RANDOM\",\"displayName\":\"Demo\"}" -o "$TMP/auth.json"
TOKEN="$(py "import json;print(json.load(open('$TMP/auth.json'))['accessToken'])")"
AUTH="Authorization: Bearer $TOKEN"
echo "  signed in as $EMAIL (token kept in memory only)"

step "3. Create a business profile from $(basename "$PROFILE_JSON")"
PROFILE_ID="$(curl -sf -X POST "$BASE/api/v1/business-profiles" -H "$JSON" -H "$AUTH" -d @"$PROFILE_JSON" | py 'import sys,json;print(json.load(sys.stdin)["id"])')"
echo "  profile $PROFILE_ID"

step "4. Generate a growth plan — real stage progress from the backend"
KEY="$(py 'import uuid;print(uuid.uuid4())')"
PLAN_ID="$(curl -sf -X POST "$BASE/api/v1/growth-plans" -H "$JSON" -H "$AUTH" -H "Idempotency-Key: $KEY" \
  -d "{\"businessProfileId\":\"$PROFILE_ID\"}" | py 'import sys,json;print(json.load(sys.stdin)["id"])')"
LAST=""; START=$(date +%s)
for _ in $(seq 1 240); do
  LINE="$(curl -sf "$BASE/api/v1/growth-plans/$PLAN_ID/status" -H "$AUTH" | py '
import sys,json;d=json.load(sys.stdin);a=[s for s in d["stages"] if s["state"]=="Active"]
print(d["status"],"|",d["stage"],"|",(a[0].get("detail") or "") if a else "")')"
  [ "$LINE" != "$LAST" ] && echo "  $LINE" && LAST="$LINE"
  case "$LINE" in Completed*|Failed*) break;; esac
  sleep 1
done
echo "  finished in $(( $(date +%s) - START ))s"

step "5. The plan"
curl -sf "$BASE/api/v1/growth-plans/$PLAN_ID" -H "$AUTH" | py '
import sys,json;d=json.load(sys.stdin)
if d["status"]=="Failed": print("  FAILED:",d["error"]); sys.exit()
ev={e["key"]:e for e in d["evidence"]}
print("  ",d["summary"]["headline"],"(isMock=" + str(d["isMock"]) + ")")
print("  evidence:",len(ev),"items by quality",d["stats"]["evidenceByQuality"],"| qloo calls",d["stats"]["qlooCalls"],"| llm calls",d["stats"]["llmCalls"])
i=d["insights"][0]; print("  insight:",i["title"],"<-",", ".join(k+" "+ev[k]["name"] for k in i["evidenceKeys"]))
for c in d["collaborations"][:1]: print("  collaboration partner (evidence-linked):",c["partnerEvidenceKey"],c["partnerName"])
c=d["campaigns"][0]; print("  campaign:",c["title"],"| effort",c["effortCategory"])
x=d["experiments"][0]; print("  experiment:",x["primaryMetric"],"over",x["measurementPeriodDays"],"days, threshold",repr(x["successThreshold"]),"(proposed default:",str(x["thresholdIsProposedDefault"])+")")
print("    if success:",x["nextDecision"]["ifSuccess"]); print("    if failure:",x["nextDecision"]["ifFailure"])
print("  AI suggestions not retrieved from Qloo:",[m["name"] for m in d["unlinkedMentions"]])
print("  limitations:"); [print("   -",l) for l in d["limitations"]]
recs=d["insights"]+d["collaborations"]+d["campaigns"]
print("  every evidence key resolves:",all(k in ev for r in recs for k in r["evidenceKeys"]))'

step "6. Evaluation mode — grounded vs LLM-only (skipped automatically if disabled)"
CODE="$(curl -s -o "$TMP/eval.json" -w '%{http_code}' -X POST "$BASE/api/v1/evaluations" -H "$JSON" -H "$AUTH" \
  -H "Idempotency-Key: $(py 'import uuid;print(uuid.uuid4())')" -d "{\"businessProfileId\":\"$PROFILE_ID\"}")"
if [ "$CODE" != "202" ]; then
  echo "  not run (HTTP $CODE: $(py "import json;print(json.load(open('$TMP/eval.json')).get('code'))"))"
  exit 0
fi
EVAL_ID="$(py "import json;print(json.load(open('$TMP/eval.json'))['id'])")"
for _ in $(seq 1 240); do
  STATUS="$(curl -sf "$BASE/api/v1/evaluations/$EVAL_ID" -H "$AUTH" -o "$TMP/eval.json" -w '' && py "import json;print(json.load(open('$TMP/eval.json'))['status'])")"
  case "$STATUS" in Completed|Failed) break;; esac
  sleep 1
done
py "
import json;d=json.load(open('$TMP/eval.json'))
if d['status']!='Completed': print('  evaluation',d['status'],d.get('error')); raise SystemExit
g,l=d['groundedMetrics'],d['llmOnlyMetrics']
row=lambda n,k: print(f'  {n:<34}{str(g[k]):>10}{str(l[k]):>10}')
print(f'  {\"metric\":<34}{\"grounded\":>10}{\"LLM-only\":>10}')
for n,k in [('named entities','namedEntities'),('traceable to retrieved evidence','traceableEntities'),('unsupported entities','unsupportedEntities'),('recs with evidence %','recommendationsWithEvidencePct'),('unsupported numeric claims','unsupportedNumericClaims'),('brief-term coverage %','briefTermCoveragePct')]: row(n,k)
print('  notes:'); [print('   -',n) for n in d['notes']]"
