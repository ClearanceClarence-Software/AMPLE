# Changelog

All notable changes to AMPLE. The release script copies the section for each version into its GitHub release.

## [0.1.0] - Unreleased

### Added

- **First-run setup.** AMPLE.exe installs into the folder it runs from: Apache HTTP Server, mod_fcgid, MariaDB,
  Ledger, the PHP versions you pick, and phpMyAdmin if you want it. Components come from their official sites
  and are verified against the checksums in `manifest/manifest.json`; the offline zip carries them in a
  `bundle\` folder and installs without a connection.
- **Overview.** One sentence about the stack, a Start all / Stop all button, a card per service with Config and
  Logs menus, and a fix-it button when another program sits on a port.
- **Database tool switch.** Ledger ships with AMPLE and opens signed in; phpMyAdmin can be installed from
  Settings and chosen instead at any time.
- **Projects.** Each project gets its own `.test` address, HTTPS with a certificate authority that exists only
  on your computer, and its own PHP version through mod_fcgid. Apply writes the virtual hosts, tests the config
  with `httpd -t` (and undoes it if Apache rejects it), updates the hosts file and restarts the web server.
- **Settings.** Database tool, ports (with Find free ports), default PHP version, more PHP versions from the
  manifest, the generated root password, startup options, theme, editor and browser, Repair.
- **Versions page.** Every PHP and MariaDB version from the manifest, installed side by side with Install and
  Remove; PHP versions run concurrently, MariaDB versions each keep their own data folder and one is active at a
  time, switchable with one click.
- **Per-project php.ini.** A project can copy its version's php.ini into `etc\php\<host>.ini` and edit it;
  mod_fcgid hands it to that project's PHP through PHPRC.
- **phpinfo pages** per PHP version at `/_ample/php/<version>/`, styled like AMPLE, with a filter box; a button on
  the Overview opens the default version's.
- **localhost page** that reads `www\_ample\stack.json` and shows versions, tools, ports and the projects with
  links.
- **Tray icon** whose apex node shows the stack's state: green running, amber attention, gray stopped.
- **Relocatable folder.** Apache gets the install path on its command line, and my.ini and php.ini are
  re-pointed when the folder moves.
- `tools/Ample.Manifest`: refreshes the manifest from the publishers' feeds, verifies it, and builds the
  offline bundle.
