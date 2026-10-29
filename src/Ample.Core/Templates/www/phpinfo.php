<?php
// AMPLE · phpinfo() in AMPLE's own skin. Written by AMPLE for each installed PHP version; rewritten on repair.
// Only answers to this computer, like the database tools.
$remote = $_SERVER['REMOTE_ADDR'] ?? '';
if (!in_array($remote, ['127.0.0.1', '::1'], true)) {
    http_response_code(403);
    exit('phpinfo is only shown to this computer.');
}

ob_start();
phpinfo();
$raw = (string) ob_get_clean();

// Keep phpinfo's body (the tables), drop its own styles and page chrome.
$body = preg_match('~<body[^>]*>(.*)</body>~si', $raw, $m) ? $m[1] : $raw;
$body = preg_replace('~<style[^>]*>.*?</style>~si', '', $body);
$body = preg_replace('~<img[^>]*>~si', '', $body);                       // the PHP logo
$body = preg_replace('~<a href="http://www\.php\.net/">.*?</a>~si', '', $body);
$body = preg_replace('~<div class="center">~', '<div class="info">', $body, 1);
$body = str_replace(['<h1 class="p">', 'class="e"', 'class="v"', 'class="h"'], ['<h1 hidden>', 'class="k"', 'class="val"', 'class="hd"'], $body);

$version = PHP_VERSION;
$sapi = PHP_SAPI;
$ini = php_ini_loaded_file() ?: 'none';
$extCount = count(get_loaded_extensions());
function e(string $s): string { return htmlspecialchars($s, ENT_QUOTES, 'UTF-8'); }
?>
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>PHP <?= e($version) ?> · phpinfo · AMPLE</title>
<style>
  :root { color-scheme: dark; --page:#0f1115; --card:#181b22; --card2:#1f232c; --line:#232733; --text:#e7e9ee; --soft:#c3c9d4; --muted:#9aa3b2; --accent:#93c5fd; --mono: ui-monospace,"Cascadia Mono","JetBrains Mono",Consolas,monospace; }
  @media (prefers-color-scheme: light) { :root { color-scheme: light; --page:#f4f5f7; --card:#fff; --card2:#f1f3f6; --line:#e0e3e8; --text:#141821; --soft:#3b4048; --muted:#5b6472; --accent:#1d4ed8; } }
  * { box-sizing: border-box; }
  body { margin: 0; background: var(--page); color: var(--text); font: 14px/1.5 Inter, system-ui, -apple-system, "Segoe UI", sans-serif; }
  a { color: var(--accent); text-decoration: none; }
  .top { position: sticky; top: 0; z-index: 2; background: var(--page); border-bottom: 1px solid var(--line); }
  .top .in { max-width: 1180px; margin: 0 auto; padding: 14px 24px; display: flex; flex-wrap: wrap; align-items: center; gap: 12px 20px; }
  .top h1 { margin: 0; font-size: 18px; } .top h1 span { color: var(--accent); }
  .top .meta { color: var(--muted); font-family: var(--mono); font-size: 12px; display: flex; flex-wrap: wrap; gap: 14px; }
  .top input { margin-left: auto; min-width: 240px; padding: 8px 12px; border-radius: 10px; border: 1px solid var(--line); background: var(--card); color: var(--text); font: inherit; }
  .wrap { max-width: 1180px; margin: 0 auto; padding: 20px 24px 60px; }
  .info { width: 100%; }
  .info > h2 { margin: 28px 0 10px; font-size: 15px; letter-spacing: .04em; text-transform: uppercase; color: var(--muted); }
  .info > h2 a { color: inherit; }
  .info table { width: 100%; border-collapse: separate; border-spacing: 0; background: var(--card); border: 1px solid var(--line); border-radius: 12px; overflow: hidden; margin-bottom: 14px; }
  .info td, .info th { padding: 8px 14px; border-top: 1px solid var(--line); vertical-align: top; text-align: left; font-size: 13px; word-break: break-word; }
  .info tr:first-child td, .info tr:first-child th { border-top: 0; }
  .info th { background: var(--card2); color: var(--muted); font-weight: 600; }
  .info td.k { width: 32%; color: var(--accent); font-family: var(--mono); font-size: 12.5px; }
  .info td.val { color: var(--soft); font-family: var(--mono); font-size: 12.5px; }
  .info td.hd, .info th.hd { background: var(--card2); color: var(--text); font-weight: 600; }
  .info tr.hide { display: none; }
  .info hr { display: none; }
  .back { color: var(--muted); font-size: 13px; }
</style>
</head>
<body>
<div class="top"><div class="in">
  <a class="back" href="/">← localhost</a>
  <h1>PHP <span><?= e($version) ?></span> · phpinfo</h1>
  <div class="meta"><span><?= e($sapi) ?></span><span><?= $extCount ?> extensions</span><span><?= e($ini) ?></span></div>
  <input type="search" id="q" placeholder="Filter settings…" autocomplete="off">
</div></div>
<div class="wrap">
  <?= $body ?>
</div>
<script>
  // Client-side filter over every table row; section headings stay so you keep your bearings.
  const q = document.getElementById('q');
  const rows = Array.from(document.querySelectorAll('.info table tr'));
  q.addEventListener('input', () => {
    const term = q.value.trim().toLowerCase();
    rows.forEach(r => r.classList.toggle('hide', term !== '' && !r.textContent.toLowerCase().includes(term)));
  });
</script>
</body>
</html>
