/* ModernSidePanel client — restores persistent legacy side panel, hides top bar, keeps modern styling.
 * Loaded via index.html patch (defer). Idempotent, SPA-aware, version-agnostic (10.9 -> 12.x).
 */
(function () {
  'use strict';

  var DEFAULTS = {
    enablePlugin: true,
    hideTopBar: true,
    persistentSidebar: true,
    sidebarWidth: 272,
    showLogo: true,
    relocateHeaderButtons: true,
    mobileBreakpoint: 768,
    blurStrength: 18
  };

  function getConfig() {
    var c = {};
    try { c = window.ModernSidePanelConfig || {}; } catch (e) { c = {}; }
    var out = {};
    for (var k in DEFAULTS) {
      out[k] = (c[k] === undefined || c[k] === null) ? DEFAULTS[k] : c[k];
    }
    out.sidebarWidth = Math.max(200, Math.min(400, parseInt(out.sidebarWidth, 10) || DEFAULTS.sidebarWidth));
    out.mobileBreakpoint = parseInt(out.mobileBreakpoint, 10) || DEFAULTS.mobileBreakpoint;
    out.blurStrength = Math.max(0, Math.min(30, parseInt(out.blurStrength, 10) || 0));
    return out;
  }

  var cfg = getConfig();
  if (cfg.enablePlugin === false) {
    document.documentElement.classList.remove('msp-sidebar', 'msp-hide-topbar');
    return;
  }

  var root = document.documentElement;
  var movedNodes = []; // {node, parent, next}
  var booted = false;

  function applyCssVars() {
    root.style.setProperty('--msp-width', cfg.sidebarWidth + 'px');
    root.style.setProperty('--msp-blur', cfg.blurStrength + 'px');
  }

  function isMobile() {
    return window.matchMedia('(max-width: ' + cfg.mobileBreakpoint + 'px)').matches;
  }

  function isFullscreenVideo() {
    try {
      var fs = document.fullscreenElement || document.webkitFullscreenElement || null;
      if (fs) {
        try {
          if (fs.classList && (fs.classList.contains('videoPlayerContainer') || fs.classList.contains('videoOsdBottom') || fs.classList.contains('osdParent'))) return true;
          if (fs.querySelector && fs.querySelector('.videoOsdBottom')) return true;
        } catch (e) { return true; }
        return false;
      }
      try {
        return !!document.querySelector('.videoPlayerContainer:fullscreen');
      } catch (e) { return false; }
    } catch (e) { return !!document.fullscreenElement; }
  }

  function applyModeClasses() {
    applyCssVars();
    var mobile = isMobile();
    root.classList.toggle('msp-mobile', mobile);
    root.classList.toggle('msp-sidebar', !!cfg.persistentSidebar);
    root.classList.toggle('msp-hide-topbar', !!cfg.hideTopBar && !mobile);
    root.classList.toggle('fullscreenVideo', isFullscreenVideo());
  }

  function ensureLogo(drawer) {
    if (!cfg.showLogo) return;
    var content = drawer.querySelector('.navDrawerContent') || drawer;
    if (content.querySelector(':scope > .msp-logo')) return;
    var logo = document.createElement('div');
    logo.className = 'msp-logo';
    logo.innerHTML = '<span class="msp-logo-badge">\u25B6</span><span>Jellyfin<small>Modern + Classic Panel</small></span>';
    content.insertBefore(logo, content.firstChild);
  }

  function ensureActionsContainer(drawer) {
    var content = drawer.querySelector('.navDrawerContent') || drawer;
    var box = content.querySelector(':scope > .msp-actions');
    if (!box) {
      box = document.createElement('div');
      box.className = 'msp-actions';
      box.setAttribute('data-msp', 'header-actions');
      content.appendChild(box);
    }
    return box;
  }

  function relocateHeaderButtons(drawer) {
    if (!cfg.relocateHeaderButtons) return;
    if (isMobile()) { restoreHeaderButtons(); return; }
    var box = ensureActionsContainer(drawer);
    if (!box) return;
    // GC orphaned nodes from dead headers (SPA re-render destroys .skinHeader/MuiAppBar).
    movedNodes = movedNodes.filter(function (btn) {
      if (!btn.isConnected) return false;
      var home = btn.__mspHome;
      if (home && home.parent && !home.parent.isConnected) {
        btn.__mspHome = { parent: null, next: null };
      }
      return true;
    });
    if (box.children.length > 8) return;
    // Legacy header buttons + modern v12 MUI icon buttons (exact labels only).
    var selectors = [
      '.skinHeader .headerSearchButton', '.skinHeader-withBackground .headerSearchButton',
      '.skinHeader .headerCastButton', '.skinHeader .headerSyncButton',
      '.skinHeader .headerAudioPlayerButton', '.skinHeader .headerUserButton',
      'header.MuiAppBar-root button[aria-label="Search"]',
      'header.MuiAppBar-root button[aria-label="search"]',
      'header.MuiAppBar-root button[aria-label="Account"]',
      'header.MuiAppBar-root button[aria-label="User"]',
      'header.MuiAppBar-root button[aria-label="Cast"]'
    ];
    selectors.forEach(function (sel) {
      var btns = document.querySelectorAll(sel);
      btns.forEach(function (btn) {
        if (btn && btn.parentElement !== box && box) {
          if (!btn.__mspHome) {
            btn.__mspHome = { parent: btn.parentElement, next: btn.nextElementSibling };
          }
          try { box.appendChild(btn); } catch (e) { /* noop */ }
          if (movedNodes.indexOf(btn) === -1) { movedNodes.push(btn); }
        }
      });
    });
  }

  function restoreHeaderButtons() {
    movedNodes.forEach(function (btn) {
      try {
        var home = btn.__mspHome;
        if (home && home.parent && home.parent.isConnected) {
          if (home.next && home.next.isConnected && home.next.parentElement === home.parent) {
            home.parent.insertBefore(btn, home.next);
          } else {
            home.parent.appendChild(btn);
          }
        }
      } catch (e) { /* noop */ }
    });
    movedNodes = [];
  }

  function highlightActiveLink(scope) {
    try {
      var hash = location.hash || '#/home.html';
      var links = (scope || document).querySelectorAll('.navDrawerMenuOption, #msp-sidepanel .msp-link');
      links.forEach(function (a) {
        var href = a.getAttribute('href') || '';
        var match = href && hash.indexOf(href.replace('#', '')) !== -1 && href !== '#';
        // Fallback: compare data-item / text for home
        if (!match && href === '#/home.html' && (hash === '#/' || hash === '' || hash.indexOf('home') !== -1)) match = true;
        a.classList.toggle('selected', !!match);
        a.classList.toggle('active', !!match);
      });
    } catch (e) { /* noop */ }
  }

  function forceDrawerOpen() {
    var drawer = document.querySelector('.mainDrawer');
    if (drawer) {
      root.classList.remove('msp-use-custom');
      drawer.removeAttribute('hidden');
      drawer.setAttribute('aria-hidden', 'false');
      // Neutralize inline transforms applied by the stock navdrawer logic on desktop
      if (!isMobile() && cfg.persistentSidebar) {
        drawer.style.transform = 'none';
        drawer.classList.add('drawer-open');
        document.body.classList.add('drawer-open');
      } else {
        drawer.style.transform = '';
      }
      ensureLogo(drawer);
      relocateHeaderButtons(drawer);
      highlightActiveLink(drawer);
      return true;
    }
    return false;
  }

  /* Fallback: v12 removed .mainDrawer entirely — build our own sidebar from scratch.
   * Tries ApiClient.getUserViews() for real libraries, falls back to static routes. */
  var customLibsLoaded = false;
  function iconForCollection(type) {
    var map = { movies: 'movie', tvshows: 'tv', music: 'music_note', livetv: 'live_tv', books: 'book', photos: 'photo_library', folders: 'folder', boxsets: 'collections_bookmark', playlists: 'queue_music', channels: 'live_tv' };
    return map[type] || 'folder_open';
  }
  function refreshCustomLibraries() {
    if (customLibsLoaded) return;
    try {
      if (!window.ApiClient || !window.Dashboard || !ApiClient.getUserViews) return;
      var userId = null;
      try { userId = Dashboard.getCurrentUserId(); } catch (e) { userId = null; }
      if (!userId) return;
      var p = null;
      try {
        p = ApiClient.getUserViews.length >= 2 ? ApiClient.getUserViews({}, userId) : ApiClient.getUserViews(userId);
      } catch (e) { p = null; }
      if (!p || !p.then) return;
      p.then(function (res) {
        var nav = document.getElementById('msp-sidepanel');
        if (!nav || !res || !res.Items) return;
        var libBox = nav.querySelector('[data-msp="libraries"]');
        if (!libBox) return;
        libBox.innerHTML = '';
        res.Items.forEach(function (v) {
          var a = document.createElement('a');
          a.className = 'msp-link';
          a.href = '#/details?id=' + encodeURIComponent(v.Id);
          a.dataset.viewId = v.Id;
          var ic = document.createElement('span');
          ic.className = 'material-icons';
          ic.textContent = iconForCollection(v.CollectionType);
          a.appendChild(ic);
          a.appendChild(document.createTextNode(v.Name || 'Untitled'));
          libBox.appendChild(a);
        });
        customLibsLoaded = true;
        highlightActiveLink(nav);
      }).catch(function () { /* keep static fallback */ });
    } catch (e) { /* keep static fallback */ }
  }
  function buildCustomSidebar() {
    if (document.getElementById('msp-sidepanel')) {
      root.classList.add('msp-use-custom');
      refreshCustomLibraries();
      return;
    }
    var nav = document.createElement('nav');
    nav.id = 'msp-sidepanel';
    nav.setAttribute('aria-label', 'Primary');
    var links = [
      { href: '#/home.html', icon: 'home', label: 'Home' },
      { href: '#/favorites.html', icon: 'favorite', label: 'Favorites' },
      { href: '#/movies.html', icon: 'movie', label: 'Movies' },
      { href: '#/tv.html', icon: 'tv', label: 'TV Shows' },
      { href: '#/music.html', icon: 'music_note', label: 'Music' },
      { href: '#/livetv.html', icon: 'live_tv', label: 'Live TV' },
      { href: '#/search.html', icon: 'search', label: 'Search' },
      { href: '#/settings.html', icon: 'settings', label: 'Settings' },
      { href: '#/dashboard.html', icon: 'dashboard', label: 'Dashboard' }
    ];
    var html = '';
    if (cfg.showLogo) {
      html += '<div class="msp-logo"><span class="msp-logo-badge">\u25B6</span><span>Jellyfin<small>Modern + Classic Panel</small></span></div>';
    }
    html += '<div class="msp-nav"><div class="msp-section">Library</div><div data-msp="libraries">';
    links.slice(0, 6).forEach(function (l) {
      html += '<a class="msp-link" href="' + l.href + '"><span class="material-icons">' + l.icon + '</span>' + l.label + '</a>';
    });
    html += '</div><div class="msp-section">System</div>';
    links.slice(6).forEach(function (l) {
      html += '<a class="msp-link" href="' + l.href + '"><span class="material-icons">' + l.icon + '</span>' + l.label + '</a>';
    });
    html += '<div class="msp-actions" data-msp="header-actions"></div></div>';
    nav.innerHTML = html;
    document.body.appendChild(nav);
    root.classList.add('msp-use-custom');
    highlightActiveLink(nav);
    refreshCustomLibraries();
  }

  /* Tag modern v12 MUI header so CSS can hide it even when class names hash-change. */
  function tagModernChrome() {
    try {
      var headers = document.querySelectorAll('header.MuiAppBar-root, div[data-testid="app-header"]');
      headers.forEach(function (h) { h.classList.add('msp-modern-header'); h.setAttribute('data-msp', 'app-header'); });
    } catch (e) { /* noop */ }
  }

  function canBuildCustom() {
    try { return !!(window.Dashboard && Dashboard.getCurrentUserId && Dashboard.getCurrentUserId()); }
    catch (e) { return false; }
  }

  function tick() {
    if (tick._running) return;
    tick._running = true;
    try {
      applyModeClasses();
      tagModernChrome();
      // Modern MUI drawer behaves like legacy drawer — force open on desktop.
      var muiDrawer = document.querySelector('.MuiDrawer-root, div[class*="MuiDrawer-paper"]');
      if (muiDrawer && !isMobile() && cfg.persistentSidebar) {
        muiDrawer.style.transform = 'none';
        muiDrawer.style.visibility = 'visible';
      }
      var found = forceDrawerOpen();
      // Prefer stock drawer when present; only build custom when NEITHER exists + logged in.
      if ((!found && !muiDrawer) && !isMobile() && canBuildCustom()) {
        buildCustomSidebar();
        var custom = document.getElementById('msp-sidepanel');
        if (custom) { relocateHeaderButtons(custom); highlightActiveLink(custom); }
      }
    } finally {
      tick._running = false;
    }
  }

  function boot() {
    if (booted) return;
    booted = true;
    applyModeClasses();
    tick();

    // SPA re-renders header/drawer on every navigation — re-apply cheaply.
    // Narrow scope: childList on body only (no attribute watching, tick() itself mutates class/style).
    var debounce = null;
    var obs = new MutationObserver(function (muts) {
      var relevant = false;
      for (var i = 0; i < muts.length; i++) {
        if (muts[i].type === 'childList') { relevant = true; break; }
      }
      if (!relevant) return;
      if (tick._running) return;
      if (debounce) clearTimeout(debounce);
      debounce = setTimeout(tick, 60);
    });
    try {
      obs.observe(document.body, { childList: true, subtree: true });
    } catch (e) {
      obs.observe(document.documentElement, { childList: true, subtree: true });
    }

    window.addEventListener('hashchange', function () { setTimeout(tick, 80); });
    window.addEventListener('resize', function () { applyModeClasses(); tick(); });
    document.addEventListener('fullscreenchange', function () { applyModeClasses(); tick(); });

    // Late retries for slow SPA boot
    [500, 1500, 3000].forEach(function (d) { setTimeout(tick, d); });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot);
  } else {
    boot();
  }
})();
