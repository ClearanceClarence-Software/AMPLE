<?php
// AMPLE · localhost. Written once by AMPLE; edit or replace it freely. Live data comes from _ample/stack.json,
// which AMPLE rewrites whenever versions, tools or projects change.
$stack = @json_decode((string) @file_get_contents(__DIR__ . '/_ample/stack.json'), true) ?: [];
$ports = $stack['ports'] ?? ['http' => 80, 'https' => 443, 'mariadb' => 3306];
$phpDefault = $stack['php']['default'] ?? PHP_VERSION;
$phpInstalled = $stack['php']['installed'] ?? [PHP_VERSION];
$mariaActive = $stack['mariadb']['active'] ?? null;
$mariaInstalled = $stack['mariadb']['installed'] ?? [];
$tools = $stack['tools'] ?? ['ledger' => true, 'phpmyadmin' => false, 'preferred' => 'ledger'];
$projects = $stack['projects'] ?? [];
$httpPort = (int) ($ports['http'] ?? 80);
$httpsPort = (int) ($ports['https'] ?? 443);
$extensions = ['mysqli', 'pdo_mysql', 'curl', 'mbstring', 'intl', 'gd', 'openssl', 'zip', 'sodium', 'opcache'];

// Is the database answering? A plain TCP connect is enough; no credentials needed.
$dbUp = false;
if (function_exists('fsockopen')) {
    $socket = @fsockopen('127.0.0.1', (int) ($ports['mariadb'] ?? 3306), $errno, $errstr, 0.3);
    if ($socket) { $dbUp = true; fclose($socket); }
}
function projectUrl(array $p, int $httpPort, int $httpsPort): string {
    if (!empty($p['https'])) { return 'https://' . $p['host'] . ($httpsPort === 443 ? '' : ':' . $httpsPort) . '/'; }
    return 'http://' . $p['host'] . ($httpPort === 80 ? '' : ':' . $httpPort) . '/';
}
function e(string $s): string { return htmlspecialchars($s, ENT_QUOTES, 'UTF-8'); }
?>
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>AMPLE · localhost</title>
<style>
  :root {
    color-scheme: dark;
    --page: #0f1115; --card: #181b22; --card2: #1f232c; --line: #232733; --line2: #2f3542;
    --text: #e7e9ee; --soft: #c3c9d4; --muted: #9aa3b2;
    --accent: #2563eb; --accent-text: #93c5fd; --ok: #4ade80; --ok-text: #86efac; --warn: #fbbf24; --stopped: #6b7280;
    --mono: ui-monospace, "Cascadia Mono", "JetBrains Mono", Consolas, monospace;
  }
  @media (prefers-color-scheme: light) {
    :root { color-scheme: light; --page: #f4f5f7; --card: #ffffff; --card2: #f1f3f6; --line: #e0e3e8; --line2: #c8cdd5;
            --text: #141821; --soft: #3b4048; --muted: #5b6472; --accent-text: #1d4ed8; --ok: #16a34a; --ok-text: #15803d; }
  }
  * { box-sizing: border-box; }
  body { margin: 0; background: var(--page); color: var(--text); font: 15px/1.5 Inter, system-ui, -apple-system, "Segoe UI", sans-serif; }
  a { color: var(--accent-text); text-decoration: none; } a:hover { text-decoration: underline; }
  .wrap { max-width: 1080px; margin: 0 auto; padding: 40px 24px 64px; }
  header { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 16px; margin-bottom: 28px; }
  .brand { display: flex; align-items: center; gap: 14px; }
  .brand h1 { margin: 0; font-size: 26px; letter-spacing: .03em; }
  .brand h1 b { color: var(--accent-text); font-weight: inherit; }
  .brand small { display: block; color: var(--muted); font-size: 13px; letter-spacing: 0; }
  .pill { display: inline-flex; align-items: center; gap: 8px; padding: 5px 12px; border-radius: 999px; font-size: 13px; font-weight: 600; background: var(--card2); color: var(--muted); }
  .pill i { width: 8px; height: 8px; border-radius: 50%; background: var(--stopped); }
  .pill.on { background: rgba(74,222,128,.12); color: var(--ok-text); } .pill.on i { background: var(--ok); }
  .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 16px; }
  .card { background: var(--card); border: 1px solid var(--line); border-radius: 16px; padding: 20px 22px; }
  .card h2 { margin: 0 0 4px; font-size: 16px; }
  .card p.sub { margin: 0 0 14px; color: var(--muted); font-size: 14px; }
  .row { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 10px 0; border-top: 1px solid var(--line); }
  .row:first-of-type { border-top: 0; }
  .mono { font-family: var(--mono); font-size: 13px; color: var(--soft); }
  .tag { font-family: var(--mono); font-size: 12px; padding: 2px 8px; border-radius: 6px; background: var(--card2); color: var(--muted); }
  .tag.on { color: var(--accent-text); }
  .btn { display: inline-flex; align-items: center; gap: 8px; min-height: 38px; padding: 0 14px; border-radius: 10px; border: 1px solid var(--line2); background: var(--card2); color: var(--text); font-weight: 600; font-size: 14px; }
  .btn:hover { text-decoration: none; border-color: var(--accent); }
  .btn.primary { background: var(--accent); border-color: var(--accent); color: #fff; }
  .ext { display: grid; grid-template-columns: repeat(2, 1fr); gap: 6px 14px; margin: 0; padding: 0; list-style: none; }
  .ext li { display: flex; align-items: center; gap: 8px; font-family: var(--mono); font-size: 13px; color: var(--soft); }
  .ext li::before { content: ""; width: 7px; height: 7px; border-radius: 50%; background: var(--ok); }
  .ext li.off { color: var(--muted); } .ext li.off::before { background: var(--stopped); }
  .empty { color: var(--muted); font-size: 14px; padding: 8px 0; }
  .span2 { grid-column: 1 / -1; }
  footer { margin-top: 28px; color: var(--muted); font-size: 13px; display: flex; flex-wrap: wrap; gap: 8px 20px; }
</style>
</head>
<body>
<div class="wrap">
  <header>
    <div class="brand">
      <svg width="48" height="48" viewBox="0 0 36 36" aria-hidden="true"><path d="M18 2 L32 10 L32 26 L18 34 L4 26 L4 10 Z" fill="#1e40af"/><path d="M18 7 L27.5 12.5 L27.5 23.5 L18 29 L8.5 23.5 L8.5 12.5 Z" fill="#2a2f3a"/><path d="M12.6 24.6 L18 11.2 L23.4 24.6 M15 20 L21 20" fill="none" stroke="#93c5fd" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"/><circle cx="18" cy="11.2" r="1.9" fill="#4ade80"/><circle cx="12.6" cy="24.6" r="1.9" fill="#93c5fd"/><circle cx="23.4" cy="24.6" r="1.9" fill="#93c5fd"/></svg>
      <h1>AMP<b>L</b>E<small>localhost · <?= e((string) ($stack['root'] ?? __DIR__)) ?></small></h1>
    </div>
    <div style="display:flex; gap:10px; flex-wrap:wrap">
      <span class="pill on"><i></i>Web server running</span>
      <span class="pill <?= $dbUp ? 'on' : '' ?>"><i></i>Database <?= $dbUp ? 'running' : 'stopped' ?></span>
    </div>
  </header>

  <div class="grid">
    <section class="card">
      <h2>PHP</h2>
      <p class="sub">This page runs on PHP <?= e(PHP_VERSION) ?> (the default). Each project can use its own version.</p>
      <?php foreach ($phpInstalled as $v): ?>
        <div class="row">
          <span class="mono">PHP <?= e($v) ?> <?php if ($v === $phpDefault): ?><span class="tag on">default</span><?php endif; ?></span>
          <a class="btn" href="/_ample/php/<?= e($v) ?>/">phpinfo</a>
        </div>
      <?php endforeach; ?>
    </section>

    <section class="card">
      <h2>Databases</h2>
      <p class="sub">MariaDB <?= e((string) ($mariaActive ?? '?')) ?> on 127.0.0.1:<?= (int) ($ports['mariadb'] ?? 3306) ?>, user <span class="mono">root</span>.</p>
      <div class="row">
        <span>Ledger <?php if (($tools['preferred'] ?? '') === 'ledger'): ?><span class="tag on">preferred</span><?php endif; ?></span>
        <?php if (!empty($tools['ledger'])): ?><a class="btn <?= ($tools['preferred'] ?? '') === 'ledger' ? 'primary' : '' ?>" href="/ledger/">Open</a><?php else: ?><span class="tag">not installed</span><?php endif; ?>
      </div>
      <div class="row">
        <span>phpMyAdmin <?php if (($tools['preferred'] ?? '') === 'phpmyadmin'): ?><span class="tag on">preferred</span><?php endif; ?></span>
        <?php if (!empty($tools['phpmyadmin'])): ?><a class="btn <?= ($tools['preferred'] ?? '') === 'phpmyadmin' ? 'primary' : '' ?>" href="/phpmyadmin/">Open</a><?php else: ?><span class="tag">install from AMPLE › Settings</span><?php endif; ?>
      </div>
      <?php if (count($mariaInstalled) > 1): ?>
        <p class="sub" style="margin:12px 0 0">Installed: <?= e(implode(', ', $mariaInstalled)) ?>. Switch the active one in AMPLE › Versions.</p>
      <?php endif; ?>
    </section>

    <section class="card span2">
      <h2>Projects</h2>
      <p class="sub">Each project has its own address and PHP version. Manage them in AMPLE › Projects.</p>
      <?php if (!$projects): ?>
        <div class="empty">No projects yet. Add a folder in AMPLE and it shows up here with a <span class="mono">.test</span> address.</div>
      <?php else: foreach ($projects as $p): ?>
        <div class="row">
          <div>
            <div><strong><?= e((string) $p['name']) ?></strong> <span class="tag">PHP <?= e((string) ($p['php'] ?? $phpDefault)) ?></span></div>
            <div class="mono" style="font-size:12px"><?= e((string) $p['folder']) ?></div>
          </div>
          <a class="btn" href="<?= e(projectUrl($p, $httpPort, $httpsPort)) ?>"><?= e((string) $p['host']) ?> ↗</a>
        </div>
      <?php endforeach; endif; ?>
    </section>

    <section class="card span2">
      <h2>Extensions in this PHP</h2>
      <p class="sub">Everything a typical framework needs is on. Turn others on in the version's php.ini (AMPLE › Settings › Edit php.ini).</p>
      <ul class="ext">
        <?php foreach ($extensions as $ext): ?>
          <li class="<?= extension_loaded($ext) ? '' : 'off' ?>"><?= e($ext) ?></li>
        <?php endforeach; ?>
      </ul>
    </section>
  </div>

  <footer>
    <span>AMPLE <?= e((string) ($stack['ample'] ?? '')) ?></span>
    <span>HTTP <?= $httpPort ?> · HTTPS <?= $httpsPort ?></span>
    <span>Document root <span class="mono"><?= e(__DIR__) ?></span></span>
    <span>This page is <span class="mono">www/index.php</span>; replace it with your own.</span>
  </footer>
</div>
</body>
</html>
