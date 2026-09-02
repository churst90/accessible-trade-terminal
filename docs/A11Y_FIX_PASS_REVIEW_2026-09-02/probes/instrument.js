window.flog = [];
(function () {
  const of = HTMLElement.prototype.focus;
  HTMLElement.prototype.focus = function () {
    const st = (new Error().stack || '').split('\n').slice(2, 4).map(l => l.trim().replace(/^at /, '').replace(/file:\/\/\S*\//, '')).join(' <- ');
    window.flog.push('focus(' + this.id + ') from: ' + st);
    return of.apply(this, arguments);
  };
  window.addEventListener('keydown', e => window.flog.push('keydown ' + e.key + ' prevented=' + e.defaultPrevented + ' active=' + document.activeElement.id), true);
  document.addEventListener('focusin', e => window.flog.push('focusin ' + e.target.id), true);
})();
window.setup = function (pos) {
  window.accessibleTrader.setModalOpen(true);
  document.getElementById('overlay').style.display = 'none';
  const ad = document.getElementById('ad'); ad.style.display = 'block'; ad.style.position = pos;
  document.getElementById('ad-cancel').focus();
  window.flog.push('--- keys start (position:' + pos + ') ---');
  return 'ok';
};
