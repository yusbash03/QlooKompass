<#
.SYNOPSIS
    Kulture Compass - BE-0 Qloo probe (Windows PowerShell).
.DESCRIPTION
    Runs the fixed request matrix from docs/backend/BE-03-qloo-integration.md section 2 against a real
    Qloo key, saves raw responses + headers under qloo-probe-output/ (gitignored), and prints a
    summary table. The key is read from env and is never echoed, logged, or written to any file
    other than the X-Api-Key header of the outbound request.
#>
$ErrorActionPreference = 'Stop'

if (-not $env:QLOO_API_KEY) {
    Write-Error "QLOO_API_KEY is required (set it as an env var - never pass it as a command-line argument)"
    exit 1
}
if (-not $env:QLOO_BASE_URL) {
    Write-Error "QLOO_BASE_URL is required, e.g. https://hackathon.api.qloo.com"
    exit 1
}

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$OutDir = Join-Path $ScriptDir "..\qloo-probe-output"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$SleepMs = 300 # <= 4 rps
$Rows = New-Object System.Collections.ArrayList

function UrlEncode([string]$s) { [uri]::EscapeDataString($s) }

function Invoke-Probe {
    param(
        [string]$N,
        [string]$Slug,
        [string]$Path,
        [string]$ApiKey = $env:QLOO_API_KEY
    )
    $BodyFile = Join-Path $OutDir "$N-$Slug.json"
    $HeadersFile = Join-Path $OutDir "$N-$Slug.headers"
    $Url = $env:QLOO_BASE_URL.TrimEnd('/') + $Path

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $status = 0
    $headers = $null
    $bodyText = $null
    try {
        $resp = Invoke-WebRequest -Uri $Url -Headers @{ 'X-Api-Key' = $ApiKey; 'Accept' = 'application/json' } -UseBasicParsing
        $status = [int]$resp.StatusCode
        $headers = $resp.Headers
        $bodyText = $resp.Content
    } catch {
        if ($_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
            $headers = $_.Exception.Response.Headers
            try {
                $stream = $_.Exception.Response.GetResponseStream()
                $reader = New-Object System.IO.StreamReader($stream)
                $bodyText = $reader.ReadToEnd()
            } catch { $bodyText = "" }
        } else {
            $status = 0
            $bodyText = ""
        }
    }
    $sw.Stop()
    $elapsed = $sw.ElapsedMilliseconds

    if ($null -ne $bodyText) { Set-Content -Path $BodyFile -Value $bodyText -Encoding utf8 }
    $headerLines = @()
    if ($headers) { foreach ($k in $headers.Keys) { $headerLines += "$k`: $($headers[$k])" } }
    Set-Content -Path $HeadersFile -Value $headerLines -Encoding utf8

    $count = 0; $top3 = ""
    if ($status -ge 200 -and $status -lt 300 -and $bodyText) {
        try {
            $json = $bodyText | ConvertFrom-Json
            $items = $null
            if ($json.results.entities) { $items = $json.results.entities }
            elseif ($json.results.tags) { $items = $json.results.tags }
            if ($items) {
                $count = @($items).Count
                $top3 = (@($items) | Select-Object -First 3 | ForEach-Object { $_.name }) -join ", "
            }
        } catch { }
    }
    $rl = ($headerLines | Where-Object { $_ -match '^(?i)x-(second|month)-ratelimit' }) -join '; '

    [void]$Rows.Add([PSCustomObject]@{ N = $N; Slug = $Slug; Status = $status; Count = $count; Top3 = $top3; RateLimit = $rl; ElapsedMs = $elapsed })
    Write-Host ("Request {0,-4} {1,-30} status={2} count={3,-3} elapsed={4}ms" -f $N, $Slug, $status, $count, $elapsed)

    Start-Sleep -Milliseconds $SleepMs
}

function Get-FirstEntityId([string]$N, [string]$Slug) {
    $f = Join-Path $OutDir "$N-$Slug.json"
    if (-not (Test-Path $f)) { return $null }
    try {
        $json = Get-Content $f -Raw | ConvertFrom-Json
        if ($json.results.entities -and @($json.results.entities).Count -gt 0) { return @($json.results.entities)[0].entity_id }
    } catch { }
    return $null
}
function Get-FirstTagId([string]$N, [string]$Slug) {
    $f = Join-Path $OutDir "$N-$Slug.json"
    if (-not (Test-Path $f)) { return $null }
    try {
        $json = Get-Content $f -Raw | ConvertFrom-Json
        if ($json.results.tags -and @($json.results.tags).Count -gt 0) { return @($json.results.tags)[0].id }
    } catch { }
    return $null
}

Write-Host "Probing $($env:QLOO_BASE_URL) ..."

Invoke-Probe -N "1" -Slug "search-artist-shorttype" -Path ("/search?query={0}&types=artist&take=3" -f (UrlEncode 'Burna Boy'))
Invoke-Probe -N "2" -Slug "search-artist-urntype"   -Path ("/search?query={0}&types=urn:entity:artist&take=3" -f (UrlEncode 'Burna Boy'))
Invoke-Probe -N "3" -Slug "search-brand"            -Path ("/search?query={0}&types=brand&take=3" -f (UrlEncode 'Nike'))
Invoke-Probe -N "4" -Slug "search-untyped"          -Path ("/search?query={0}&take=5" -f (UrlEncode 'Wizkid'))
Invoke-Probe -N "5" -Slug "search-brand-ng"         -Path ("/search?query={0}&types=brand&take=5" -f (UrlEncode 'Orange Culture'))
Invoke-Probe -N "6" -Slug "search-place"            -Path ("/search?query={0}&types=destination,place&take=5" -f (UrlEncode 'Lagos'))
Invoke-Probe -N "7" -Slug "tags-streetwear"         -Path ("/v2/tags?filter.query={0}" -f (UrlEncode 'streetwear'))
Invoke-Probe -N "8" -Slug "tags-afrobeats"          -Path ("/v2/tags?filter.query={0}" -f (UrlEncode 'afrobeats'))

$ArtistId = Get-FirstEntityId "1" "search-artist-shorttype"
$BrandId  = Get-FirstEntityId "3" "search-brand"
$TagId    = Get-FirstTagId "7" "tags-streetwear"

if ($ArtistId) {
    Invoke-Probe -N "9" -Slug "insights-brand-from-artist" -Path ("/v2/insights?filter.type=urn:entity:brand&signal.interests.entities={0}&take=10&feature.explainability=true" -f (UrlEncode $ArtistId))
} else { Write-Host "skip #9: no artist id resolved from #1" }

if ($BrandId) {
    Invoke-Probe -N "10" -Slug "insights-artist-from-brand" -Path ("/v2/insights?filter.type=urn:entity:artist&signal.interests.entities={0}&take=10" -f (UrlEncode $BrandId))
} else { Write-Host "skip #10: no brand id resolved from #3" }

if ($ArtistId) {
    Invoke-Probe -N "11" -Slug "insights-cultural-themes" -Path ("/v2/insights?filter.type=urn:tag&filter.tag.types=urn:tag:keyword:qloo,urn:tag:genre:qloo&signal.interests.entities={0}&take=15" -f (UrlEncode $ArtistId))
    Invoke-Probe -N "12" -Slug "insights-local-places" -Path ("/v2/insights?filter.type=urn:entity:place&filter.location.query={0}&signal.interests.entities={1}&take=10" -f (UrlEncode 'Lagos, Nigeria'), (UrlEncode $ArtistId))
    Invoke-Probe -N "14" -Slug "insights-demographics" -Path ("/v2/insights?filter.type=urn:demographics&signal.interests.entities={0}" -f (UrlEncode $ArtistId))
    Invoke-Probe -N "17a" -Slug "insights-podcast" -Path ("/v2/insights?filter.type=urn:entity:podcast&signal.interests.entities={0}&take=5" -f (UrlEncode $ArtistId))
    Invoke-Probe -N "17b" -Slug "insights-tv_show" -Path ("/v2/insights?filter.type=urn:entity:tv_show&signal.interests.entities={0}&take=5" -f (UrlEncode $ArtistId))
} else { Write-Host "skip #11,#12,#14,#17: no artist id resolved from #1" }

if ($TagId) {
    Invoke-Probe -N "13" -Slug "insights-tag-only-signal" -Path ("/v2/insights?filter.type=urn:entity:brand&signal.interests.tags={0}&take=10" -f (UrlEncode $TagId))
} else { Write-Host "skip #13: no tag id resolved from #7" }

Invoke-Probe -N "15" -Slug "insights-no-signal-400" -Path "/v2/insights?filter.type=urn:entity:brand"
Invoke-Probe -N "16" -Slug "search-wrong-key-401" -Path ("/search?query={0}&types=artist&take=1" -f (UrlEncode 'Burna Boy')) -ApiKey "invalid-test-key-0000000000"

Write-Host ""
Write-Host ("{0,-4} {1,-28} {2,-6} {3,-6} {4,-30} {5,-28} {6}" -f "#", "Request", "Status", "Count", "Top 3 names", "Rate-limit headers", "Elapsed")
Write-Host ("-" * 130)
foreach ($r in $Rows) {
    Write-Host ("{0,-4} {1,-28} {2,-6} {3,-6} {4,-30} {5,-28} {6}ms" -f $r.N, $r.Slug, $r.Status, $r.Count, $r.Top3, $r.RateLimit, $r.ElapsedMs)
}

Write-Host ""
Write-Host "Raw responses + headers saved under: $OutDir (gitignored). Do not copy values from them into committed files."
