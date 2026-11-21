/*
 * Site language (zh or en), decided before the page is painted so it never flashes the wrong
 * language. Order: ?lang= in the address (the plug-in passes its own UI language), then the
 * visitor's earlier choice, then the system/browser languages; English when none is Chinese or
 * English. The choice is kept per browser. Elements marked .l-zh / .l-en are shown for their
 * language only (see site.css); any element with data-set-lang switches the language.
 */
(function () {
  'use strict';

  var STORAGE_KEY = 'cm-lang';

  function fromTag(tag) {
    var value = (tag || '').toLowerCase();
    if (value.indexOf('zh') === 0) {
      return 'zh';
    }
    return value.indexOf('en') === 0 ? 'en' : null;
  }

  function stored() {
    try {
      return fromTag(window.localStorage.getItem(STORAGE_KEY));
    } catch (e) {
      return null;
    }
  }

  function remember(lang) {
    try {
      window.localStorage.setItem(STORAGE_KEY, lang);
    } catch (e) {
      // Private windows may refuse storage; the language still applies to this page.
    }
  }

  function detect() {
    var asked = fromTag(new URLSearchParams(window.location.search).get('lang'));
    if (asked) {
      remember(asked);
      return asked;
    }
    var saved = stored();
    if (saved) {
      return saved;
    }
    var preferred = navigator.languages && navigator.languages.length ? navigator.languages : [navigator.language];
    for (var i = 0; i < preferred.length; i++) {
      var lang = fromTag(preferred[i]);
      if (lang) {
        return lang;
      }
    }
    return 'en';
  }

  function apply(lang) {
    var root = document.documentElement;
    root.setAttribute('data-lang', lang);
    root.lang = lang === 'zh' ? 'zh-CN' : 'en';
    // Pages without their own title logic keep one title per language on <html>.
    var title = root.getAttribute('data-title-' + lang);
    if (title) {
      document.title = title;
    }
  }

  var current = detect();
  apply(current);

  window.cmLang = {
    get: function () {
      return current;
    },
    set: function (lang) {
      if (lang !== 'zh' && lang !== 'en') {
        return;
      }
      current = lang;
      remember(lang);
      apply(lang);
      document.dispatchEvent(new CustomEvent('cm-lang-change', { detail: lang }));
    }
  };

  document.addEventListener('click', function (event) {
    var target = event.target.closest ? event.target.closest('[data-set-lang]') : null;
    if (target) {
      event.preventDefault();
      window.cmLang.set(target.getAttribute('data-set-lang'));
    }
  });
}());
