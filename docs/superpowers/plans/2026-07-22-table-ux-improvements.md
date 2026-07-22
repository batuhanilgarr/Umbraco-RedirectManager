# Table UX Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add sticky table headers, click-to-sort columns, and automatic removal of a 404 row once a redirect is created for it, across both the AngularJS dashboard and the Lit dashboard.

**Architecture:** Pure front-end changes, no backend/API/DTO changes. CSS gets `position: sticky` on `th`. A small generic client-side sort helper (`sortRows`) is added once per dashboard and reused for all 4 tables via per-table `{column, direction, type}` state objects. The 404→redirect link reuses the existing local-array-filter pattern already used by "Dismiss".

**Tech Stack:** Plain JS (AngularJS 1.x controller + Lit `LitElement`, no build step, no test framework in this repo for the frontend — verification is manual, in a running Umbraco instance).

**Design doc:** `docs/superpowers/specs/2026-07-22-table-ux-improvements-design.md`

---

## File Structure

- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js` — Lit dashboard: CSS (sticky header + sort indicator), sort state/helpers, 4 tables' `<thead>`/`<tbody>` markup, `saveRedirect()`.
- Modify: `App_Plugins/RedirectManager/redirect.css` — AngularJS dashboard's shared stylesheet: sticky header + sort indicator CSS.
- Modify: `App_Plugins/RedirectManager/redirect.controller.js` — AngularJS controller: sort state/helpers, `saveRedirect()`.
- Modify: `App_Plugins/RedirectManager/dashboard.html` — AngularJS template: 4 tables' `<thead>` (`ng-click`, sort indicator) and `ng-repeat` source expressions.

No new files. No backend files touched.

---

### Task 1: Sticky header CSS — Lit dashboard

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js:390-408`

- [ ] **Step 1: Edit the `table`/`th` CSS rules**

Find (inside `static styles = css\`...\``):

```css
        table {
            width: 100%;
            min-width: 900px;
            border-collapse: collapse;
            background: white;
        }

        th {
            padding: 9px 14px;
            text-align: left;
            font-size: 11px;
            font-weight: 600;
            color: #888;
            background: #fafafa;
            border-bottom: 1px solid #e9e9e9;
            white-space: nowrap;
            text-transform: uppercase;
            letter-spacing: 0.04em;
        }
```

Replace with:

```css
        table {
            width: 100%;
            min-width: 900px;
            border-collapse: separate;
            border-spacing: 0;
            background: white;
        }

        th {
            padding: 9px 14px;
            text-align: left;
            font-size: 11px;
            font-weight: 600;
            color: #888;
            background: #fafafa;
            border-bottom: 1px solid #e9e9e9;
            white-space: nowrap;
            text-transform: uppercase;
            letter-spacing: 0.04em;
            position: sticky;
            top: 0;
            z-index: 1;
        }

        th.sortable {
            cursor: pointer;
            user-select: none;
        }

        th.sortable:hover {
            color: #555;
        }

        .sort-indicator {
            display: inline-block;
            width: 10px;
            margin-left: 2px;
            font-size: 9px;
            color: #4a6fdc;
        }
```

(`border-collapse` switches from `collapse` to `separate` + `border-spacing: 0` because collapsed borders get clipped at the sticky scroll boundary; visual row separation is unaffected since it already comes from `border-bottom` on `th`/`td`, not from collapsed double borders.)

- [ ] **Step 2: Commit**

```bash
git add App_Plugins/RedirectManager/redirect-dashboard.js
git commit -m "feat: sticky table headers in Lit dashboard"
```

---

### Task 2: Sticky header CSS — AngularJS dashboard

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect.css:21-44`

- [ ] **Step 1: Edit the `.redirect-table` CSS rules**

Find:

```css
.redirect-table {
    width: 100%;
    border-collapse: collapse;
    margin-top: 1px;
}

.redirect-table th,
.redirect-table td {
    padding: 10px 14px;
    text-align: left;
    border-bottom: 1px solid #f0f0f0;
    vertical-align: middle;
}

.redirect-table th {
    background-color: #fafafa;
    font-size: 11px;
    font-weight: 600;
    color: #888;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    border-bottom: 1px solid #e9e9e9;
    white-space: nowrap;
}
```

Replace with:

```css
.redirect-table {
    width: 100%;
    border-collapse: separate;
    border-spacing: 0;
    margin-top: 1px;
}

.redirect-table th,
.redirect-table td {
    padding: 10px 14px;
    text-align: left;
    border-bottom: 1px solid #f0f0f0;
    vertical-align: middle;
}

.redirect-table th {
    background-color: #fafafa;
    font-size: 11px;
    font-weight: 600;
    color: #888;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    border-bottom: 1px solid #e9e9e9;
    white-space: nowrap;
    position: sticky;
    top: 0;
    z-index: 1;
}

.redirect-table th.sortable {
    cursor: pointer;
    user-select: none;
}

.redirect-table th.sortable:hover {
    color: #555;
}

.redirect-table .sort-indicator {
    display: inline-block;
    width: 10px;
    margin-left: 2px;
    font-size: 9px;
    color: #4a6fdc;
}
```

- [ ] **Step 2: Commit**

```bash
git add App_Plugins/RedirectManager/redirect.css
git commit -m "feat: sticky table headers in AngularJS dashboard"
```

---

### Task 3: Generic sort helper + state — Lit dashboard

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js:6-32` (properties)
- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js:710-735` (constructor)
- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js` (new methods, placed after `authFetch`, i.e. after line 781)

- [ ] **Step 1: Add 4 new reactive properties**

In `static properties = { ... }`, after the existing `latestVersion: { type: String }` line, add:

```js
        redirectsSort: { type: Object },
        missedSort: { type: Object },
        topRedirectsSort: { type: Object },
        staleRedirectsSort: { type: Object }
```

(Remember to add a trailing comma after `latestVersion: { type: String }` since it's no longer the last entry.)

- [ ] **Step 2: Initialize the 4 sort states in the constructor**

In `constructor()`, after `this.latestVersion = '';`, add:

```js
        this.redirectsSort = { column: null, direction: 'asc', type: 'string' };
        this.missedSort = { column: null, direction: 'asc', type: 'string' };
        this.topRedirectsSort = { column: null, direction: 'asc', type: 'string' };
        this.staleRedirectsSort = { column: null, direction: 'asc', type: 'string' };
```

- [ ] **Step 3: Add the generic sort helper methods**

Immediately after the closing brace of `authFetch(url, options = {})` (right before `async testRedirect(path) {`), add:

```js
    sortRows(rows, column, direction, type) {
        const sign = direction === 'asc' ? 1 : -1;
        return [...rows].sort((a, b) => {
            let av = a[column];
            let bv = b[column];
            if (type === 'date') {
                av = av ? new Date(av).getTime() : 0;
                bv = bv ? new Date(bv).getTime() : 0;
                return sign * (av - bv);
            }
            if (type === 'number') {
                av = Number(av) || 0;
                bv = Number(bv) || 0;
                return sign * (av - bv);
            }
            av = (av ?? '').toString().toLowerCase();
            bv = (bv ?? '').toString().toLowerCase();
            return sign * (av < bv ? -1 : (av > bv ? 1 : 0));
        });
    }

    onSortClick(stateProp, column, type) {
        const state = this[stateProp];
        const direction = (state.column === column && state.direction === 'asc') ? 'desc' : 'asc';
        this[stateProp] = { column, direction, type };
    }

    sortIndicator(stateProp, column) {
        const state = this[stateProp];
        if (state.column !== column) {
            return '';
        }
        return state.direction === 'asc' ? '▲' : '▼';
    }

    get sortedRedirects() {
        const { column, direction, type } = this.redirectsSort;
        return column ? this.sortRows(this.redirects, column, direction, type) : this.redirects;
    }

    get sortedMissedRequests() {
        const { column, direction, type } = this.missedSort;
        return column ? this.sortRows(this.missedRequests, column, direction, type) : this.missedRequests;
    }

    get sortedTopRedirects() {
        const { column, direction, type } = this.topRedirectsSort;
        const rows = this.stats?.topRedirects || [];
        return column ? this.sortRows(rows, column, direction, type) : rows;
    }

    get sortedStaleRedirects() {
        const { column, direction, type } = this.staleRedirectsSort;
        const rows = this.stats?.staleRedirects || [];
        return column ? this.sortRows(rows, column, direction, type) : rows;
    }
```

- [ ] **Step 4: Commit**

```bash
git add App_Plugins/RedirectManager/redirect-dashboard.js
git commit -m "feat: add generic client-side sort helper to Lit dashboard"
```

---

### Task 4: Wire sorting into the Redirects table — Lit dashboard

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js:1500-1520` (thead + map source)

- [ ] **Step 1: Replace the Redirects table `<thead>`**

Find (lines 1500-1518):

```html
                            <thead>
                                <tr>
                                    <th style="width:36px;">
                                        <input type="checkbox" .checked=${this.allSelected} @change=${this.toggleSelectAll} />
                                    </th>
                                    <th style="width:60px;" class="center">Status</th>
                                    <th>Old URL</th>
                                    <th>New URL</th>
                                    <th>Domain</th>
                                    <th>Culture</th>
                                    <th>Notes</th>
                                    <th class="center">Match</th>
                                    <th class="center">Active</th>
                                    <th class="center" title="Hit count">Hits</th>
                                    <th class="center" title="Hits in the last 7 days">7d</th>
                                    <th class="center" title="Hits in the last 30 days">30d</th>
                                    <th></th>
                                </tr>
                            </thead>
```

Replace with:

```html
                            <thead>
                                <tr>
                                    <th style="width:36px;">
                                        <input type="checkbox" .checked=${this.allSelected} @change=${this.toggleSelectAll} />
                                    </th>
                                    <th style="width:60px;" class="center sortable" @click=${() => this.onSortClick('redirectsSort', 'statusCode', 'number')}>
                                        Status<span class="sort-indicator">${this.sortIndicator('redirectsSort', 'statusCode')}</span>
                                    </th>
                                    <th class="sortable" @click=${() => this.onSortClick('redirectsSort', 'oldUrl', 'string')}>
                                        Old URL<span class="sort-indicator">${this.sortIndicator('redirectsSort', 'oldUrl')}</span>
                                    </th>
                                    <th class="sortable" @click=${() => this.onSortClick('redirectsSort', 'newUrl', 'string')}>
                                        New URL<span class="sort-indicator">${this.sortIndicator('redirectsSort', 'newUrl')}</span>
                                    </th>
                                    <th class="sortable" @click=${() => this.onSortClick('redirectsSort', 'domain', 'string')}>
                                        Domain<span class="sort-indicator">${this.sortIndicator('redirectsSort', 'domain')}</span>
                                    </th>
                                    <th class="sortable" @click=${() => this.onSortClick('redirectsSort', 'culture', 'string')}>
                                        Culture<span class="sort-indicator">${this.sortIndicator('redirectsSort', 'culture')}</span>
                                    </th>
                                    <th class="sortable" @click=${() => this.onSortClick('redirectsSort', 'description', 'string')}>
                                        Notes<span class="sort-indicator">${this.sortIndicator('redirectsSort', 'description')}</span>
                                    </th>
                                    <th class="center">Match</th>
                                    <th class="center sortable" @click=${() => this.onSortClick('redirectsSort', 'isActive', 'number')}>
                                        Active<span class="sort-indicator">${this.sortIndicator('redirectsSort', 'isActive')}</span>
                                    </th>
                                    <th class="center sortable" title="Hit count" @click=${() => this.onSortClick('redirectsSort', 'hitCount', 'number')}>
                                        Hits<span class="sort-indicator">${this.sortIndicator('redirectsSort', 'hitCount')}</span>
                                    </th>
                                    <th class="center sortable" title="Hits in the last 7 days" @click=${() => this.onSortClick('redirectsSort', 'hits7d', 'number')}>
                                        7d<span class="sort-indicator">${this.sortIndicator('redirectsSort', 'hits7d')}</span>
                                    </th>
                                    <th class="center sortable" title="Hits in the last 30 days" @click=${() => this.onSortClick('redirectsSort', 'hits30d', 'number')}>
                                        30d<span class="sort-indicator">${this.sortIndicator('redirectsSort', 'hits30d')}</span>
                                    </th>
                                    <th></th>
                                </tr>
                            </thead>
```

- [ ] **Step 2: Point the row map at the sorted getter**

Find:

```js
                                ${this.redirects.map(redirect => html`
```

Replace with:

```js
                                ${this.sortedRedirects.map(redirect => html`
```

- [ ] **Step 3: Commit**

```bash
git add App_Plugins/RedirectManager/redirect-dashboard.js
git commit -m "feat: sortable columns in Lit redirects table"
```

---

### Task 5: Wire sorting into the 404 log table — Lit dashboard

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js:1626-1636`

- [ ] **Step 1: Replace the 404 log table `<thead>`**

Find:

```html
                            <thead>
                                <tr>
                                    <th>Path</th>
                                    <th class="center">Hits</th>
                                    <th class="center">First seen</th>
                                    <th class="center">Last seen</th>
                                    <th></th>
                                </tr>
                            </thead>
```

Replace with:

```html
                            <thead>
                                <tr>
                                    <th class="sortable" @click=${() => this.onSortClick('missedSort', 'path', 'string')}>
                                        Path<span class="sort-indicator">${this.sortIndicator('missedSort', 'path')}</span>
                                    </th>
                                    <th class="center sortable" @click=${() => this.onSortClick('missedSort', 'hitCount', 'number')}>
                                        Hits<span class="sort-indicator">${this.sortIndicator('missedSort', 'hitCount')}</span>
                                    </th>
                                    <th class="center sortable" @click=${() => this.onSortClick('missedSort', 'firstSeenDate', 'date')}>
                                        First seen<span class="sort-indicator">${this.sortIndicator('missedSort', 'firstSeenDate')}</span>
                                    </th>
                                    <th class="center sortable" @click=${() => this.onSortClick('missedSort', 'lastSeenDate', 'date')}>
                                        Last seen<span class="sort-indicator">${this.sortIndicator('missedSort', 'lastSeenDate')}</span>
                                    </th>
                                    <th></th>
                                </tr>
                            </thead>
```

- [ ] **Step 2: Point the row map at the sorted getter**

Find:

```js
                                ${this.missedRequests.map(item => html`
```

Replace with:

```js
                                ${this.sortedMissedRequests.map(item => html`
```

- [ ] **Step 3: Commit**

```bash
git add App_Plugins/RedirectManager/redirect-dashboard.js
git commit -m "feat: sortable columns in Lit 404 log table"
```

---

### Task 6: Wire sorting into the two stats tables — Lit dashboard

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js:1702-1718` (top redirects)
- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js:1731-1751` (stale redirects)

- [ ] **Step 1: Replace the "Top 10 most-used redirects" `<thead>` and map source**

Find:

```html
                                    <thead>
                                        <tr>
                                            <th>Old URL</th>
                                            <th>New URL</th>
                                            <th class="center">Hits</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        ${this.stats.topRedirects.map(r => html`
```

Replace with:

```html
                                    <thead>
                                        <tr>
                                            <th class="sortable" @click=${() => this.onSortClick('topRedirectsSort', 'oldUrl', 'string')}>
                                                Old URL<span class="sort-indicator">${this.sortIndicator('topRedirectsSort', 'oldUrl')}</span>
                                            </th>
                                            <th class="sortable" @click=${() => this.onSortClick('topRedirectsSort', 'newUrl', 'string')}>
                                                New URL<span class="sort-indicator">${this.sortIndicator('topRedirectsSort', 'newUrl')}</span>
                                            </th>
                                            <th class="center sortable" @click=${() => this.onSortClick('topRedirectsSort', 'hitCount', 'number')}>
                                                Hits<span class="sort-indicator">${this.sortIndicator('topRedirectsSort', 'hitCount')}</span>
                                            </th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        ${this.sortedTopRedirects.map(r => html`
```

- [ ] **Step 2: Replace the "stale redirects" `<thead>` and map source**

Find:

```html
                                    <thead>
                                        <tr>
                                            <th>Old URL</th>
                                            <th>New URL</th>
                                            <th class="center">All-time hits</th>
                                            <th class="center">Last hit</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        ${this.stats.staleRedirects.map(r => html`
```

Replace with:

```html
                                    <thead>
                                        <tr>
                                            <th class="sortable" @click=${() => this.onSortClick('staleRedirectsSort', 'oldUrl', 'string')}>
                                                Old URL<span class="sort-indicator">${this.sortIndicator('staleRedirectsSort', 'oldUrl')}</span>
                                            </th>
                                            <th class="sortable" @click=${() => this.onSortClick('staleRedirectsSort', 'newUrl', 'string')}>
                                                New URL<span class="sort-indicator">${this.sortIndicator('staleRedirectsSort', 'newUrl')}</span>
                                            </th>
                                            <th class="center sortable" @click=${() => this.onSortClick('staleRedirectsSort', 'hitCount', 'number')}>
                                                All-time hits<span class="sort-indicator">${this.sortIndicator('staleRedirectsSort', 'hitCount')}</span>
                                            </th>
                                            <th class="center sortable" @click=${() => this.onSortClick('staleRedirectsSort', 'lastHitDate', 'date')}>
                                                Last hit<span class="sort-indicator">${this.sortIndicator('staleRedirectsSort', 'lastHitDate')}</span>
                                            </th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        ${this.sortedStaleRedirects.map(r => html`
```

- [ ] **Step 3: Commit**

```bash
git add App_Plugins/RedirectManager/redirect-dashboard.js
git commit -m "feat: sortable columns in Lit stats tables"
```

---

### Task 7: Remove 404 row on redirect creation — Lit dashboard

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect-dashboard.js:1262-1265`

- [ ] **Step 1: Filter `missedRequests` in the create branch of `saveRedirect()`**

Find:

```js
                } else {
                    this.redirects = [saved, ...this.redirects];
                    this.showMessage(`Redirect created.${overlapNote}`, overlapNote ? 'warning' : 'success');
                }
```

Replace with:

```js
                } else {
                    this.redirects = [saved, ...this.redirects];
                    this.missedRequests = this.missedRequests.filter(m => {
                        const samePath = (m.path || '').toLowerCase() === (saved.oldUrl || '').toLowerCase();
                        const sameDomain = (m.domain || '').toLowerCase() === (saved.domain || '').toLowerCase();
                        return !(samePath && sameDomain);
                    });
                    this.showMessage(`Redirect created.${overlapNote}`, overlapNote ? 'warning' : 'success');
                }
```

- [ ] **Step 2: Commit**

```bash
git add App_Plugins/RedirectManager/redirect-dashboard.js
git commit -m "feat: remove 404 row from Lit dashboard once its redirect is created"
```

---

### Task 8: Generic sort helper + state — AngularJS controller

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect.controller.js:13-23` (vm state)
- Modify: `App_Plugins/RedirectManager/redirect.controller.js` (new methods, placed after `vm.getAuditTitle`, before `vm.loadRedirects`, i.e. after line 106)

- [ ] **Step 1: Add 4 sort-state fields**

Find:

```js
        vm.updateAvailable = false;
        vm.currentVersion = '';
        vm.latestVersion = '';
```

Replace with:

```js
        vm.updateAvailable = false;
        vm.currentVersion = '';
        vm.latestVersion = '';
        vm.redirectsSort = { column: null, direction: 'asc', type: 'string' };
        vm.missedSort = { column: null, direction: 'asc', type: 'string' };
        vm.topRedirectsSort = { column: null, direction: 'asc', type: 'string' };
        vm.staleRedirectsSort = { column: null, direction: 'asc', type: 'string' };
```

- [ ] **Step 2: Add the generic sort helper functions**

Immediately after the closing brace of `vm.getAuditTitle` (right before `vm.loadRedirects = function () {`), add:

```js
        vm.sortRows = function (rows, column, direction, type) {
            var sign = direction === "asc" ? 1 : -1;
            return rows.slice().sort(function (a, b) {
                var av = a[column];
                var bv = b[column];
                if (type === "date") {
                    av = av ? new Date(av).getTime() : 0;
                    bv = bv ? new Date(bv).getTime() : 0;
                    return sign * (av - bv);
                }
                if (type === "number") {
                    av = Number(av) || 0;
                    bv = Number(bv) || 0;
                    return sign * (av - bv);
                }
                av = (av || "").toString().toLowerCase();
                bv = (bv || "").toString().toLowerCase();
                return sign * (av < bv ? -1 : (av > bv ? 1 : 0));
            });
        };

        vm.sortBy = function (stateName, column, type) {
            var state = vm[stateName];
            var direction = (state.column === column && state.direction === "asc") ? "desc" : "asc";
            vm[stateName] = { column: column, direction: direction, type: type };
        };

        vm.sortIndicator = function (stateName, column) {
            var state = vm[stateName];
            if (state.column !== column) {
                return "";
            }
            return state.direction === "asc" ? "▲" : "▼";
        };

        vm.sortedRedirects = function () {
            var s = vm.redirectsSort;
            return s.column ? vm.sortRows(vm.redirects, s.column, s.direction, s.type) : vm.redirects;
        };

        vm.sortedMissedRequests = function () {
            var s = vm.missedSort;
            return s.column ? vm.sortRows(vm.missedRequests, s.column, s.direction, s.type) : vm.missedRequests;
        };

        vm.sortedTopRedirects = function () {
            var s = vm.topRedirectsSort;
            var rows = (vm.stats && vm.stats.topRedirects) || [];
            return s.column ? vm.sortRows(rows, s.column, s.direction, s.type) : rows;
        };

        vm.sortedStaleRedirects = function () {
            var s = vm.staleRedirectsSort;
            var rows = (vm.stats && vm.stats.staleRedirects) || [];
            return s.column ? vm.sortRows(rows, s.column, s.direction, s.type) : rows;
        };
```

- [ ] **Step 3: Commit**

```bash
git add App_Plugins/RedirectManager/redirect.controller.js
git commit -m "feat: add generic client-side sort helper to AngularJS controller"
```

---

### Task 9: Wire sorting into the Redirects and 404 log tables — AngularJS template

**Files:**
- Modify: `App_Plugins/RedirectManager/dashboard.html:119-137` (redirects thead + ng-repeat)
- Modify: `App_Plugins/RedirectManager/dashboard.html:235-244` (404 log thead + ng-repeat)

- [ ] **Step 1: Replace the Redirects table `<thead>` and `ng-repeat` source**

Find:

```html
                <table ng-if="!vm.loading && vm.redirects.length > 0" class="redirect-table">
                    <thead>
                        <tr>
                            <th style="width:60px;text-align:center;">Status</th>
                            <th>Old URL</th>
                            <th>New URL</th>
                            <th>Domain</th>
                            <th>Culture</th>
                            <th>Notes</th>
                            <th style="text-align:center;">Match</th>
                            <th style="text-align:center;">Active</th>
                            <th style="text-align:center;" title="Hit count">Hits</th>
                            <th style="text-align:center;" title="Hits in the last 7 days">7d</th>
                            <th style="text-align:center;" title="Hits in the last 30 days">30d</th>
                            <th></th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr ng-repeat="redirect in vm.redirects" title="{{vm.getAuditTitle(redirect)}}">
```

Replace with:

```html
                <table ng-if="!vm.loading && vm.redirects.length > 0" class="redirect-table">
                    <thead>
                        <tr>
                            <th class="sortable" style="width:60px;text-align:center;" ng-click="vm.sortBy('redirectsSort', 'statusCode', 'number')">
                                Status<span class="sort-indicator">{{vm.sortIndicator('redirectsSort', 'statusCode')}}</span>
                            </th>
                            <th class="sortable" ng-click="vm.sortBy('redirectsSort', 'oldUrl', 'string')">
                                Old URL<span class="sort-indicator">{{vm.sortIndicator('redirectsSort', 'oldUrl')}}</span>
                            </th>
                            <th class="sortable" ng-click="vm.sortBy('redirectsSort', 'newUrl', 'string')">
                                New URL<span class="sort-indicator">{{vm.sortIndicator('redirectsSort', 'newUrl')}}</span>
                            </th>
                            <th class="sortable" ng-click="vm.sortBy('redirectsSort', 'domain', 'string')">
                                Domain<span class="sort-indicator">{{vm.sortIndicator('redirectsSort', 'domain')}}</span>
                            </th>
                            <th class="sortable" ng-click="vm.sortBy('redirectsSort', 'culture', 'string')">
                                Culture<span class="sort-indicator">{{vm.sortIndicator('redirectsSort', 'culture')}}</span>
                            </th>
                            <th class="sortable" ng-click="vm.sortBy('redirectsSort', 'description', 'string')">
                                Notes<span class="sort-indicator">{{vm.sortIndicator('redirectsSort', 'description')}}</span>
                            </th>
                            <th style="text-align:center;">Match</th>
                            <th class="sortable" style="text-align:center;" ng-click="vm.sortBy('redirectsSort', 'isActive', 'number')">
                                Active<span class="sort-indicator">{{vm.sortIndicator('redirectsSort', 'isActive')}}</span>
                            </th>
                            <th class="sortable" style="text-align:center;" title="Hit count" ng-click="vm.sortBy('redirectsSort', 'hitCount', 'number')">
                                Hits<span class="sort-indicator">{{vm.sortIndicator('redirectsSort', 'hitCount')}}</span>
                            </th>
                            <th class="sortable" style="text-align:center;" title="Hits in the last 7 days" ng-click="vm.sortBy('redirectsSort', 'hits7d', 'number')">
                                7d<span class="sort-indicator">{{vm.sortIndicator('redirectsSort', 'hits7d')}}</span>
                            </th>
                            <th class="sortable" style="text-align:center;" title="Hits in the last 30 days" ng-click="vm.sortBy('redirectsSort', 'hits30d', 'number')">
                                30d<span class="sort-indicator">{{vm.sortIndicator('redirectsSort', 'hits30d')}}</span>
                            </th>
                            <th></th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr ng-repeat="redirect in vm.sortedRedirects()" title="{{vm.getAuditTitle(redirect)}}">
```

- [ ] **Step 2: Replace the 404 log table `<thead>` and `ng-repeat` source**

Find:

```html
                <table ng-if="!vm.missedLoading && vm.missedRequests.length > 0" class="redirect-table">
                    <thead>
                        <tr>
                            <th>Path</th>
                            <th style="text-align:center;">Hits</th>
                            <th style="text-align:center;">First seen</th>
                            <th style="text-align:center;">Last seen</th>
                            <th></th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr ng-repeat="item in vm.missedRequests">
```

Replace with:

```html
                <table ng-if="!vm.missedLoading && vm.missedRequests.length > 0" class="redirect-table">
                    <thead>
                        <tr>
                            <th class="sortable" ng-click="vm.sortBy('missedSort', 'path', 'string')">
                                Path<span class="sort-indicator">{{vm.sortIndicator('missedSort', 'path')}}</span>
                            </th>
                            <th class="sortable" style="text-align:center;" ng-click="vm.sortBy('missedSort', 'hitCount', 'number')">
                                Hits<span class="sort-indicator">{{vm.sortIndicator('missedSort', 'hitCount')}}</span>
                            </th>
                            <th class="sortable" style="text-align:center;" ng-click="vm.sortBy('missedSort', 'firstSeenDate', 'date')">
                                First seen<span class="sort-indicator">{{vm.sortIndicator('missedSort', 'firstSeenDate')}}</span>
                            </th>
                            <th class="sortable" style="text-align:center;" ng-click="vm.sortBy('missedSort', 'lastSeenDate', 'date')">
                                Last seen<span class="sort-indicator">{{vm.sortIndicator('missedSort', 'lastSeenDate')}}</span>
                            </th>
                            <th></th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr ng-repeat="item in vm.sortedMissedRequests()">
```

- [ ] **Step 3: Commit**

```bash
git add App_Plugins/RedirectManager/dashboard.html
git commit -m "feat: sortable columns in AngularJS redirects and 404 log tables"
```

---

### Task 10: Wire sorting into the two stats tables — AngularJS template

**Files:**
- Modify: `App_Plugins/RedirectManager/dashboard.html:313-322` (top redirects)
- Modify: `App_Plugins/RedirectManager/dashboard.html:339-349` (stale redirects)

- [ ] **Step 1: Replace the "Top 10 most-used redirects" `<thead>` and `ng-repeat` source**

Find:

```html
                        <table ng-if="vm.stats.topRedirects.length > 0" class="redirect-table">
                            <thead>
                                <tr>
                                    <th>Old URL</th>
                                    <th>New URL</th>
                                    <th style="text-align:center;">Hits</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr ng-repeat="r in vm.stats.topRedirects">
```

Replace with:

```html
                        <table ng-if="vm.stats.topRedirects.length > 0" class="redirect-table">
                            <thead>
                                <tr>
                                    <th class="sortable" ng-click="vm.sortBy('topRedirectsSort', 'oldUrl', 'string')">
                                        Old URL<span class="sort-indicator">{{vm.sortIndicator('topRedirectsSort', 'oldUrl')}}</span>
                                    </th>
                                    <th class="sortable" ng-click="vm.sortBy('topRedirectsSort', 'newUrl', 'string')">
                                        New URL<span class="sort-indicator">{{vm.sortIndicator('topRedirectsSort', 'newUrl')}}</span>
                                    </th>
                                    <th class="sortable" style="text-align:center;" ng-click="vm.sortBy('topRedirectsSort', 'hitCount', 'number')">
                                        Hits<span class="sort-indicator">{{vm.sortIndicator('topRedirectsSort', 'hitCount')}}</span>
                                    </th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr ng-repeat="r in vm.sortedTopRedirects()">
```

- [ ] **Step 2: Replace the "stale redirects" `<thead>` and `ng-repeat` source**

Find:

```html
                        <table ng-if="vm.stats.staleRedirects.length > 0" class="redirect-table">
                            <thead>
                                <tr>
                                    <th>Old URL</th>
                                    <th>New URL</th>
                                    <th style="text-align:center;">All-time hits</th>
                                    <th style="text-align:center;">Last hit</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr ng-repeat="r in vm.stats.staleRedirects">
```

Replace with:

```html
                        <table ng-if="vm.stats.staleRedirects.length > 0" class="redirect-table">
                            <thead>
                                <tr>
                                    <th class="sortable" ng-click="vm.sortBy('staleRedirectsSort', 'oldUrl', 'string')">
                                        Old URL<span class="sort-indicator">{{vm.sortIndicator('staleRedirectsSort', 'oldUrl')}}</span>
                                    </th>
                                    <th class="sortable" ng-click="vm.sortBy('staleRedirectsSort', 'newUrl', 'string')">
                                        New URL<span class="sort-indicator">{{vm.sortIndicator('staleRedirectsSort', 'newUrl')}}</span>
                                    </th>
                                    <th class="sortable" style="text-align:center;" ng-click="vm.sortBy('staleRedirectsSort', 'hitCount', 'number')">
                                        All-time hits<span class="sort-indicator">{{vm.sortIndicator('staleRedirectsSort', 'hitCount')}}</span>
                                    </th>
                                    <th class="sortable" style="text-align:center;" ng-click="vm.sortBy('staleRedirectsSort', 'lastHitDate', 'date')">
                                        Last hit<span class="sort-indicator">{{vm.sortIndicator('staleRedirectsSort', 'lastHitDate')}}</span>
                                    </th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr ng-repeat="r in vm.sortedStaleRedirects()">
```

- [ ] **Step 3: Commit**

```bash
git add App_Plugins/RedirectManager/dashboard.html
git commit -m "feat: sortable columns in AngularJS stats tables"
```

---

### Task 11: Remove 404 row on redirect creation — AngularJS controller

**Files:**
- Modify: `App_Plugins/RedirectManager/redirect.controller.js:257-267`

- [ ] **Step 1: Filter `vm.missedRequests` in the create-success callback of `vm.saveRedirect`**

Find:

```js
            } else {
                redirectResource.create(redirect).then(function (response) {
                    notificationsService.success("Success", "Redirect created successfully");
                    vm.notifyOverlapWarnings(response.data);
                    vm.closeModal();
                    vm.loadRedirects();
                }, function (error) {
                    notificationsService.error("Error", error.data || "Failed to create redirect");
                    model.submitButtonState = "error";
                });
            }
```

Replace with:

```js
            } else {
                redirectResource.create(redirect).then(function (response) {
                    notificationsService.success("Success", "Redirect created successfully");
                    vm.notifyOverlapWarnings(response.data);
                    vm.missedRequests = vm.missedRequests.filter(function (m) {
                        var samePath = (m.path || "").toLowerCase() === (response.data.oldUrl || "").toLowerCase();
                        var sameDomain = (m.domain || "").toLowerCase() === (response.data.domain || "").toLowerCase();
                        return !(samePath && sameDomain);
                    });
                    vm.closeModal();
                    vm.loadRedirects();
                }, function (error) {
                    notificationsService.error("Error", error.data || "Failed to create redirect");
                    model.submitButtonState = "error";
                });
            }
```

- [ ] **Step 2: Commit**

```bash
git add App_Plugins/RedirectManager/redirect.controller.js
git commit -m "feat: remove 404 row from AngularJS dashboard once its redirect is created"
```

---

### Task 12: Manual verification in a running Umbraco instance

**Files:** none (verification only)

- [ ] **Step 1: Launch the app** (use the project's `run` skill, or the user's own local Umbraco test site, to build and start the site with this package loaded)

- [ ] **Step 2: Verify sticky headers** — open the Lit dashboard's "Redirects" tab with enough rows to scroll (or shrink the browser window), scroll down, confirm the header row stays pinned at the top of the table. Repeat for the "404 log" and "Overview" (both stats tables) tabs. Then repeat all of this in the legacy AngularJS dashboard (accessible in older Umbraco versions / via the classic backoffice route this package still ships).

- [ ] **Step 3: Verify sorting** — in each of the 4 tables (both dashboards), click every sortable column header once (ascending, indicator ▲ appears) and again (descending, indicator ▼ appears, rows re-order), confirm string columns sort alphabetically, number columns numerically, date columns chronologically, and that a different column's click resets that column to ascending while clearing the previous column's indicator.

- [ ] **Step 4: Verify 404 removal** — in the "404 log" tab, note a path with a 404 entry, click "Create redirect", fill in a New URL, save. Confirm: (a) the success message appears, (b) the new entry appears in the "Redirects" tab, (c) the 404 row for that path disappears from the "404 log" tab without a manual refresh. Repeat in both dashboards.

- [ ] **Step 5: Report back** — if all checks pass, this plan is complete and ready for the version bump / changelog / publish step (handled separately, per [[project_roadmap_batch_release_goal]]). If any check fails, note exactly which table/dashboard/step failed before moving on.
