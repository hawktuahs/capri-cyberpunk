(() => {
  'use strict';
  const repository = 'hawktuahs/capri-cyberpunk';
  const releaseURL = `https://github.com/${repository}/releases/latest`;
  const setText = (selector, value) => document.querySelectorAll(selector).forEach(el => { el.textContent = value; });
  async function loadRelease() {
    let release;
    try {
      const response = await fetch(`https://api.github.com/repos/${repository}/releases/latest`, { signal: AbortSignal.timeout(8000) });
      if (!response.ok) throw new Error('Release API unavailable');
      release = await response.json();
    } catch {
      try {
        const response = await fetch('release.json');
        if (!response.ok) throw new Error('No release snapshot');
        release = await response.json();
      } catch { setText('#download-meta', 'Release information unavailable. Open GitHub Releases for downloads.'); return; }
    }
    if (release.draft || release.prerelease || !Array.isArray(release.assets)) return;
    const asset = release.assets.find(a => /^Capri-Desktop-.*Windows-x64\.zip$/i.test(a.name)) || release.assets.find(a => /^Capri-Desktop-.*\.zip$/i.test(a.name));
    const validURL = url => { try { const u = new URL(url); return u.protocol === 'https:' && u.hostname === 'github.com' && u.pathname.startsWith(`/${repository}/releases/`); } catch { return false; } };
    setText('[data-version]', release.tag_name || '');
    if (asset && validURL(asset.browser_download_url)) {
      document.querySelectorAll('[data-download]').forEach(a => { a.href = asset.browser_download_url; a.textContent = 'Download for Windows ↓'; });
      setText('#download-description', /Windows-x64\.zip$/i.test(asset.name) ? 'Full package · Windows app + speech runtime + Kokoro TTS + Vosk English STT. No Python or CUDA installation needed.' : 'Legacy package · Voice dependencies require separate setup. See the release notes before installing.');
      setText('#download-meta', `${asset.name} / ${(asset.size / 1024 / 1024).toFixed(1)} MB`);
    } else { setText('#download-meta', 'No Windows ZIP attached yet. Check the release page.'); }
    setText('#release-title', release.name || release.tag_name || 'Latest release');
    if (release.published_at) setText('#release-date', `RELEASED ${new Date(release.published_at).toLocaleDateString('en', {year:'numeric',month:'short',day:'numeric'})}`);
    const summary = (release.body || '').replace(/!\[[^\]]*\]\([^)]*\)/g, '').replace(/[#*`]/g, '').trim();
    if (summary) setText('#release-summary', summary.length > 360 ? `${summary.slice(0, 357)}…` : summary);
    const link = document.querySelector('#release-link');
    if (link) link.href = validURL(release.html_url) ? release.html_url : releaseURL;
  }
  if (document.querySelector('[data-download]')) loadRelease();
  const tabs = [...document.querySelectorAll('[role=tab]')];
  function selectTab(tab, focus = false) {
    tabs.forEach(button => {
      const active = button === tab;
      button.setAttribute('aria-selected', String(active));
      button.tabIndex = active ? 0 : -1;
      document.getElementById(button.getAttribute('aria-controls')).hidden = !active;
    });
    if (focus) tab.focus();
  }
  tabs.forEach((tab, index) => {
    tab.addEventListener('click', () => selectTab(tab));
    tab.addEventListener('keydown', event => {
      let next;
      if (event.key === 'ArrowRight') next = (index + 1) % tabs.length;
      if (event.key === 'ArrowLeft') next = (index - 1 + tabs.length) % tabs.length;
      if (event.key === 'Home') next = 0;
      if (event.key === 'End') next = tabs.length - 1;
      if (next !== undefined) { event.preventDefault(); selectTab(tabs[next], true); }
    });
  });
  document.querySelectorAll('[data-copy]').forEach(button => button.addEventListener('click', async () => {
    try { await navigator.clipboard.writeText(button.dataset.copy); button.textContent = 'Copied'; }
    catch { button.textContent = 'Select command to copy'; }
    setTimeout(() => { button.textContent = 'Copy'; }, 2500);
  }));
})();
