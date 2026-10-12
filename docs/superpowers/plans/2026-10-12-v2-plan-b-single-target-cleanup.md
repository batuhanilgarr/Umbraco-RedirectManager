# Plan B: Single Target Cleanup (Week 2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> Master plan: `docs/superpowers/plans/2026-10-12-v2-master-plan.md`. Spec: `docs/superpowers/specs/2026-10-12-redirectmanager-2-0-design.md`.
> **Prerequisite:** Plan A is finished: `v1.14.0` is published, `release/1.x` exists, and
> `scripts/dev/golden/baseline.json` is committed on `main`.

**Goal:** Turn `main` into the 2.0 line: net10.0 only, no Umbraco 13 or AngularJS code, one migration plan, with the
redirect engine proven unchanged by the golden suite.

**Architecture:** Pure deletion and simplification. Four `#if NET10_0_OR_GREATER` blocks keep their net10 branch; the
legacy `MigrationBase` copies of every migration go away (same migration GUIDs, so a 1.x database is already up to
date); the AngularJS assets and the `package.manifest` are deleted; CI, README and marketplace text stop mentioning
Umbraco 13 as supported. No behavior change, no schema change, no new feature.

**Tech Stack:** .NET 10, Umbraco 17.1.0 package references (patched versions are Plan D), Lit dashboard, xUnit, Node 22.

## File Structure

- Modify `Umbraco.RedirectManager.csproj`: single `TargetFramework`, unconditional Umbraco 17.1.0 references.
- Modify `Services/RedirectCultureResolver.cs`, `Services/RedirectCsvBackupService.cs`, `Services/RedirectManagerHealthCheck.cs`: keep the net10 branch.
- Modify `Migrations/RedirectManagerMigrationPlan.cs`: delete the net8 (`MigrationBase`) copies (lines 316 to 580).
- Delete `App_Plugins/RedirectManager/dashboard.html`, `modal.html`, `redirect.controller.js`, `redirect.css`, `redirect.resource.js`, `package.manifest`, `Lang/en-us.xml`.
- Modify `.github/workflows/publish-nuget.yml`: drop .NET 8.
- Modify `README.md`, `umbraco-marketplace.json`, `docs/EXTENDING.md`: supported versions.
- Modify version in `Umbraco.RedirectManager.csproj` and `App_Plugins/RedirectManager/umbraco-package.json`: `2.0.0-alpha.1`.

Work in a worktree: `git worktree add ../RedirectManager-v2-b -b v2/plan-b main`. When verifying live, point the test
site's `App_Plugins/RedirectManager` symlink at the worktree and restore it afterwards.

---

### Task 1: Single target framework

**Files:** Modify `Umbraco.RedirectManager.csproj`.

- [ ] **Step 1: Edit the csproj.** Replace `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>` with
`<TargetFramework>net10.0</TargetFramework>`, and replace the two conditional `ItemGroup`s

```xml
  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <PackageReference Include="Umbraco.Cms" Version="17.1.0" />
    <PackageReference Include="Umbraco.Cms.Infrastructure" Version="17.1.0" />
  </ItemGroup>

  <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
    <PackageReference Include="Umbraco.Cms" Version="13.9.2" />
    <PackageReference Include="Umbraco.Cms.Infrastructure" Version="13.9.2" />
  </ItemGroup>
```
with
```xml
  <ItemGroup>
    <PackageReference Include="Umbraco.Cms" Version="17.1.0" />
    <PackageReference Include="Umbraco.Cms.Infrastructure" Version="17.1.0" />
  </ItemGroup>
```

- [ ] **Step 2: Restore and build (the `#if` branches still exist, so this must still compile)**

Run: `dotnet restore && dotnet build -v q 2>&1 | grep -E "error|Error\(s\)"`
Expected: `0 Error(s)`. Then `git checkout appsettings-schema.Umbraco.Cms.json`.

- [ ] **Step 3: Commit**

```bash
git add Umbraco.RedirectManager.csproj
git commit -m "build: target net10.0 only (Umbraco 17+)"
```

---

### Task 2: Remove the `#if` branches

**Files:** Modify the four `.cs` files listed above.

- [ ] **Step 1: Strip them with one script that keeps the net10 branch**

```bash
python3 - <<'EOF'
import re
files = [
    "Services/RedirectCultureResolver.cs",
    "Services/RedirectCsvBackupService.cs",
    "Services/RedirectManagerHealthCheck.cs",
    "Migrations/RedirectManagerMigrationPlan.cs",
]
pattern = re.compile(r"#if NET10_0_OR_GREATER\n(.*?)#else\n.*?#endif\n", re.S)
for f in files:
    s = open(f).read()
    new, n = pattern.subn(r"\1", s)
    assert n == 1, (f, n)
    open(f, "w").write(new)
    print(f, "ok")
EOF
```
Expected: four `ok` lines.

- [ ] **Step 2: Prove nothing conditional is left and the legacy classes are gone**

```bash
grep -rn "NET10_0_OR_GREATER\|NET8_0\|#if \|#else\|#endif" --include="*.cs" . | grep -v -E "/(bin|obj)/|Tests|samples" ; echo "grep-exit=$?"
grep -c "class CreateRedirectManagerTable" Migrations/RedirectManagerMigrationPlan.cs
grep -c "MigrationBase\b" Migrations/RedirectManagerMigrationPlan.cs
wc -l Migrations/RedirectManagerMigrationPlan.cs
```
Expected: no `#if` lines (`grep-exit=1`), `1`, `0` (only `AsyncMigrationBase` remains; if the second count is non-zero check it is
`AsyncMigrationBase`, which the pattern `\bMigrationBase\b` does not match), and roughly 310 lines in the migration file (was 580).

- [ ] **Step 3: Fix the now-stale comments.** In `Services/RedirectCultureResolver.cs` the comment above
`GetAllAsync` mentions "this TFM" and "net8.0 targets 13.9.2"; replace the whole comment block with:

```csharp
        // Umbraco.Cms.Core's synchronous IDomainService.GetAll(bool) was obsoleted in 17.1.0 and removed
        // entirely in 18.0.1; GetAllAsync is the only overload guaranteed to exist at runtime, even though this
        // project compiles against the 17.1.0 reference (which still has both).
```
In `Services/RedirectManagerHealthCheck.cs` replace "on this TFM" in the comment with nothing (delete the words `on this TFM`).
In `Services/RedirectCsvBackupService.cs` leave the comment that explains the `SendAsync` overload as is.

- [ ] **Step 4: Build and test**

Run: `dotnet build -v q 2>&1 | grep -E "error|Error\(s\)"; dotnet test Umbraco.RedirectManager.Tests -v q`
Expected: `0 Error(s)`, `Passed!  - Failed: 0, Passed: 147` (the count from Plan A; if Plan A ended with a different number, use that
number). Then `git checkout appsettings-schema.Umbraco.Cms.json`.

- [ ] **Step 5: Commit**

```bash
git add -A . ':!appsettings-schema.Umbraco.Cms.json'
git commit -m "refactor: remove the Umbraco 13 conditional code and the legacy migration copies"
```

---

### Task 3: Remove the AngularJS dashboard

**Files:** Delete the seven files; modify `README.md`, `umbraco-marketplace.json`, `docs/EXTENDING.md`.

- [ ] **Step 1: Delete the AngularJS assets**

```bash
git rm App_Plugins/RedirectManager/dashboard.html App_Plugins/RedirectManager/modal.html \
  App_Plugins/RedirectManager/redirect.controller.js App_Plugins/RedirectManager/redirect.css \
  App_Plugins/RedirectManager/redirect.resource.js App_Plugins/RedirectManager/package.manifest \
  App_Plugins/RedirectManager/Lang/en-us.xml
ls App_Plugins/RedirectManager
```
Expected: `assets  redirect-dashboard.js  umbraco-package.json`.

- [ ] **Step 2: README.** Make these edits:
  - First paragraph: replace `for Umbraco CMS **13, 17, and 18**` with `for Umbraco CMS **17 and 18**`.
  - Feature bullet "Redesigned backoffice dashboard": replace `built with Lit (Umbraco 17/18) and AngularJS (Umbraco 13)` with `built with Lit (Umbraco 17+)`.
  - In the "Supported versions" table (added in Plan A) change the 2.0 row to
    `| 2.0 (pre-release) | 17, 18 (19 planned) | 10 | In development; no database changes, upgrading from 1.x needs nothing but the package bump |`.
  - Under the table add: `Umbraco 13 users: stay on 1.x (\`dotnet add package BT.RedirectManager --version 1.*\`).`

- [ ] **Step 3: Marketplace text.** In `umbraco-marketplace.json` replace `for Umbraco 13, 17, and 18` with `for Umbraco 17 and 18`.

- [ ] **Step 4: EXTENDING.md.** In the paragraph that says the dashboard extensions are rendered by the Lit dashboard
and that the "legacy AngularJS dashboard (Umbraco 13) ignores them", replace it with
`Dashboard extensions are rendered by the Lit dashboard (Umbraco 17+).` and drop the sentence about the server-side seams
compiling for every target.

- [ ] **Step 5: Check no stray references remain**

```bash
grep -rn -i "angularjs\|umbraco 13\|net8\|redirect.controller\|package.manifest" README.md docs/EXTENDING.md umbraco-marketplace.json Umbraco.RedirectManager.csproj build .github 2>/dev/null
```
Expected: only the intentional lines that describe the 1.x line (the "Umbraco 13 users: stay on 1.x" sentence and the supported-versions table).

- [ ] **Step 6: Verify the dashboard still loads live.** Run the test site (Plan A, Task 1 Step 4 commands), then:

```bash
node scripts/dev/cdp.mjs reload
node scripts/dev/cdp.mjs wait-for button "404 log" 90000
for t in "Redirects" "404 log" "Overview" "Settings"; do node scripts/dev/cdp.mjs click-text button "$t"; sleep 1; done
node scripts/dev/cdp.mjs eval "(()=>{const d=window.__rm.dashboard();return JSON.stringify({tab:d.activeTab,ver:d.currentVersion})})()"
node scripts/dev/cdp.mjs shot /tmp/v2-b-smoke.png
```
Expected: `{"tab":"settings",...}` and a screenshot of the Settings tab with no console errors
(`node scripts/dev/cdp.mjs eval "1"` still works; also check `read_console_messages` equivalent: no red errors in the screenshot).

- [ ] **Step 7: Commit**

```bash
git add -A . ':!appsettings-schema.Umbraco.Cms.json'
git commit -m "feat!: remove the AngularJS dashboard; 2.0 supports Umbraco 17+ only"
```

---

### Task 4: CI, version, packaging

**Files:** `.github/workflows/publish-nuget.yml`, `Umbraco.RedirectManager.csproj`, `App_Plugins/RedirectManager/umbraco-package.json`.

- [ ] **Step 1: CI.** In `.github/workflows/publish-nuget.yml` change the setup-dotnet block

```yaml
          dotnet-version: |
            8.0.x
            10.0.x
```
to
```yaml
          dotnet-version: 10.0.x
```

- [ ] **Step 2: Version.** Set `<Version>2.0.0-alpha.1</Version>` in the csproj and `"version": "2.0.0-alpha.1"` with the element URL
`redirect-dashboard.js?v=2.0.0-alpha.1` in `umbraco-package.json`.

- [ ] **Step 3: Pack and inspect the package**

```bash
rm -rf /tmp/v2-pack && dotnet pack -c Release -o /tmp/v2-pack -v q 2>&1 | grep -E "error|Error\(s\)"
unzip -l /tmp/v2-pack/BT.RedirectManager.2.0.0-alpha.1.nupkg | awk '{print $4}' | grep -E "^lib/|contentFiles|build/"
unzip -p /tmp/v2-pack/BT.RedirectManager.2.0.0-alpha.1.nupkg BT.RedirectManager.nuspec | grep -E "<version>|targetFramework=|dependency id=\"Umbraco"
```
Expected: only `lib/net10.0/...`; contentFiles contain `redirect-dashboard.js`, `umbraco-package.json` and `assets/...` and none of
`dashboard.html`, `package.manifest`, `redirect.controller.js`; the nuspec shows version `2.0.0-alpha.1`, group `net10.0` and Umbraco 17.1.0 dependencies.
`git checkout appsettings-schema.Umbraco.Cms.json` afterwards.

- [ ] **Step 4: Commit**

```bash
git add -A . ':!appsettings-schema.Umbraco.Cms.json'
git commit -m "build: net10 CI, version 2.0.0-alpha.1"
```

---

### Task 5: Prove nothing behavioral changed

- [ ] **Step 1: Golden suite** (test site running, Chrome logged in, symlink pointing at this worktree)

Run: `node scripts/dev/golden/run.mjs check`
Expected: `golden suite matches (26 HTTP, 4 API probes, CSV).` Any mismatch means this plan changed behavior: stop and fix; do not re-record.

- [ ] **Step 2: Upgrade from a 1.x database.** The test site's SQLite database was created by 1.x. Start the site on this
build and confirm there is no migration work and nothing is lost:

```bash
sqlite3 /Users/bhan/Documents/works/sites/RedirectManagerTestSite/RmTestSite/umbraco/Data/Umbraco.sqlite.db \
  "select count(*) from RedirectManagerRedirects; select key, state from umbracoKeyValue where key like '%RedirectManager%';"
grep -i "package migration\|RedirectManager" /tmp/rm-site.log | head
```
Expected: the row count is unchanged from before the upgrade (note it first), the state is the last migration GUID
`F5A8B319-4D6C-47E3-A092-8B1D4F7C5E06`, and the log shows no "Starting package migration for BT.RedirectManager" line for the core.

- [ ] **Step 3: Premium still builds and passes against this core**

```bash
cd /Users/bhan/Documents/works/sites/RedirectManagerPremium
dotnet test BT.RedirectManager.Premium.Tests -p:CoreRepoPath=/Users/bhan/Documents/works/sites/RedirectManager-v2-b/ -v q 2>&1 | grep -E "Passed!|Failed|error"
```
Expected: `Passed!  - Failed: 0, Passed: 200`. If a compile error appears, the cleanup broke an extension contract: fix it here, not in Premium.

- [ ] **Step 4: Sample extension still builds**

Run: `dotnet build samples/RedirectManager.SampleExtension -v q 2>&1 | grep -E "error|Error\(s\)"`
Expected: `0 Error(s)`.

---

### Task 6: Merge and record

- [ ] **Step 1: Merge locally and clean up**

```bash
cd /Users/bhan/Documents/works/sites/RedirectManager
git merge --no-ff v2/plan-b -m "Merge plan B: single target (net10.0), AngularJS dashboard removed"
git worktree remove ../RedirectManager-v2-b && git branch -d v2/plan-b
dotnet test Umbraco.RedirectManager.Tests -v q
```
Expected: merge succeeds, all tests pass. Do **not** push a tag. (`main` is now the 2.0 line; pushing `main` is fine, publishing waits for `2.0.0-rc.1` in Plan D.)

- [ ] **Step 2: Update the memory file** `/Users/bhan/.claude/projects/-Users-bhan-Documents-works-sites-RedirectManager/memory/project_monetization_roadmap.md`:
add "Plan B done: main is net10-only 2.0.0-alpha.1, AngularJS removed, golden suite green" and mark Plan C (design pass) as next.

- [ ] **Step 3: Write Plan C** with `superpowers:writing-plans` (design pass and redesigned dialogs), starting from the cleaned-up
`redirect-dashboard.js`, and ask the owner for the first design checkpoint date.

## Self-review against the spec

- net10 only, `#if` and legacy migrations removed, AngularJS and `package.manifest` removed, CI single target: Tasks 1 to 4.
- No schema change and the same migration GUIDs; upgrading a 1.x database runs nothing: Task 5 Step 2.
- No behavior change: Task 5 Step 1 (golden suite) plus the 1.x tests.
- Premium and the sample extension keep building: Task 5 Steps 3 and 4.
- README and marketplace stop claiming Umbraco 13 for 2.0 and point Umbraco 13 users to 1.x: Task 3.
- Out of scope here (and left for later plans): the redesign (C), extension API freeze and patched Umbraco packages (D), Umbraco 19 (E).
