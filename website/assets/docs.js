/*
 * Documentation reader. Most pages are Markdown files next to docs/index.html, rendered in the
 * browser so they stay plain .md in the repository and need no build step: <slug>.md in Chinese,
 * <slug>.en.md in English. The About and legal pages are HTML fragments built by legal/build.py
 * (the same ones pasted into new-api); they hold both languages and site.css shows the current
 * one. /docs/network loads the network page; /docs/ loads the first page of the list below.
 * The language comes from lang.js and the page re-renders when the visitor switches it.
 */
(function () {
  'use strict';

  // Pages in navigation order. The slug is the file name without ".md".
  var PAGES = [
    { slug: 'getting-started', title: { zh: '安装与开始使用', en: 'Install and get started' } },
    { slug: 'account', title: { zh: '账号、充值与余额', en: 'Account, top-up and balance' } },
    { slug: 'network', title: { zh: '无法连接网络', en: 'Cannot connect to the internet' } },
    { slug: 'about', title: { zh: '关于 Chameleon', en: 'About Chameleon' }, group: { zh: '关于', en: 'About' }, html: '/legal/about.html' },
    { slug: 'terms', title: { zh: '用户协议', en: 'User agreement' }, group: { zh: '关于', en: 'About' }, html: '/legal/terms.html' },
    { slug: 'privacy', title: { zh: '隐私政策', en: 'Privacy policy' }, group: { zh: '关于', en: 'About' }, html: '/legal/privacy.html' }
  ];

  var MISSING = {
    zh: { title: '页面不存在', text: '请从左侧目录选择文档。' },
    en: { title: 'Page not found', text: 'Choose a page from the list.' }
  };

  function lang() {
    return window.cmLang ? window.cmLang.get() : 'zh';
  }

  function currentSlug() {
    var match = /\/docs\/([a-z0-9-]+)\/?$/.exec(window.location.pathname);
    var slug = match ? match[1] : PAGES[0].slug;
    return PAGES.some(function (p) { return p.slug === slug; }) ? slug : null;
  }

  function pageOf(slug) {
    return PAGES.filter(function (p) { return p.slug === slug; })[0];
  }

  function renderNav(active) {
    var list = document.getElementById('docs-pages');
    var language = lang();
    var group = '';
    list.textContent = '';
    PAGES.forEach(function (page) {
      // A page with a new group starts a heading in the list ("About" before the legal pages).
      if (page.group && page.group[language] !== group) {
        group = page.group[language];
        var heading = document.createElement('li');
        heading.className = 'docs-group';
        heading.textContent = group;
        list.appendChild(heading);
      }
      var item = document.createElement('li');
      var link = document.createElement('a');
      link.href = '/docs/' + page.slug;
      link.textContent = page.title[language];
      if (page.slug === active) {
        link.setAttribute('aria-current', 'page');
      }
      item.appendChild(link);
      list.appendChild(item);
    });
  }

  function showMissing(target) {
    var text = MISSING[lang()];
    target.innerHTML = '<h1></h1><p class="missing"></p>';
    target.querySelector('h1').textContent = text.title;
    target.querySelector('p').textContent = text.text;
    document.title = text.title + ' · Chameleon';
  }

  function scrollToHash() {
    if (window.location.hash) {
      var anchor = document.getElementById(decodeURIComponent(window.location.hash.slice(1)));
      if (anchor) {
        anchor.scrollIntoView();
      }
    }
  }

  function fetchText(url) {
    return fetch(url, { cache: 'no-cache' }).then(function (response) {
      if (!response.ok) {
        throw new Error('HTTP ' + response.status);
      }
      return response.text();
    });
  }

  // Each render gets a number, so a slow answer for the previous language cannot overwrite it.
  var renderCount = 0;

  function render(slug) {
    var target = document.getElementById('doc');
    var language = lang();
    var ticket = ++renderCount;
    if (!slug) {
      showMissing(target);
      return;
    }
    var page = pageOf(slug);
    var url = page.html || ('/docs/' + slug + (language === 'en' ? '.en' : '') + '.md');
    fetchText(url)
      .then(function (text) {
        if (ticket !== renderCount) {
          return;
        }
        // Both kinds of page are this site's own files, so their HTML is trusted.
        target.innerHTML = page.html ? text : window.marked.parse(text);
        document.title = page.title[language] + ' · Chameleon';
        scrollToHash();
      })
      .catch(function () {
        if (ticket === renderCount) {
          showMissing(target);
        }
      });
  }

  // Headings get ids from their text, so sections can be linked (e.g. #windows-firewall).
  window.marked.use({
    renderer: {
      heading: function (token) {
        var text = this.parser.parseInline(token.tokens);
        var id = token.text.toLowerCase().trim().replace(/[\s]+/g, '-').replace(/[^\p{L}\p{N}-]/gu, '');
        return '<h' + token.depth + ' id="' + id + '">' + text + '</h' + token.depth + '>\n';
      }
    }
  });

  var slug = currentSlug();
  renderNav(slug);
  render(slug);
  document.addEventListener('cm-lang-change', function () {
    renderNav(slug);
    if (!pageOf(slug) || !pageOf(slug).html) {
      // Markdown pages have one file per language; the legal pages switch by CSS alone.
      render(slug);
    } else {
      document.title = pageOf(slug).title[lang()] + ' · Chameleon';
    }
  });
}());
