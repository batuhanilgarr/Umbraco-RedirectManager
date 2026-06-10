# BT Redirect Manager

A URL redirect manager plugin for Umbraco CMS **13 and 17**. Manage 301, 302, 404, and 410 redirects directly from the Umbraco backoffice with a modern dashboard, CSV import/export, regex support, and a built-in test tool.

## Screenshots

![BT Redirect Manager – Dashboard](https://raw.githubusercontent.com/batuhanilgarr/Umbraco-RedirectManager/main/assets/1.png)

![BT Redirect Manager – Add New Redirect](https://raw.githubusercontent.com/batuhanilgarr/Umbraco-RedirectManager/main/assets/2.png)

![BT Redirect Manager – Edit Redirect](https://raw.githubusercontent.com/batuhanilgarr/Umbraco-RedirectManager/main/assets/3.png)

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

```bash
dotnet add package BT.RedirectManager
```

Or via the NuGet Package Manager:

```
Install-Package BT.RedirectManager
```

Then build your project — the App_Plugins assets are copied automatically.

## Usage

1. Install the package.
2. Restart your Umbraco application.
3. Navigate to the **Settings** section in the Umbraco backoffice.
4. Open the **Redirect Manager** dashboard.
5. Add, edit, test, or delete redirects, or import/export CSV files for bulk changes.

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

## Links

- [Source code & issues](https://github.com/batuhanilgarr/Umbraco-RedirectManager)
- [NuGet package](https://www.nuget.org/packages/BT.RedirectManager)
