# 8Bitiz Redirect Manager

A URL redirect manager plugin for Umbraco CMS 17 LTS. Manage 301, 302, 404, and 410 redirects directly from the Umbraco backoffice with a modern dashboard, CSV import/export, regex support, and a built-in test tool.

## Features

- **Multiple status codes**: 301 (Permanent), 302 (Temporary), 404 (Not Found), and 410 (Gone).
- **Modern backoffice dashboard**: Clean Umbraco 17 dashboard built with Lit; search, filter, and bulk actions.
- **Regex and exact match**: Support for both exact path redirects and regex rules with capture groups.
- **CSV import/export**: Quickly migrate or bulk edit redirects via CSV.
- **Test tool**: Test a path before saving to see which redirect will match.
- **Database storage**: Redirects stored in a dedicated table, fully controlled from the backoffice.
- **Automatic migration**: Database table created/updated automatically on installation.
- **Auto-update App_Plugins**: App_Plugins assets are copied on build via the included MSBuild targets.

## Installation

### Docker ile kendi NuGet sunucunuz (önerilen)

Kendi NuGet sunucunuzu Docker’da çalıştırıp plugini oraya atar, istediğiniz projede `dotnet add package 8Bitiz.RedirectManager` ile kurarsınız.

1. **NuGet sunucusunu başlatın:** `docker compose -f docker/docker-compose.yml up -d`
2. **Plugini sunucuya gönderin:** `./scripts/push-to-feed.sh` (Windows: `.\scripts\push-to-feed.ps1`)
3. **Kurulum yapacağınız projenin solution klasörüne** `nuget.config` ekleyin (`nuget.config.example` örneğine bakın; feed: `http://localhost:5555/v3/index.json`)
4. **Projede:** `dotnet add package 8Bitiz.RedirectManager` → `dotnet build`

Detaylı adımlar: [docs/NUGET-SUNUCU-VE-KURULUM.md](docs/NUGET-SUNUCU-VE-KURULUM.md)

### From NuGet.org

```bash
dotnet add package 8Bitiz.RedirectManager
```

Or via NuGet Package Manager:
```
Install-Package 8Bitiz.RedirectManager
```

**NuGet sunucusu / yerel feed:** Paketi kendi projelerinize `dotnet add package` ile kurma seçenekleri (yerel klasör, nuget.org, özel BaGet sunucusu) için bkz. [docs/NUGET-SUNUCU-VE-KURULUM.md](docs/NUGET-SUNUCU-VE-KURULUM.md).

### Local Installation (Development)

1. Clone the repository:
```bash
git clone https://github.com/8Bitiz/RedirectManager.git
```

2. Build the package:
```bash
cd RedirectManager
dotnet build
```

3. Add `nuget.config` to your solution folder:
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="LocalFeed" value="/path/to/RedirectManager/bin/Debug/" />
  </packageSources>
</configuration>
```

4. Add the package to your project:
```bash
dotnet add package 8Bitiz.RedirectManager
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
- `StatusCode` (int)
- `CreatedDate` (datetime)
- `UpdatedDate` (datetime)
- `IsActive` (bit)

## License

MIT License

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

**Optional:** To avoid `Co-authored-by: Cursor` in commit messages (e.g. when using Cursor IDE), install the prepare-commit-msg hook:

```bash
cp scripts/prepare-commit-msg.sample .git/hooks/prepare-commit-msg && chmod +x .git/hooks/prepare-commit-msg
```
