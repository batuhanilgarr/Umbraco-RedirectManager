# Plan A: 1.14 Announcement, `release/1.x` and Golden Baseline (Week 1)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> Master plan: `docs/superpowers/plans/2026-10-12-v2-master-plan.md`. Spec: `docs/superpowers/specs/2026-10-12-redirectmanager-2-0-design.md`.

**Goal:** Before any 2.0 code changes: (1) record how the redirect engine behaves today in a repeatable golden suite,
(2) tell Umbraco 13 users that 2.0 is coming and that they stay on 1.x (release 1.14.0), (3) make the update banner stay inside the installed major version so no 1.x site is ever told to install 2.x, (4) cut the
`release/1.x` branch so Umbraco 13 keeps a maintained line.

**Architecture:** The golden suite is a Node script that drives a running Umbraco site: it creates tagged redirect
rules through the backoffice API (using the logged-in Chrome from `scripts/dev/cdp.mjs`), sends real HTTP requests,
records status/Location/cookies plus the `/test` API and CSV export, then deletes its rules. `record` writes a baseline
JSON; `check` re-runs and fails on any difference. The announcement is a dismissible notice in the AngularJS dashboard
(the only audience that needs it) plus an About row in the Lit dashboard and README updates.

**Tech Stack:** Node 22+ (built-in `fetch`/`WebSocket`, no dependencies), Chrome DevTools Protocol, Umbraco 18 test
site, AngularJS dashboard (Umbraco 13), Lit dashboard (Umbraco 17/18), git branches and tags.

## File Structure

- Create `scripts/dev/golden/cases.json`: the fixed rules and probes (data only).
- Create `scripts/dev/golden/run.mjs`: runner with `record` and `check`.
- Create `scripts/dev/golden/baseline.json`: the recording made on 1.x (committed).
- Create `Services/LatestVersionSelector.cs` and `Umbraco.RedirectManager.Tests/Services/LatestVersionSelectorTests.cs`; modify
  `Services/RedirectVersionChecker.cs`: the update check stays inside the installed major.
- Modify `App_Plugins/RedirectManager/dashboard.html`, `redirect.controller.js`, `redirect.css`: AngularJS notice.
- Modify `App_Plugins/RedirectManager/redirect-dashboard.js`: one About row (Lit).
- Modify `README.md`: supported-versions table and the 2.0 note.
- Modify `Umbraco.RedirectManager.csproj`, `App_Plugins/RedirectManager/umbraco-package.json`: version 1.14.0.
- Modify `docs/EXTENDING.md`: one line announcing the 2.0 stability window.
- Create (outside the repo) `/Users/bhan/Documents/works/sites/RedirectManagerTestSite/RmTestSite13`: Umbraco 13 test site.

All work happens in a worktree on branch `v2/plan-a` created from `main`:
```bash
cd /Users/bhan/Documents/works/sites/RedirectManager
git worktree add ../RedirectManager-v2-a -b v2/plan-a main
```
The test site's `App_Plugins/RedirectManager` symlink points at the main checkout; when verifying a worktree, point the
symlink at the worktree (`ln -sfn /Users/bhan/Documents/works/sites/RedirectManager-v2-a/App_Plugins/RedirectManager <testsite>/App_Plugins/RedirectManager`)
and restore it afterwards.

---

### Task 1: Golden behavior suite

**Files:**
- Create: `scripts/dev/golden/cases.json`
- Create: `scripts/dev/golden/run.mjs`
- Create: `scripts/dev/golden/baseline.json` (generated)

- [ ] **Step 1: Create `scripts/dev/golden/cases.json`** (data only; the `key` of each entry and probe is stable and used in the baseline)

```json
{
  "tag": "golden-suite",
  "entries": [
    { "key": "exact301", "oldUrl": "/golden/old-a", "newUrl": "/golden/new-a", "statusCode": 301 },
    { "key": "exact302", "oldUrl": "/golden/old-b", "newUrl": "/golden/new-b", "statusCode": 302 },
    { "key": "gone", "oldUrl": "/golden/gone", "statusCode": 410 },
    { "key": "notfound", "oldUrl": "/golden/notfound", "statusCode": 404 },
    { "key": "slash", "oldUrl": "/golden/slash", "newUrl": "/golden/slash-new", "statusCode": 301 },
    { "key": "case", "oldUrl": "/golden/MixedCase", "newUrl": "/golden/case-new", "statusCode": 301 },
    { "key": "qkeep", "oldUrl": "/golden/q-keep", "newUrl": "/golden/q-keep-new", "statusCode": 301, "preserveQueryString": true },
    { "key": "qdrop", "oldUrl": "/golden/q-drop", "newUrl": "/golden/q-drop-new", "statusCode": 301 },
    { "key": "qspecific", "oldUrl": "/golden/search?x=1", "newUrl": "/golden/search-x1", "statusCode": 301 },
    { "key": "wildcard", "oldUrl": "/golden/wild/*", "newUrl": "/golden/dest/*", "statusCode": 301 },
    { "key": "regex", "oldUrl": "^/golden/rx/(\\d+)$", "newUrl": "/golden/item/$1", "statusCode": 301, "isRegex": true },
    { "key": "domain", "oldUrl": "/golden/dom", "newUrl": "/golden/dom-new", "statusCode": 301, "domain": "golden.test" },
    { "key": "inactive", "oldUrl": "/golden/inactive", "newUrl": "/golden/inactive-new", "statusCode": 301, "isActive": false },
    { "key": "future", "oldUrl": "/golden/future", "newUrl": "/golden/future-new", "statusCode": 301, "validFromDays": 1 },
    { "key": "expired", "oldUrl": "/golden/expired", "newUrl": "/golden/expired-new", "statusCode": 301, "validUntilDays": -1 },
    { "key": "window", "oldUrl": "/golden/window", "newUrl": "/golden/window-new", "statusCode": 301, "validFromDays": -1, "validUntilDays": 1 },
    { "key": "abAll", "oldUrl": "/golden/ab-all", "newUrl": "/golden/ab-a", "statusCode": 301, "variantBUrl": "/golden/ab-b", "variantBWeight": 100 },
    { "key": "abNone", "oldUrl": "/golden/ab-none", "newUrl": "/golden/ab-a2", "statusCode": 301, "variantBUrl": "/golden/ab-b2", "variantBWeight": 0 },
    { "key": "prioExact", "oldUrl": "/golden/prio/x", "newUrl": "/golden/prio-exact", "statusCode": 301 },
    { "key": "prioWild", "oldUrl": "/golden/prio/*", "newUrl": "/golden/prio-wild", "statusCode": 301 },
    { "key": "prioRegex", "oldUrl": "^/golden/prio/.*$", "newUrl": "/golden/prio-regex", "statusCode": 301, "isRegex": true },
    { "key": "skipped", "oldUrl": "/api/golden-skip", "newUrl": "/golden/should-not-happen", "statusCode": 301 }
  ],
  "probes": [
    { "key": "exact301", "path": "/golden/old-a" },
    { "key": "exact302", "path": "/golden/old-b" },
    { "key": "gone", "path": "/golden/gone" },
    { "key": "notfound", "path": "/golden/notfound" },
    { "key": "slashWithSlash", "path": "/golden/slash/" },
    { "key": "slashWithout", "path": "/golden/slash" },
    { "key": "caseUpper", "path": "/GOLDEN/MIXEDCASE" },
    { "key": "qkeep", "path": "/golden/q-keep?a=1&b=2" },
    { "key": "qdrop", "path": "/golden/q-drop?a=1" },
    { "key": "qspecificHit", "path": "/golden/search?x=1" },
    { "key": "qspecificMiss", "path": "/golden/search?x=2" },
    { "key": "wildcardDeep", "path": "/golden/wild/a/b" },
    { "key": "regexHit", "path": "/golden/rx/42" },
    { "key": "regexMiss", "path": "/golden/rx/abc" },
    { "key": "domainHit", "path": "/golden/dom", "host": "golden.test" },
    { "key": "domainMiss", "path": "/golden/dom" },
    { "key": "inactive", "path": "/golden/inactive" },
    { "key": "future", "path": "/golden/future" },
    { "key": "expired", "path": "/golden/expired" },
    { "key": "window", "path": "/golden/window" },
    { "key": "abAll", "path": "/golden/ab-all", "repeat": 6 },
    { "key": "abNone", "path": "/golden/ab-none", "repeat": 6 },
    { "key": "prioExact", "path": "/golden/prio/x" },
    { "key": "prioWildBeatsRegex", "path": "/golden/prio/y" },
    { "key": "skipped", "path": "/api/golden-skip" },
    { "key": "unknown", "path": "/golden/never-defined" }
  ],
  "apiProbes": [
    { "key": "testExact", "path": "/golden/old-a" },
    { "key": "testWildcard", "path": "/golden/wild/z" },
    { "key": "testRegex", "path": "/golden/rx/7" },
    { "key": "testNone", "path": "/golden/never-defined" }
  ]
}
```

- [ ] **Step 2: Create `scripts/dev/golden/run.mjs`**

```javascript
#!/usr/bin/env node
// Golden behavior suite for the redirect engine. Records what a running Umbraco site does for a fixed set of
// redirect rules and requests, then checks later builds against that recording.
//
//   node scripts/dev/golden/run.mjs record [file]   write the recording (default: baseline.json next to this file)
//   node scripts/dev/golden/run.mjs check  [file]   run again and fail on any difference
//
// Needs: Node 22+, a running Umbraco site with this package, and Chrome started as described in scripts/dev/cdp.mjs
// (logged in to the backoffice). Environment: SITE (default https://localhost:44346), CDP_PORT, CDP_ORIGIN.
// The suite creates rules tagged "golden-suite" through the backoffice API and deletes them again, also on failure.
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import https from 'node:https';
import http from 'node:http';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const cdp = path.join(here, '..', 'cdp.mjs');
const SITE = process.env.SITE ?? 'https://localhost:44346';
const cases = JSON.parse(fs.readFileSync(path.join(here, 'cases.json'), 'utf8'));
const API = '/umbraco/api/redirectmanager';

const [mode, fileArg] = process.argv.slice(2);
if (!['record', 'check'].includes(mode)) {
  console.error('usage: run.mjs record|check [file]');
  process.exit(2);
}
const file = path.resolve(fileArg ?? path.join(here, 'baseline.json'));

function cdpRun(...args) {
  const r = spawnSync('node', [cdp, ...args], { encoding: 'utf8', timeout: 180000 });
  if (r.status !== 0) throw new Error(`cdp ${args[0]} failed: ${r.stderr || r.stdout}`);
  return r.stdout.trim();
}

// Runs async JS in the backoffice page (where authFetch adds the user's token) and returns its JSON result.
function inPage(body) {
  const out = cdpRun('eval', `(async () => { const d = window.__rm.dashboard(); const f = (u, o) => d.authFetch('${API}' + u, o); ${body} })()`);
  return JSON.parse(JSON.parse(out.split('\n').pop()));
}

function request(url, headers = {}) {
  return new Promise((resolve, reject) => {
    const u = new URL(url);
    const lib = u.protocol === 'https:' ? https : http;
    const req = lib.request(u, { method: 'GET', headers, rejectUnauthorized: false }, (res) => {
      res.resume();
      res.on('end', () => resolve({
        status: res.statusCode,
        location: res.headers.location ?? null,
        cookie: (res.headers['set-cookie'] ?? []).length > 0
      }));
    });
    req.on('error', reject);
    req.end();
  });
}

const dateOffset = (days) => new Date(Date.now() + days * 86400000).toISOString();

function cleanup() {
  return inPage(`
    const all = await (await f('/getall')).json();
    const mine = all.filter(r => r.description === ${JSON.stringify(cases.tag)});
    for (const r of mine) await f('/delete/' + r.id, { method: 'DELETE' });
    return JSON.stringify(mine.length);`);
}

async function main() {
  cdpRun('goto', `${SITE}/umbraco/section/settings/dashboard/redirect-manager`);
  cdpRun('wait-for', 'button', 'Redirects', '90000');

  cleanup();
  try {
    const payload = cases.entries.map(({ key, validFromDays, validUntilDays, ...dto }) => ({
      ...dto,
      description: cases.tag,
      validFrom: validFromDays === undefined ? null : dateOffset(validFromDays),
      validUntil: validUntilDays === undefined ? null : dateOffset(validUntilDays)
    }));
    const created = inPage(`
      const out = [];
      for (const dto of ${JSON.stringify(payload)}) {
        const r = await f('/create', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(dto) });
        out.push([dto.oldUrl, r.status]);
      }
      return JSON.stringify(out);`);
    const failed = created.filter(([, s]) => s !== 200);
    if (failed.length) throw new Error('could not create rules: ' + JSON.stringify(failed));

    const result = { http: {}, api: {}, meta: { rules: created.length } };

    for (const p of cases.probes) {
      const headers = p.host ? { Host: p.host } : {};
      const runs = [];
      for (let i = 0; i < (p.repeat ?? 1); i++) runs.push(await request(SITE + p.path, headers));
      result.http[p.key] = p.repeat
        ? { status: [...new Set(runs.map(r => r.status))].sort(), locations: [...new Set(runs.map(r => r.location))].sort(), cookie: runs.some(r => r.cookie) }
        : runs[0];
    }

    for (const p of cases.apiProbes) {
      const r = inPage(`
        const r = await f('/test?path=' + encodeURIComponent(${JSON.stringify(p.path)}));
        const j = await r.json();
        return JSON.stringify({ status: r.status, matched: j.matched, matchType: j.matchType ?? null, status3: j.redirect?.statusCode ?? null, target: j.computedNewUrl ?? null });`);
      result.api[p.key] = r;
    }

    // CSV round trip: export contains exactly our rules in the documented column layout.
    const csv = inPage(`
      const r = await f('/export?q=' + encodeURIComponent('/golden/'));
      return JSON.stringify({ status: r.status, text: await r.text() });`);
    const lines = csv.text.replace(/^﻿/, '').trim().split(/\r?\n/);
    result.csv = { status: csv.status, header: lines[0], rows: lines.length - 1, first: lines[1] ?? null };

    return result;
  } finally {
    cleanup();
  }
}

const actual = await main();

if (mode === 'record') {
  fs.writeFileSync(file, JSON.stringify(actual, null, 2) + '\n');
  console.log(`recorded ${Object.keys(actual.http).length} HTTP probes, ${Object.keys(actual.api).length} API probes -> ${file}`);
  process.exit(0);
}

const expected = JSON.parse(fs.readFileSync(file, 'utf8'));
const diffs = [];
for (const group of ['http', 'api']) {
  for (const key of new Set([...Object.keys(expected[group]), ...Object.keys(actual[group])])) {
    const a = JSON.stringify(expected[group][key]);
    const b = JSON.stringify(actual[group][key]);
    if (a !== b) diffs.push(`${group}.${key}\n    expected ${a}\n    actual   ${b}`);
  }
}
if (JSON.stringify(expected.csv) !== JSON.stringify(actual.csv))
  diffs.push(`csv\n    expected ${JSON.stringify(expected.csv)}\n    actual   ${JSON.stringify(actual.csv)}`);

if (diffs.length) {
  console.error(`GOLDEN MISMATCH (${diffs.length}):\n` + diffs.join('\n'));
  process.exit(1);
}
console.log(`golden suite matches (${Object.keys(actual.http).length} HTTP, ${Object.keys(actual.api).length} API probes, CSV).`);
```

- [ ] **Step 3: Syntax-check the runner**

Run: `node --check scripts/dev/golden/run.mjs`
Expected: no output, exit code 0.

- [ ] **Step 4: Start the test site and a logged-in Chrome**

```bash
cd /Users/bhan/Documents/works/sites/RedirectManagerTestSite/RmTestSite
unset RedirectManager__Premium__LicenseKey RedirectManager__Premium__PublicKeyOverride
(nohup dotnet run --launch-profile Umbraco.Web.UI > /tmp/rm-site.log 2>&1 &)
for i in $(seq 1 80); do c=$(curl -sk -o /dev/null -w "%{http_code}" https://localhost:44346/umbraco); [ "$c" != "000" ] && break; sleep 3; done; echo site=$c
curl -s localhost:9222/json/version >/dev/null 2>&1 || (nohup "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" --remote-debugging-port=9222 --user-data-dir=/tmp/chrome-rm --no-first-run --ignore-certificate-errors https://localhost:44346/umbraco >/dev/null 2>&1 &)
sleep 8
cd /Users/bhan/Documents/works/sites/RedirectManager
node scripts/dev/cdp.mjs login admin@example.com 'TestAdmin123!'
```
Expected: `site=200`, then `submitted` (or `login form not found` if the session is already logged in; both are fine).
If Chrome is running but has no page, open one with `curl -s -X PUT "localhost:9222/json/new?https://localhost:44346/umbraco"`.
The test site has no Premium reference (the `RmTestSite.csproj` must contain no `RedirectManagerPremium` line).

- [ ] **Step 5: Record the baseline on the current 1.x code**

Run: `node scripts/dev/golden/run.mjs record`
Expected: `recorded 26 HTTP probes, 4 API probes -> .../scripts/dev/golden/baseline.json`

- [ ] **Step 6: Review the recording against documented behavior**

Run: `python3 -c "import json;d=json.load(open('scripts/dev/golden/baseline.json'));[print(k,json.dumps(v)) for g in ('http','api') for k,v in d[g].items()]"`
Expected (these are the behaviors the README documents; if any line differs, STOP and investigate before committing):
```
exact301            301 -> /golden/new-a        exact302     302 -> /golden/new-b
gone                410, no Location            notfound     404, no Location
slashWithSlash      301 -> /golden/slash-new    slashWithout 301 -> /golden/slash-new
caseUpper           301 -> /golden/case-new
qkeep               301 -> /golden/q-keep-new?a=1&b=2     qdrop 301 -> /golden/q-drop-new
qspecificHit        301 -> /golden/search-x1    qspecificMiss 404
wildcardDeep        301 -> /golden/dest/a/b
regexHit            301 -> /golden/item/42      regexMiss    404
domainHit           301 -> /golden/dom-new      domainMiss   404
inactive/future/expired  404                    window       301 -> /golden/window-new
abAll               locations ["/golden/ab-b"], cookie true
abNone              locations ["/golden/ab-a2"], cookie true
prioExact           301 -> /golden/prio-exact   prioWildBeatsRegex 301 -> /golden/prio-wild
skipped             404 (the /api prefix is never redirected)          unknown 404
testExact Exact -> /golden/new-a   testWildcard Wildcard -> /golden/dest/z   testRegex Regex -> /golden/item/7   testNone matched=false
csv header "OldUrl,NewUrl,Description,StatusCode,IsActive,IsRegex", rows 22
```

- [ ] **Step 7: Check passes, leaves nothing behind, and fails when behavior differs**

```bash
node scripts/dev/golden/run.mjs check
node scripts/dev/cdp.mjs eval "(async()=>{const d=window.__rm.dashboard();const all=await (await d.authFetch('/umbraco/api/redirectmanager/getall')).json();return JSON.stringify({left:all.filter(r=>r.description==='golden-suite').length})})()"
python3 - <<'EOF'
import json
d=json.load(open('scripts/dev/golden/baseline.json')); d['http']['exact301']['status']=302
json.dump(d,open('/tmp/golden-tampered.json','w'))
EOF
node scripts/dev/golden/run.mjs check /tmp/golden-tampered.json; echo "exit=$?"
```
Expected: `golden suite matches (26 HTTP, 4 API probes, CSV).`; `{"left":0}`; a `GOLDEN MISMATCH (1):` block naming `http.exact301`; `exit=1`.

- [ ] **Step 8: Commit**

```bash
git add scripts/dev/golden
git commit -m "test: add golden behavior suite for the redirect engine and record the 1.x baseline"
```

---

### Task 2: The update banner must stay inside the installed major version

Why this task exists: `RedirectVersionChecker` asks NuGet for the newest stable version and shows
"A new version is available: X, `dotnet add package ... --version X`". The day 2.0.0 is published, every 1.x site
(including every Umbraco 13 site, which cannot install 2.x) would be told to install it. 1.14.0 must already behave
correctly, so it has to ship before 2.0.0.

**Files:**
- Create: `Services/LatestVersionSelector.cs`
- Create: `Umbraco.RedirectManager.Tests/Services/LatestVersionSelectorTests.cs`
- Modify: `Services/RedirectVersionChecker.cs` (the `latestVersion` line in `CheckIfDueAsync`, plus two helpers)

- [ ] **Step 1: Write the failing tests** `Umbraco.RedirectManager.Tests/Services/LatestVersionSelectorTests.cs`

```csharp
using Umbraco.RedirectManager.Services;
using Xunit;

namespace Umbraco.RedirectManager.Tests.Services;

public class LatestVersionSelectorTests
{
    private static readonly string[] Published =
        { "1.9.0", "1.14.0", "1.10.0", "2.0.0-rc.1", "2.0.0", "1.14.1-beta.1", "2.1.0" };

    [Theory]
    [InlineData(1, "1.14.0")]   // numeric compare (1.14 > 1.10 > 1.9), prerelease 1.14.1-beta.1 ignored
    [InlineData(2, "2.1.0")]
    public void Picks_the_highest_stable_version_of_the_requested_major(int major, string expected)
        => Assert.Equal(expected, LatestVersionSelector.Pick(Published, major));

    [Fact]
    public void A_1x_install_is_never_pointed_at_2x()
        => Assert.DoesNotContain("2.", LatestVersionSelector.Pick(Published, 1));

    [Fact]
    public void Returns_null_when_the_major_has_no_stable_release()
        => Assert.Null(LatestVersionSelector.Pick(Published, 3));

    [Fact]
    public void Ignores_garbage_blank_and_metadata_versions()
        => Assert.Equal("1.2.0", LatestVersionSelector.Pick(new string?[] { null, "", " ", "abc", "1.2.0", "1.3.0+build5" }, 1));

    [Fact]
    public void Empty_input_gives_null()
        => Assert.Null(LatestVersionSelector.Pick(Array.Empty<string>(), 1));
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test Umbraco.RedirectManager.Tests --filter LatestVersionSelectorTests -v q`
Expected: build error `The name 'LatestVersionSelector' does not exist`.

- [ ] **Step 3: Implement** `Services/LatestVersionSelector.cs`

```csharp
namespace Umbraco.RedirectManager.Services;

/// <summary>
/// Picks the version an install should be told about: the highest stable release of its own major version.
/// A 1.x site (for example on Umbraco 13) must never be nudged to a 2.x package it cannot use.
/// </summary>
public static class LatestVersionSelector
{
    public static string? Pick(IEnumerable<string?> versions, int major)
    {
        string? best = null;
        Version? bestParsed = null;

        foreach (var v in versions)
        {
            // Prereleases ("-rc.1") and build metadata ("+abc") are never recommended.
            if (string.IsNullOrWhiteSpace(v) || v.Contains('-') || v.Contains('+'))
            {
                continue;
            }

            if (!Version.TryParse(v, out var parsed) || parsed.Major != major)
            {
                continue;
            }

            if (bestParsed is null || parsed > bestParsed)
            {
                best = v;
                bestParsed = parsed;
            }
        }

        return best;
    }
}
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test Umbraco.RedirectManager.Tests --filter LatestVersionSelectorTests -v q`
Expected: `Passed!  - Failed: 0, Passed: 6` (the theory counts as two).

- [ ] **Step 5: Use it in the checker.** In `Services/RedirectVersionChecker.cs` replace

```csharp
            var latestVersion = data[0].GetProperty("version").GetString();
            if (string.IsNullOrWhiteSpace(latestVersion))
            {
                return;
            }
```
with
```csharp
            // Stay inside the installed major: a 1.x site (for example on Umbraco 13) must never be pointed at 2.x.
            var latestVersion = LatestVersionSelector.Pick(ReadVersions(data[0]), GetPluginMajor());
            if (string.IsNullOrWhiteSpace(latestVersion))
            {
                return;
            }
```
and add these two members next to `GetPluginVersion`:
```csharp
    private static IEnumerable<string?> ReadVersions(JsonElement package) =>
        package.TryGetProperty("versions", out var versions) && versions.ValueKind == JsonValueKind.Array
            ? versions.EnumerateArray().Select(v => v.TryGetProperty("version", out var s) ? s.GetString() : null)
            : Enumerable.Empty<string?>();

    private static int GetPluginMajor() => typeof(RedirectVersionChecker).Assembly.GetName().Version?.Major ?? 0;
```
(If NuGet's response ever lacks the `versions` array, no banner is shown; that is the safe failure.)

- [ ] **Step 6: Build and run the whole suite**

Run: `dotnet build -v q 2>&1 | grep -E "error|Error\(s\)"; dotnet test Umbraco.RedirectManager.Tests -v q`
Expected: `0 Error(s)`, `Passed!  - Failed: 0, Passed: 147`. Then `git checkout appsettings-schema.Umbraco.Cms.json`.

- [ ] **Step 7: Verify live** that the endpoint still answers sensibly (the dashboard-open trigger runs the check):

```bash
node scripts/dev/cdp.mjs eval "(async()=>{const d=window.__rm.dashboard();await d.authFetch('/umbraco/api/redirectmanager/update-status');await new Promise(r=>setTimeout(r,4000));const r=await d.authFetch('/umbraco/api/redirectmanager/update-status');return JSON.stringify(await r.json())})()"
```
Expected: JSON with `latestVersion` of the form `1.x.y` (never `2.`), `updateAvailable` true or false consistent with
`currentVersion`. (Delete `App_Data/RedirectManagerUpdateCheck/latest-version.json` in the test site first so the 24-hour
throttle does not hide the new logic.)

- [ ] **Step 8: Commit**

```bash
git add Services/LatestVersionSelector.cs Services/RedirectVersionChecker.cs Umbraco.RedirectManager.Tests/Services/LatestVersionSelectorTests.cs
git commit -m "fix: keep the update check inside the installed major version so 1.x sites are never pointed at 2.x"
```

---

### Task 3: 1.14.0 announcement (AngularJS notice, Lit About row, README, version)

**Files:**
- Modify: `App_Plugins/RedirectManager/dashboard.html` (after the update banner, line ~70)
- Modify: `App_Plugins/RedirectManager/redirect.controller.js` (state near line 36, function near line 482)
- Modify: `App_Plugins/RedirectManager/redirect.css` (append)
- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js` (About card)
- Modify: `README.md`, `docs/EXTENDING.md`
- Modify: `Umbraco.RedirectManager.csproj` (`<Version>`), `App_Plugins/RedirectManager/umbraco-package.json` (`version` and `?v=`)

- [ ] **Step 1: AngularJS notice markup.** In `dashboard.html`, directly after the closing `</div>` of the update-available banner, add:

```html
            <!-- 2.0 announcement (Umbraco 13 only: this dashboard does not exist in 2.0). Dismissal is per browser. -->
            <div class="update-banner" ng-if="!vm.announceDismissed">
                <span>
                    <strong>Heads-up:</strong> Redirect Manager 2.0 (planned for December 2026) will support Umbraco 17 and later only.
                    Umbraco 13 sites stay on the maintained 1.x line and lose nothing.
                </span>
                <a href="https://github.com/batuhanilgarr/Umbraco-RedirectManager/blob/main/docs/superpowers/specs/2026-10-12-redirectmanager-2-0-design.md" target="_blank" rel="noopener">Details</a>
                <button type="button" class="btn btn-small" ng-click="vm.dismissAnnounce()">Dismiss</button>
            </div>
```

- [ ] **Step 2: AngularJS state and handler.** In `redirect.controller.js`, after `vm.latestVersion = '';` add:

```javascript
        vm.announceDismissed = readAnnounceDismissed();
```
and, immediately before `// Always-on update-availability check`, add:

```javascript
        // Per-browser preference only; any storage failure just means the notice shows.
        function readAnnounceDismissed() {
            try { return $window.localStorage.getItem('rm-announce-2.0-dismissed') === '1'; } catch (e) { return false; }
        }

        vm.dismissAnnounce = function () {
            vm.announceDismissed = true;
            try { $window.localStorage.setItem('rm-announce-2.0-dismissed', '1'); } catch (e) { /* ignore */ }
        };

```

- [ ] **Step 3: Style the button inside the banner.** Append to `redirect.css`:

```css
.update-banner .btn {
    margin-left: auto;
}
```

- [ ] **Step 4: Lit About row.** In `redirect-dashboard.js`, in the About card's `<dl class="about-list">`, add after the `Extending` row:

```javascript
                            <dt>Next major</dt><dd>2.0 (Umbraco 17+), planned for December 2026. Umbraco 13 stays on 1.x.</dd>
```

- [ ] **Step 5: README.** Replace the first paragraph's "for Umbraco CMS **13, 17, and 18**" wording by adding, directly under the first paragraph, this section:

```markdown
## Supported versions

| Package line | Umbraco | .NET | Status |
|---|---|---|---|
| 1.x | 13, 17, 18 | 8 and 10 | Maintained; Umbraco 13 gets security and critical fixes until 2027-06-30, best effort afterwards |
| 2.0 (planned December 2026) | 17, 18, 19 | 10 | In development; no database changes, upgrading from 1.x needs nothing but the package bump |

Umbraco 13 reaches end of life in December 2026. Nothing you have today is removed: see the
[Free Forever Promise](docs/FREE-FOREVER.md).
```

- [ ] **Step 5b: EXTENDING.md.** At the end of the "Stability policy" bullet list add:

```markdown
* 2.0 (planned December 2026) is the one window for breaking changes to these interfaces. It will be announced here and
  in the release notes first, with a migration note; the interfaces are frozen again for all of 2.x.
```

- [ ] **Step 6: Version 1.14.0.** Set `<Version>1.14.0</Version>` in `Umbraco.RedirectManager.csproj`; in
`App_Plugins/RedirectManager/umbraco-package.json` set `"version": "1.14.0"` and the element URL to `redirect-dashboard.js?v=1.14.0`.
(`package.manifest` for AngularJS keeps its own legacy version string; do not touch it.)

- [ ] **Step 7: Run the tests and a syntax check**

Run: `dotnet test Umbraco.RedirectManager.Tests -v q` and `node --check App_Plugins/RedirectManager/redirect-dashboard.js && node --check App_Plugins/RedirectManager/redirect.controller.js`
Expected: `Passed!  - Failed: 0, Passed: 147` and no syntax errors. Then `git checkout appsettings-schema.Umbraco.Cms.json` if the build modified it.

- [ ] **Step 8: Verify the Lit dashboard live**

With the test site running (Task 1 Step 4) and the symlink pointing at this worktree:
```bash
node scripts/dev/cdp.mjs reload
node scripts/dev/cdp.mjs wait-for button "Settings" 90000
node scripts/dev/cdp.mjs click-text button "Settings"
node scripts/dev/cdp.mjs eval "window.__rm.dashboard().shadowRoot.querySelector('.about-list').innerText"
```
Expected: the text contains `Next major` and `2.0 (Umbraco 17+)` and `Version` shows `1.14.0`.

- [ ] **Step 9: Run the golden suite**

Run: `node scripts/dev/golden/run.mjs check`
Expected: `golden suite matches (26 HTTP, 4 API probes, CSV).`

- [ ] **Step 10: Commit**

```bash
git add -A . ':!appsettings-schema.Umbraco.Cms.json'
git commit -m "feat: announce 2.0 (Umbraco 17+) to Umbraco 13 users and document supported versions; bump to 1.14.0"
```

---

### Task 4: Umbraco 13 test site, to verify the AngularJS notice and to maintain `release/1.x`

**Files:**
- Create (outside the repo): `/Users/bhan/Documents/works/sites/RedirectManagerTestSite/RmTestSite13`

- [ ] **Step 1: Create the site**

```bash
cd /Users/bhan/Documents/works/sites/RedirectManagerTestSite
dotnet new install Umbraco.Templates::13.9.2 --force
dotnet new umbraco --force -n RmTestSite13 --friendly-name "Admin" --email admin@example.com --password "TestAdmin123!" \
  --development-database-type SQLite
```
Expected: a `RmTestSite13` folder with a `.csproj` targeting `net8.0`.

- [ ] **Step 2: Reference the package source and link the assets**

Edit `RmTestSite13/RmTestSite13.csproj`: add inside an `<ItemGroup>` `<ProjectReference Include="../../RedirectManager/Umbraco.RedirectManager.csproj" />`.
Then:
```bash
mkdir -p RmTestSite13/App_Plugins
ln -sfn /Users/bhan/Documents/works/sites/RedirectManager-v2-a/App_Plugins/RedirectManager RmTestSite13/App_Plugins/RedirectManager
```
(Use the main checkout path when verifying `release/1.x` later.) Set a distinct port: in `RmTestSite13/Properties/launchSettings.json` use `https://localhost:44347`.

- [ ] **Step 3: Run it and install Umbraco**

```bash
cd RmTestSite13 && (nohup dotnet run --launch-profile Umbraco.Web.UI > /tmp/rm-site13.log 2>&1 &)
for i in $(seq 1 90); do c=$(curl -sk -o /dev/null -w "%{http_code}" https://localhost:44347/umbraco); [ "$c" != "000" ] && break; sleep 3; done; echo site=$c
```
Expected: `site=200` (the unattended install from Step 1 creates the admin user).

- [ ] **Step 4: Verify the notice** with Chrome (`CDP_ORIGIN=localhost:44347`):
```bash
export CDP_ORIGIN=localhost:44347
node scripts/dev/cdp.mjs goto https://localhost:44347/umbraco
node scripts/dev/cdp.mjs login admin@example.com 'TestAdmin123!'
node scripts/dev/cdp.mjs goto https://localhost:44347/umbraco/#/settings/settingsDashboards/redirectManagerDashboard
node scripts/dev/cdp.mjs shot /tmp/u13-notice.png
```
Expected: the screenshot shows the "Heads-up: Redirect Manager 2.0 ..." banner with a Dismiss button. Click Dismiss, reload, confirm it does not return; clear `localStorage` key `rm-announce-2.0-dismissed` to restore.
If the dashboard URL differs in Umbraco 13, open Settings in the UI and select the "Redirect Manager" dashboard tab; the check is the banner, not the URL.

- [ ] **Step 5: Stop the site** (`pkill -f RmTestSite13`) and record the site's path and port in `docs/superpowers/plans/2026-10-12-v2-master-plan.md` under "Working rules" (one line).

- [ ] **Step 6: Commit the one-line doc change**

```bash
git add docs/superpowers/plans/2026-10-12-v2-master-plan.md
git commit -m "docs: note the Umbraco 13 test site used for the 1.x line"
```

---

### Task 5: Merge, publish 1.14.0 (needs the owner's explicit OK), cut `release/1.x`

**Files:** none (git and release operations).

- [ ] **Step 1: Merge locally**

```bash
cd /Users/bhan/Documents/works/sites/RedirectManager
git merge --no-ff v2/plan-a -m "Merge plan A: golden suite and 1.14.0 announcement"
git worktree remove ../RedirectManager-v2-a && git branch -d v2/plan-a
dotnet test Umbraco.RedirectManager.Tests -v q
```
Expected: merge succeeds, `Passed!  - Failed: 0`.

- [ ] **Step 2: Ask the owner to confirm the 1.14.0 release** (it is public: every 1.x user will see the Lit About row; Umbraco 13 users the banner). Only after an explicit yes in that moment:

```bash
git push origin main
git tag -a v1.14.0 -m "v1.14.0: announce 2.0 (Umbraco 17+); Umbraco 13 stays on 1.x"
git push origin v1.14.0
```
Watch the run: `gh run list --workflow=publish-nuget.yml --limit 1`, then `gh run watch <id> --exit-status`.
Then sync the Marketplace: `curl -s -X POST "https://functions.marketplace.umbraco.com/api/InitiateSinglePackageSyncFunction" -H "Content-Type: application/json" -d '{"PackageId": "BT.RedirectManager"}' -o /dev/null -w "%{http_code}\n"`
Expected: workflow `success`, sync `202`.

- [ ] **Step 3: Create the 1.x maintenance branch at the 1.14.0 tag**

```bash
git branch release/1.x v1.14.0
git push origin release/1.x
```
Expected: `release/1.x` exists on GitHub. From now on, Umbraco 13 fixes land there and are released with `v1.14.x` tags from that branch.

- [ ] **Step 4: Publish release notes** on GitHub for `v1.14.0` (`gh release create v1.14.0 --notes "..."`) stating: nothing removed; what 2.0 changes; that Umbraco 13 stays on 1.x until 2027-06-30 with security and critical fixes, best effort afterwards; link to the spec and to `docs/FREE-FOREVER.md`.

- [ ] **Step 5: Update the memory file** `/Users/bhan/.claude/projects/-Users-bhan-Documents-works-sites-RedirectManager/memory/project_monetization_roadmap.md`:
add a line "Plan A done: golden suite recorded, 1.14.0 published, release/1.x created" and mark Plan B as next.

## Self-review against the spec

- Golden baseline before touching the engine: Task 1.
- No 1.x or Umbraco 13 site is ever told to install 2.x (found while planning): Task 2. It must ship in 1.14.0, before 2.0.0 exists.
- 1.14 announcement "one minor ahead" (spec section 4): Task 3 and Task 5.
- `release/1.x` branch (spec section 1, section 4): Task 5 Step 3.
- Umbraco 13 users keep everything and are told so: Task 3 (banner text) and README table.
- Nothing here changes redirect behavior: Task 3 Step 9 runs the golden suite to prove it.
