<?php
/**
 * phpMyAdmin configuration written by AMPLE.
 *
 * Auth type "config" signs in as root with the password AMPLE generated, so opening /phpmyadmin needs no login.
 * Change 'auth_type' to 'cookie' and remove 'password' if you want a login form instead.
 */

declare(strict_types=1);

$cfg['blowfish_secret'] = '${BLOWFISH_SECRET}';

$i = 0;
$i++;
$cfg['Servers'][$i]['host'] = '127.0.0.1';
$cfg['Servers'][$i]['port'] = '${MARIADB_PORT}';
$cfg['Servers'][$i]['auth_type'] = '${AUTH_TYPE}';
$cfg['Servers'][$i]['user'] = 'root';
$cfg['Servers'][$i]['password'] = '${ROOT_PASSWORD}';
$cfg['Servers'][$i]['compress'] = false;
$cfg['Servers'][$i]['AllowNoPassword'] = false;

$cfg['UploadDir'] = '';
$cfg['SaveDir'] = '';
$cfg['TempDir'] = '${AMPLE_ROOT}/tmp/phpmyadmin';
$cfg['CheckConfigurationPermissions'] = false;
$cfg['ShowPhpInfo'] = true;
$cfg['VersionCheck'] = false;
