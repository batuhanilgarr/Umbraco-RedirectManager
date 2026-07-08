# BT Redirect Manager

A URL redirect manager plugin for Umbraco CMS **13, 17, and 18**. Manage 301, 302, 404, and 410 redirects directly from the Umbraco backoffice with a redesigned modern dashboard, CSV import/export, regex support, domain scoping, hit-count analytics, and a built-in test tool.

## Screenshots

![BT Redirect Manager – Dashboard](https://raw.githubusercontent.com/batuhanilgarr/Umbraco-RedirectManager/refs/heads/main/assets/1.png)

![BT Redirect Manager – Add New Redirect](https://raw.githubusercontent.com/batuhanilgarr/Umbraco-RedirectManager/refs/heads/main/assets/2.png)

![BT Redirect Manager – Edit Redirect](https://raw.githubusercontent.com/batuhanilgarr/Umbraco-RedirectManager/refs/heads/main/assets/3.png)

## Features

- **Multiple status codes**: 301 (Permanent), 302 (Temporary), 404 (Not Found), and 410 (Gone) — each with a distinct soft-color badge in the dashboard.
- **Redesigned backoffice dashboard**: Clean, modern UI built with Lit (Umbraco 17/18) and AngularJS (Umbraco 13). Compact status legend, search bar, filters, bulk selection, and tab-based navigation all in one view.
- **Domain-scoped redirects**: Scope a redirect to a specific hostname for multi-site installs. The same Old URL can point to a different New URL per domain, with domain-specific rules taking precedence over global ones. Leave the Domain field blank to apply a redirect to all domains.
- **Regex and exact match**: Support for both exact path redirects and regex rules with `$1` capture groups. Regex rules are highlighted with a purple pill in the dashboard.
- **Hit-count analytics**: Every redirect tracks how many times it has fired and when it was last hit, plus rolling 7-day and 30-day totals, visible directly in the redirect list — useful for spotting stale redirects to retire or rules that aren't firing when they should.
- **404 log with one-click redirect creation**: Genuine 404s are logged automatically with hit count, first seen, and last seen dates. Turn any frequent 404 into a redirect in a single click.
- **CSV import/export**: Migrate or bulk-edit redirects via CSV files.
- **Scheduled CSV backup**: Opt-in periodic backup of all redirects to a local folder and/or by email (`RedirectManager:Backup` config), independent of manual export.
- **A/B testing**: Split traffic on a 301/302 exact-match rule between two target URLs by percentage, with per-visitor sticky assignment (cookie) and separate hit counts per variant.
- **Dashboard overview**: Totals, active/inactive counts, top 10 most-used redirects, and active redirects with zero hits in the last 30 days — exportable as CSV, with an optional periodic summary email.
- **Trailing-slash matching**: An exact-match rule fires regardless of a trailing-slash mismatch between the request and the stored Old URL.
- **Built-in test tool**: Test a path before saving to confirm which redirect rule will match.
- **Backoffice-secured API**: All redirect-management endpoints require an authenticated Umbraco backoffice session with a valid bearer token.
- **Automatic database migration**: Tables are created and updated automatically on first run — no manual SQL required.
- **Auto-copy App_Plugins**: App_Plugins assets are copied to the output directory on build via the included MSBuild targets file.

## Installation

```bash
dotnet add package BT.RedirectManager
```

Or via the NuGet Package Manager:

```
Install-Package BT.RedirectManager
```

After installation, restart your Umbraco application and open the **Redirect Manager** dashboard from the **Settings** section in the backoffice.

## Usage

1. Navigate to **Settings → Redirect Manager** in the Umbraco backoffice.
2. Click **+ Add redirect** to create a new redirect rule.
3. Fill in the Old URL, New URL, status code, and optionally a domain and notes.
4. Toggle **Active** to enable or disable the rule without deleting it.
5. Enable **Regex match** to use regular expression patterns and `$1` capture groups in the New URL.
6. Use the **Test** button on any row to verify a redirect resolves as expected.
7. Switch to the **404 Log** tab to review unmatched requests and convert them to redirects with one click.
8. Use **Export CSV** / **Import CSV** for bulk operations.

## Status Codes

| Code | Label | Description |
|------|-------|-------------|
| 301  | Permanent | The resource has permanently moved to the new URL. Browsers and search engines update their records. |
| 302  | Temporary | The resource has temporarily moved. Search engines keep the original URL indexed. |
| 404  | Not Found | Returns a 404 response for the matched URL. Useful for explicitly blocking paths. |
| 410  | Gone | Signals that the resource is permanently gone with no replacement. |

## Database

The plugin creates three tables automatically:

**`RedirectManagerEntries`**

| Column | Type | Notes |
|--------|------|-------|
| Id | int PK | Auto-increment |
| OldUrl | nvarchar | Path or regex pattern |
| NewUrl | nvarchar | Target path (nullable for 404/410) |
| Domain | nvarchar | Hostname filter — null/blank applies to all domains |
| Description | nvarchar | Optional notes |
| StatusCode | int | 301, 302, 404, or 410 |
| IsActive | bit | Enable/disable without deleting |
| IsRegex | bit | Treat OldUrl as a regex pattern |
| HitCount | int | Total number of times this rule fired (variant A, if A/B testing) |
| LastHitDate | datetime | Timestamp of the most recent hit |
| VariantBUrl | nvarchar | A/B test target URL — null means this rule isn't an A/B test |
| VariantBWeight | int | % of visitors sent to Variant B |
| VariantBHitCount | int | Total number of times Variant B fired |
| VariantBLastHitDate | datetime | Timestamp of the most recent Variant B hit |
| CreatedDate | datetime | |
| UpdatedDate | datetime | |

**`RedirectManagerHitDaily`**

One row per redirect per UTC day, used to compute the 7-day/30-day rolling
totals shown in the dashboard. Rows older than 35 days are pruned
automatically.

**`RedirectManagerMissedRequests`**

Logs genuine 404 responses (path, hit count, first seen, last seen). Entries older than 90 days are cleaned up automatically.

## Configuration

No additional configuration required. The plugin works out of the box after installation.

## License

MIT

## Contributing

Contributions are welcome! Feel free to open an issue or submit a pull request.
