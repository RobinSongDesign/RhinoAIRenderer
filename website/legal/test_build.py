"""Tests of the legal page generator. Run: python3 -m unittest discover -s website/legal"""
import re
import unittest

import build


class MarkdownTests(unittest.TestCase):
    def test_headings_paragraphs_lists_and_meta_lines(self):
        out = build.markdown("# Title\n\n~ Effective: now\n\nFirst line\nsame paragraph.\n\n- a\n- b\n\n1. one\n2. two\n")
        self.assertIn("<h1>Title</h1>", out)
        self.assertIn('<p class="cm-meta">Effective: now</p>', out)
        self.assertIn("<p>First line same paragraph.</p>", out)
        self.assertIn("<ul><li>a</li><li>b</li></ul>", out)
        self.assertIn("<ol><li>one</li><li>two</li></ol>", out)

    def test_tables_skip_the_separator_row(self):
        out = build.markdown("| A | B |\n|---|---|\n| 1 | **2** |\n")
        self.assertEqual("<table><thead><tr><th>A</th><th>B</th></tr></thead><tbody><tr><td>1</td><td><strong>2</strong></td></tr></tbody></table>", out)

    def test_inline_markup_and_escaping(self):
        out = build.inline("Use `a<b>` and [docs](https://example.com/x) or <https://example.com> & <script>")
        self.assertIn("<code>a&lt;b&gt;</code>", out)
        self.assertIn('<a href="https://example.com/x" target="_blank" rel="noopener">docs</a>', out)
        self.assertIn('<a href="https://example.com" target="_blank" rel="noopener">https://example.com</a>', out)
        self.assertIn("&amp; &lt;script&gt;", out)
        self.assertNotIn("<script>", out)

    def test_only_http_links_become_anchors(self):
        self.assertNotIn("<a", build.inline("[x](javascript:alert(1))"))


class FillTests(unittest.TestCase):
    def test_values_or_language_placeholders(self):
        self.assertEqual("By ACME", build.fill("By {{operator}}", "en", {"operator": " ACME "}))
        self.assertEqual("By [Operator name]", build.fill("By {{operator}}", "en", {"operator": ""}))
        self.assertEqual("由〔运营者名称〕", build.fill("由{{operator}}", "zh-CN", {}))
        with self.assertRaises(KeyError):
            build.fill("{{unknown}}", "en", {})


class BuildDocTests(unittest.TestCase):
    def setUp(self):
        self._convert = build.to_traditional
        build.to_traditional = lambda text: "TW:" + text  # OpenCC is not needed to test the layout.

    def tearDown(self):
        build.to_traditional = self._convert

    def test_one_fragment_with_every_language_and_css_tabs(self):
        sources = {code: "# %s\n\nBody {{email}}" % code for code, _ in build.LANGUAGES}
        out = build.build_doc("terms", sources, {"email": "a@example.com"})

        self.assertTrue(out.startswith('<div class="cm-doc">'))
        self.assertEqual(len(build.LANGUAGES), out.count('<section class="cm-lang'))
        self.assertEqual(1, out.count(" checked>"))
        self.assertIn('id="cm-terms-zh-cn" checked', out)
        # Every tab has a rule that shows its section; no script is involved.
        for code, _ in build.LANGUAGES:
            self.assertIn("#cm-terms-%s:checked ~ .cm-lang-%s { display: block; }" % (code.lower(), code.lower()), out)
        self.assertNotIn("<script", out)
        # Traditional Chinese is converted from the Simplified Chinese source, values filled in first.
        tw = re.search(r'<section class="cm-lang cm-lang-zh-tw" lang="zh-TW">(.*?)</section>', out, re.S).group(1)
        self.assertIn("<p>TW:# zh-CN</p>", tw)
        self.assertIn("a@example.com", tw)


if __name__ == "__main__":
    unittest.main()
