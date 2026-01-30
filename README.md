# Umbraco Redirect Manager

A URL redirect manager plugin for Umbraco CMS (versions 13-17). Manage 301, 302, 404, and 410 redirects directly from the Umbraco backoffice.

## Features

- **Multiple Status Codes**: Support for 301 (Permanent), 302 (Temporary), 404 (Not Found), and 410 (Gone)
- **Backoffice Dashboard**: Easy-to-use interface for managing redirects
- **Database Storage**: Redirects stored in a custom database table
- **Automatic Migration**: Database table created automatically on installation
- **Umbraco 13-17 Compatible**: Works with both AngularJS (v13) and Lit/TypeScript (v14+) backoffice

## Installation

```bash
dotnet add package Umbraco.RedirectManager
```

Or via NuGet Package Manager:
```
Install-Package Umbraco.RedirectManager
```

## Usage

1. Install the package
2. Restart your Umbraco application
3. Navigate to the "Settings" section in the backoffice
4. Click on "Redirect Manager" dashboard
5. Add, edit, or delete redirects as needed

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
