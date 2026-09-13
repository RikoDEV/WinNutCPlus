WinNutCPlus is a modern Windows desktop client for connecting to a [Network UPS Tools](https://networkupstools.org/) (NUT) monitoring server. It's a full rewrite of the original WinNUT-Client on Avalonia UI + .NET 8.

- 📈 Monitor important values of your UPS like voltage, load, and power consumption
- 🌩️ Receive notifications for abnormal power conditions (power outage)
- ❤️ Keep your hardware and data safe with configurable suspend and shutdown triggers
- 🔁 Reliable reconnect after your PC wakes from sleep
- 🌐 Available in English, German, French, Polish, Russian, Ukrainian, Simplified Chinese, and Traditional Chinese
- 🎨 Light and dark themes, with an in-app language override

<br />

# Installation

1. Get the [latest release](https://github.com/RikoDEV/WinNutCPlus/releases)
2. Run the installer and follow the prompts
3. If you have an existing WinNUT-Client install, your settings are imported automatically on first launch
4. Start WinNutCPlus and configure your NUT server connection in Preferences

## Synology NAS

If you are connecting to a Synology NAS with a UPS attached, there is some additional configuration that needs to be done.

Referring to the [Synology documentation](https://kb.synology.com/en-us/DSM/help/DSM/AdminCenter/system_hardware_ups?version=7), note that you must add your client computer's IP address to the *Permitted DiskStation Devices* window. In addition, WinNutCPlus requires the following settings:

- **Login**: upsmon
- **Password**: secret
- **UPS Name**: ups

## QNAP NAS

If your NUT server is hosted on a QNAP NAS, be sure to provide the following connection information (default):

- **UPS Name**: qnapups
- (Login and Password can be empty)

Also check the "Enable network UPS master" box on the Control Panel → External device page on the QNAP web interface, and add the IP address of the WinNutCPlus machine to allow it to connect.

# Building from source

The project lives under `WinNUT.Avalonia/`:

```
WinNUT.Avalonia/
  src/
    WinNUT.Core/     # NUT protocol, device model, settings, logging, updater (no UI dependency)
    WinNUT.App/      # Avalonia UI app (views, viewmodels, controls, assets)
  WinNUT.Core.Tests/ # xUnit test suite
  installer/         # Inno Setup script
```

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet build WinNUT.Avalonia/src/WinNUT.App/WinNUT.App.csproj
dotnet test WinNUT.Avalonia/WinNUT.Core.Tests/WinNUT.Core.Tests.csproj
```

An installer can be produced with [Inno Setup 6+](https://jrsoftware.org/isinfo.php) after a self-contained publish:

```
dotnet publish WinNUT.Avalonia/src/WinNUT.App/WinNUT.App.csproj -c Release -r win-x64 --self-contained true -o WinNUT.Avalonia/publish/win-x64
iscc WinNUT.Avalonia/installer/winnut.iss
```

# Updates

WinNutCPlus has built-in update functionality. This process can be started automatically on startup or manually on demand, and you can choose whether you want to update to the stable or development version.

# Contributing

Issues and pull requests are welcome at [RikoDEV/WinNutCPlus](https://github.com/RikoDEV/WinNutCPlus).

## Acknowledgments

WinNutCPlus is built on:
- [Avalonia UI](https://avaloniaui.net/) — cross-platform .NET UI framework, under the [MIT license](https://opensource.org/licenses/MIT)
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM source generators, under the [MIT license](https://opensource.org/licenses/MIT)
- [Octokit](https://github.com/octokit/octokit.net) — GitHub API client used for update checks, under the [MIT license](https://opensource.org/licenses/MIT)
- [Microsoft.Toolkit.Uwp.Notifications](https://github.com/CommunityToolkit/WindowsCommunityToolkit) — Windows toast notifications, under the [MIT license](https://opensource.org/licenses/MIT)

Originally based on WinNUT-Client, with thanks to its original authors and contributors.

## License

WinNutCPlus is a NUT Windows client for monitoring a UPS hooked up to your favorite Linux server.

- Copyright (C) 2019-2021 Gawindx (Decaux Nicolas)
- Copyright (C) 2022-2024 NUT Dot Net project
- Copyright (C) 2026 RikoDEV

This program is free software: you can redistribute it and/or modify it under the terms of the
GNU General Public License as published by the Free Software Foundation, either version 3 of the
License, or any later version.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY.
