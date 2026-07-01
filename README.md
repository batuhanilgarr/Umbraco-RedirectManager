# BT Redirect Manager

A URL redirect manager plugin for Umbraco CMS **13, 17, and 18**. Manage 301, 302, 404, and 410 redirects directly from the Umbraco backoffice with a modern dashboard, CSV import/export, regex support, and a built-in test tool.

## Screenshots

![BT Redirect Manager – Dashboard](https://raw.githubusercontent.com/batuhanilgarr/Umbraco-RedirectManager/refs/heads/main/assets/1.png)

![BT Redirect Manager – Add New Redirect](https://raw.githubusercontent.com/batuhanilgarr/Umbraco-RedirectManager/refs/heads/main/assets/2.png)

![BT Redirect Manager – Edit Redirect](https://raw.githubusercontent.com/batuhanilgarr/Umbraco-RedirectManager/refs/heads/main/assets/3.png)

## Features

- **Multiple status codes**: 301 (Permanent), 302 (Temporary), 404 (Not Found), and 410 (Gone).
- **Modern backoffice dashboard**: Clean Umbraco 17 dashboard built with Lit; search, filter, and bulk actions.
- **Regex and exact match**: Support for both exact path redirects and regex rules with capture groups.
- **Domain-scoped redirects**: Optionally scope a redirect to a specific hostname for multi-site installs — the same Old URL can point to a different New URL per domain, with domain-specific rules taking precedence over "all domains" ones. Leave the Domain field blank to apply a redirect everywhere.
- **Hit-count analytics**: Every redirect tracks how many times it's fired and when it was last hit, visible right in the dashboard.
- **404 log with one-click redirect creation**: Genuine 404s are logged automatically (not just unmatched lookups), with a "Create Redirect" action to turn a frequent 404 into a redirect in one click.
- **CSV import/export**: Quickly migrate or bulk edit redirects via CSV.
- **Test tool**: Test a path before saving to see which redirect will match.
- **Backoffice-secured API**: All redirect-management endpoints require an authenticated Umbraco backoffice session.
- **Database storage**: Redirects stored in a dedicated table, fully controlled from the backoffice.
- **Automatic migration**: Database tables created/updated automatically on installation.
- **Auto-update App_Plugins**: App_Plugins assets are copied on build via the included MSBuild targets.

## Installation

### From NuGet.org

```bash
dotnet add package BT.RedirectManager
```

Or via the NuGet Package Manager:
```
Install-Package BT.RedirectManager
```

### Self-hosted NuGet feed (Docker, optional)

If you'd rather run your own feed instead of nuget.org, this repo includes a Docker Compose setup for [BaGet](https://github.com/loic-sharma/BaGet):

1. **Start the feed:** `docker compose -f docker/docker-compose.yml up -d`
2. **Push this package to it:** `./scripts/push-to-feed.sh` (Windows: `.\scripts\push-to-feed.ps1`)
3. **Add the feed** to the `nuget.config` of the solution where you want to install the package (feed: `http://localhost:5555/v3/index.json`)
4. **Install:** `dotnet add package BT.RedirectManager` then `dotnet build`

### Local Installation (Development)

1. Clone the repository:
```bash
git clone https://github.com/batuhanilgarr/Umbraco-RedirectManager.git
```

2. Build the package:
```bash
cd Umbraco-RedirectManager
dotnet build
```

3. Add `nuget.config` to your solution folder:
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="LocalFeed" value="/path/to/Umbraco-RedirectManager/bin/Debug/" />
  </packageSources>
</configuration>
```

4. Add the package to your project:
```bash
dotnet add package BT.RedirectManager
```

5. Build your project (App_Plugins files will be copied automatically):
```bash
dotnet build
```

## Usage

1. Install the package.
2. Restart your Umbraco application.
3. Navigate to the **Settings** section in the Umbraco backoffice.
4. Open the **Redirect Manager** dashboard.
5. Add, edit, test, or delete redirects as needed, or import/export CSV files for bulk changes.

## Status Codes

| Code | Description |
|------|-------------|
| 301  | Permanent Redirect - Use when a page has permanently moved |
| 302  | Temporary Redirect - Use when a page has temporarily moved |
| 404  | Not Found - Returns a 404 error for the URL |
| 410  | Gone - Indicates the resource is permanently gone |

## Configuration

No additional configuration required. The plugin works out of the box.

## Database

The plugin creates a table called `RedirectManagerEntries` with the following structure:

- `Id` (int, PK)
- `OldUrl` (nvarchar)
- `NewUrl` (nvarchar, nullable)
- `Domain` (nvarchar, nullable — blank/null means the redirect applies to all domains)
- `Description` (nvarchar, nullable)
- `StatusCode` (int)
- `CreatedDate` (datetime)
- `UpdatedDate` (datetime)
- `IsActive` (bit)
- `IsRegex` (bit)
- `HitCount` (int)
- `LastHitDate` (datetime, nullable)

It also creates a `RedirectManagerMissedRequests` table that logs genuine
404 responses (path, hit count, first/last seen) so they can be turned into
redirects from the dashboard's "404 Log" tab. Entries older than 90 days are
cleaned up automatically.

## License

MIT License

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

**Optional:** To avoid `Co-authored-by: Cursor` in commit messages (e.g. when using Cursor IDE), install the prepare-commit-msg hook:

```bash
cp scripts/prepare-commit-msg.sample .git/hooks/prepare-commit-msg && chmod +x .git/hooks/prepare-commit-msg
```
