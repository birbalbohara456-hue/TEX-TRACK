const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const appSource = fs.readFileSync(
  path.join(__dirname, '..', '..', 'Components', 'App.razor'),
  'utf8');

test('row navigation resolves the nearest internal scroll surface', () => {
  assert.match(appSource, /let container = row\.parentElement/);
  assert.match(appSource, /container\.scrollHeight > container\.clientHeight/);
  assert.match(appSource, /if \(!boundaryScrollable\) return/);
});

test('row reveal keeps selection below sticky headers and above sticky totals', () => {
  assert.match(appSource, /const stickyTop =/);
  assert.match(appSource, /const stickyBottom =/);
  assert.match(appSource, /const visibleTop = containerRect\.top \+ stickyTop/);
  assert.match(appSource, /const visibleBottom = containerRect\.bottom - stickyBottom/);
  assert.match(appSource, /rowRect\.top < visibleTop/);
  assert.match(appSource, /rowRect\.bottom > visibleBottom/);
  assert.match(appSource, /\[data-scroll-sticky-footer="true"\]/);
  assert.match(appSource, /Math\.max\(tableFooterHeight, overlayFooterHeight\)/);
});

test('report navigation keys cannot scroll the outer browser page', () => {
  assert.match(appSource, /\['ArrowDown', 'ArrowUp', 'PageDown', 'PageUp', 'Home', 'End'\]/);
  assert.match(appSource, /target\.closest\('\[data-keyboard-list-root="true"\]'\)/);
  assert.match(appSource, /event\.preventDefault\(\)/);
});

test('large report arrows update selection locally and clamp at list walls', () => {
  assert.match(appSource, /texTrackFastReportNavigationInstalled/);
  assert.match(appSource, /\[data-keyboard-list-root="true"\], \.page-card\[tabindex="0"\]/);
  assert.match(appSource, /Math\.min\(index \+ 1, rows\.length - 1\)/);
  assert.match(appSource, /Math\.max\(index - 1, 0\)/);
  assert.match(appSource, /if \(next === index\) \{\s*event\.preventDefault\(\);\s*return;/);
  assert.match(appSource, /\.selectable-grid tbody > tr\[id\]/);
  assert.match(appSource, /rows\[next\]\.focus\(\{ preventScroll: true \}\)/);
  assert.doesNotMatch(appSource, /\(index \+ 1\) % rows\.length/);
});

test('mouse hover and keyboard selection use different colours in voucher registers', () => {
  const css = fs.readFileSync(
    path.join(__dirname, '..', '..', 'Components', 'Pages', 'Reports', 'VoucherHistory.razor.css'),
    'utf8');

  assert.match(css, /\.voucher-history-grid tbody tr:hover:not\(\.selected\):not\(\.selected-row\)/);
  assert.match(css, /\.voucher-history-grid tbody tr\.selected,\s*\.voucher-history-grid tbody tr\.selected-row/);
  assert.doesNotMatch(css, /tbody tr:hover,\s*\.voucher-history-grid tbody tr\.selected/);
  // The totals bar sits at the true bottom of a flex column now (see
  // .voucher-history-grid/.voucher-history-scroll) rather than relying on
  // position:sticky, which only reaches the bottom once there's enough
  // content to actually scroll - it never did for a short list.
  assert.match(css, /\.voucher-history-totalbar \{[^}]*flex:\s*0 0 auto/);
});

test('menu mouse hover and keyboard selection use different visual states', () => {
  const css = fs.readFileSync(
    path.join(__dirname, '..', '..', 'wwwroot', 'css', 'app.css'),
    'utf8');

  assert.match(css, /\.desk-shell \.menu-link:hover:not\(\.keyboard-selected\)/);
  assert.match(css, /\.desk-shell \.menu-link\.keyboard-selected/);
  assert.match(css, /\.menu-link:hover:not\(\.keyboard-selected\)/);
  assert.match(css, /\.master-menu-group-button:hover:not\(\.keyboard-selected\)/);
  assert.doesNotMatch(css, /\.menu-link:hover,\s*\r?\n\.menu-link:focus,\s*\r?\n\.menu-link\.keyboard-selected/);
});
