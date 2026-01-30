# 8Bitiz Redirect Manager

A URL redirect manager plugin for Umbraco CMS 17 LTS. Manage 301, 302, 404, and 410 redirects directly from the Umbraco backoffice.

## Features

- **Multiple Status Codes**: Support for 301 (Permanent), 302 (Temporary), 404 (Not Found), and 410 (Gone)
- **Backoffice Dashboard**: Easy-to-use interface for managing redirects
- **Database Storage**: Redirects stored in a custom database table
- **Automatic Migration**: Database table created automatically on installation
- **Auto-update App_Plugins**: Files are automatically copied on build

## Installation

### From NuGet.org

```bash
dotnet add package 8Bitiz.RedirectManager
```

Or via NuGet Package Manager:
```
Install-Package 8Bitiz.RedirectManager
```

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
