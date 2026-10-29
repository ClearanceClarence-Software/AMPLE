# Third-party components

AMPLE itself is MIT-licensed (see `../LICENSE`). It installs these programs, each under its own license. The
installer downloads them unmodified from their publishers, and the offline bundle ships them unmodified; the
exact versions and checksums are in `../manifest/manifest.json`, and each installed stack records what it got in
`etc\ample\stack.json`.

| Component | License | Source | Notes |
|:--|:--|:--|:--|
| Apache HTTP Server | Apache-2.0 | https://www.apachelounge.com/ (Windows build) · https://httpd.apache.org/ | License and NOTICE are inside `bin\apache`. |
| mod_fcgid | Apache-2.0 | https://www.apachelounge.com/ · https://httpd.apache.org/mod_fcgid/ | |
| PHP | PHP-3.01 | https://windows.php.net/ | `license.txt` is inside each `bin\php\<version>`. |
| MariaDB | GPL-2.0-only | https://mariadb.org/ | `COPYING` is inside `bin\mariadb`. The corresponding source for each version is at https://downloads.mariadb.org/ and https://github.com/MariaDB/server; AMPLE redistributes the official binaries unchanged. |
| Ledger | MIT | https://github.com/ClearanceClarence-Software/Ledger | By the same author as AMPLE. |
| phpMyAdmin (optional) | GPL-2.0-or-later | https://www.phpmyadmin.net/ | `LICENSE` is inside `apps\phpmyadmin`. Source at https://github.com/phpmyadmin/phpmyadmin. |
| Microsoft Visual C++ Redistributable | Microsoft proprietary (redistributable) | https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist | Required by Apache and PHP; installed only when missing. |

AMPLE's own code depends on [Avalonia](https://avaloniaui.net/) (MIT) and the Inter font (SIL OFL 1.1), which are
compiled into `AMPLE.exe`.

"XAMPP" is a trademark of Apache Friends; AMPLE is not affiliated with Apache Friends or the Apache Software
Foundation.
