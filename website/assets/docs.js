/*
 * Documentation reader: the pages are Markdown files next to docs/index.html, rendered in the
 * browser so they stay plain .md in the repository and need no build step.
 * /docs/network loads /docs/network.md; /docs/ loads the first page of the list below.
 */
(function () {
  'use strict';

  // Pages in navigation order. The slug is the file name without ".md".
  var PAGES = [
    { slug: 'getting-started', title: '安装与开始使用' },
    { slug: 'account', title: '账号、充值与余额' },
    { slug: 'network', title: '无法连接网络' },
    { slug: 'about', title: '关于 Chameleon', group: '关于' },
    { slug: 'terms', title: '用户协议', group: '关于' },
    { slug: 'privacy', title: '隐私政策', group: '关于' }
  ];

  function currentSlug() {
    var match = /\/docs\/([a-z0-9-]+)\/?$/.exec(window.location.pathname);
    var slug = match ? match[1] : PAGES[0].slug;
    return PAGES.some(function (p) { return p.slug === slug; }) ? slug : null;
  }

  function renderNav(active) {
    var list = document.getElementById('docs-pages');
    var group = '';
    PAGES.forEach(function (page) {
      // A page with a new group starts a heading in the list (e.g. "关于" before the legal pages).
      if (page.group && page.group !== group) {
        group = page.group;
        var heading = document.createElement('li');
        heading.className = 'docs-group';
        heading.textContent = group;
        list.appendChild(heading);
      }
      var item = document.createElement('li');
      var link = document.createElement('a');
      link.href = '/docs/' + page.slug;
      link.textContent = page.title;
      if (page.slug === active) {
        link.setAttribute('aria-current', 'page');
      }
      item.appendChild(link);
      list.appendChild(item);
    });
  }

  function showMissing(target) {
    target.innerHTML = '<h1>页面不存在</h1><p class="missing">请从左侧目录选择文档。</p>';
    document.title = '页面不存在 · Chameleon 文档';
  }

  function render(slug) {
    var target = document.getElementById('doc');
    if (!slug) {
      showMissing(target);
      return;
    }
    fetch('/docs/' + slug + '.md', { cache: 'no-cache' })
      .then(function (response) {
        if (!response.ok) {
          throw new Error('HTTP ' + response.status);
        }
        return response.text();
      })
      .then(function (markdown) {
        // The pages are this site's own files, so their HTML is trusted.
        target.innerHTML = window.marked.parse(markdown);
        var heading = target.querySelector('h1');
        document.title = (heading ? heading.textContent : 'Chameleon') + ' · Chameleon 文档';
        if (window.location.hash) {
          var anchor = document.getElementById(decodeURIComponent(window.location.hash.slice(1)));
          if (anchor) {
            anchor.scrollIntoView();
          }
        }
      })
      .catch(function () { showMissing(target); });
  }

  // Headings get ids from their text, so sections can be linked (e.g. #windows-防火墙).
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
}());
