# SiteDown Windows

SiteDown Windows is a desktop monitoring app for [SiteDown](https://www.sitedown.app/).

It checks configured websites in the background and notifies the user when a website appears to be down.

## Features

- Background website monitoring
- System tray support
- Custom popup alerts with sound
- Activity log with colored `OK` and `ERROR` lines
- One-time setup token support
- Local settings encrypted with Windows DPAPI
- Single-instance protection
- Start with Windows support
- Update notification block based on `windows_latest_version_code`
- Internet-connection check using `https://www.sitedown.app/api/checkLink.txt`

## Requirements

- Windows 10 or Windows 11
- Visual Studio 2022 or newer
- .NET 8 SDK

## Build in Visual Studio

1. Open `SiteDownWindows.sln`.
2. Select `Release`.
3. Build the project.

## Publish a portable build

From the project folder:

```cmd
dotnet publish SiteDownWindows.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishReadyToRun=true
```

Published files will be created under:

```text
bin\Release\net8.0-windows\win-x64\publish\
```

## Current version

```text
Version: 1.0.5
Version code: 5
```

## Version constants

The current app version is stored in `MainWindow.xaml.cs`:

```csharp
private const int CURRENT_VERSION_CODE = 5;
private const string CURRENT_VERSION = "1.0.5";
```

The app compares `CURRENT_VERSION_CODE` with the API value:

```text
windows_latest_version_code
```

If the API version code is higher, the app displays a yellow update block.

## API endpoints used

The app uses the SiteDown API to load monitoring configuration:

```text
https://sitedown.app/api/config/
```

The app uses this endpoint only after a website connection error/timeout to check whether the internet connection is available:

```text
https://www.sitedown.app/api/checkLink.txt
```

Expected response:

```text
ok
```

## Local settings

Local settings are stored under:

```text
%APPDATA%\SiteDown\settings.dat
```

The settings file is encrypted with Windows DPAPI for the current Windows user.

The one-time setup token is not saved.

## Security note

Do not commit code-signing certificates, private keys, tokens, or build secrets to this repository.

## Author

Developer / owner: **Maksym Matviychyna**

Copyright (C) 2026 Maksym Matviychyna

## License

This project is released under the GNU General Public License v3.0.
