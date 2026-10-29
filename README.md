<p align="center">
  <img src="docs/logo-256.png" alt="AMPLE" width="96">
</p>

<h1 align="center">AMPLE</h1>

<p align="center"><b>A</b>pache · <b>M</b>ariaDB · <b>P</b>HP · <b>L</b>edger · <b>E</b>nvironment<br>
A local web stack for Windows that is easy to use and stays out of your way.</p>

---

AMPLE is one `AMPLE.exe`. Put it in a folder, run it, and it installs Apache HTTP Server, MariaDB, PHP (with
per-project versions) and [Ledger](https://github.com/ClearanceClarence-Software/Ledger), a modern database tool,
into that folder. phpMyAdmin is one click away if you'd rather have it. Everything lives in the one folder; move it
or delete it and nothing else on your computer changes.

> **Status:** 0.1.0 is being built. The code is here; the first release will follow once the manifest has been
> verified against real downloads and the setup has been run on a clean Windows machine.

## What you get

- **Overview** that says in one sentence whether your stack is running, with a single Start all button.
- **Fix-it buttons** instead of log lines: when Skype or IIS sits on port 80, the card offers "Switch to port 8080".
- **Projects** with their own `.test` addresses, trusted HTTPS, their own PHP version and, if wanted, their own `php.ini`.
- **Versions side by side.** Install several PHP and MariaDB versions from the Versions page; PHP versions run at
  the same time, MariaDB versions each keep their own data and one is active.
- **phpinfo in AMPLE's own skin**, one page per installed PHP version, with a filter box.
- **Ledger or phpMyAdmin**, switchable at any time. Both open already signed in with the generated root password.
- **Verified downloads.** Every component is pinned to a version and a SHA-256 checksum in
  [`manifest/manifest.json`](manifest/manifest.json). The offline installer ships the same files.
- **A folder you can move.** No install path is written into Apache's config; MariaDB's and PHP's are re-pointed
  when the folder moves.

## How it is put together

```
C:\ample\
  AMPLE.exe             the app
  bin\apache\           Apache HTTP Server (Apache Lounge build), mod_fcgid in modules\
  bin\php\8.4.27\       one folder per PHP version, NTS x64, each with its own php.ini
  bin\mariadb\11.8.9\   one folder per MariaDB version; one is active at a time
  apps\ledger\          Ledger, preconfigured
  apps\phpmyadmin\      phpMyAdmin, when installed
  etc\apache\           httpd.conf (yours) and the generated ports, php, apps, ssl and projects files
  etc\mariadb\my.ini    points at the active MariaDB version
  etc\php\              the projects' custom php.ini files
  etc\ample\stack.json  what is installed, the default PHP, the active MariaDB, the generated root password
  data\mariadb\11.8.9\  each MariaDB version keeps its own data
  logs\                 apache\, mariadb\, php\
  projects\             the default home for new projects
  www\                  what http://localhost serves; www\_ample\ holds AMPLE's phpinfo pages and stack.json
  tmp\                  downloads and staging
  bundle\               components shipped with the offline installer (optional)
```

PHP runs through **mod_fcgid**, not mod_php. That is what makes per-project PHP versions possible: each virtual
host points at its own `php-cgi.exe`. It also means a PHP build's Visual Studio version doesn't have to match
Apache's.

Apache is started as `httpd -d <root>/bin/apache -f <root>/etc/apache/httpd.conf -C "Define AMPLE_ROOT <root>"`,
so `httpd.conf` uses `${AMPLE_ROOT}` everywhere and never contains the install path.

## Building

Requires the .NET 10 SDK. JetBrains Rider or Visual Studio open `AMPLE.sln`.

```
dotnet build AMPLE.sln
dotnet run --project src/Ample.App -- --root C:\ample
```

AMPLE manages the folder its exe sits in. From the IDE that would be `bin\Debug\...`, so pass `--root` with the folder
you want the stack in; it is created if missing and the first-run setup installs into it. A published `AMPLE.exe`
needs no switch: drop it in `C:\ample` (or wherever) and run it.

To get that single exe: `dotnet publish src/Ample.App -p:PublishProfile=win-x64`, which writes
`bin\publish\win-x64\AMPLE.exe`.

Code style is in `.editorconfig` and [CODING_STANDARDS.md](CODING_STANDARDS.md); `dotnet format AMPLE.sln`
applies the formatting rules, and CI fails on anything it would change.

### The manifest

`manifest/manifest.json` lists every component with its download URL and checksum. Keep it current with the
maintainer tool, which reads the publishers' feeds (windows.php.net, the MariaDB REST API, Apache Lounge,
GitHub, phpmyadmin.net), downloads each file and records its SHA-256:

```
dotnet run --project tools/Ample.Manifest -- refresh    # newest builds in each series, fresh checksums
dotnet run --project tools/Ample.Manifest -- verify     # download everything and compare
dotnet run --project tools/Ample.Manifest -- bundle DIR # download everything into DIR for the offline zip
```

Commit the refreshed manifest; running AMPLEs read it from this repository's `main` branch at install time.

### Releasing

`tools/release.ps1` publishes the single-file exe, signs it (`-Sign`), builds the offline bundle (`-Bundle`) and
uploads a draft release (`-Upload`). CI only checks that pushes build.

## Project layout

| Folder | What |
|:--|:--|
| `src/Ample.Core` | Everything that knows the stack: paths, settings, the manifest and installer, config writing, the Apache and MariaDB drivers, projects, certificates. No UI. |
| `src/Ample.App` | The Avalonia app: shell, Overview, Projects, Logs, Settings, first-run Setup, tray icon. |
| `tools/Ample.Manifest` | The manifest maintainer tool. |
| `manifest/` | The published component list. |
| `LICENSES/` | Third-party license notes. |

## Licenses

AMPLE is MIT-licensed. The components it installs have their own licenses; see [LICENSES](LICENSES/README.md).
AMPLE is not affiliated with Apache Friends (XAMPP) or the Apache Software Foundation.

## About

AMPLE is by [ClearanceClarence](https://github.com/ClearanceClarence) under the CC Software label, a sibling of
[SpaceSharp](https://github.com/ClearanceClarence/SpaceSharp), Routemark and the
[Modern XAMPP Control Panel](https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel), whose
drivers and project code AMPLE grew out of.
