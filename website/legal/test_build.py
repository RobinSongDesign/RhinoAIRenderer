"""Tests of the legal page generator. Run: python3 -m unittest discover -s website/legal"""
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
    def test_english_then_chinese_without_language_switching(self):
        sources = {"en": "# Terms\n\nMail {{email}}", "zh-CN": "# 协议\n\n邮箱 {{email}}"}
        out = build.build_doc("terms", sources, {"email": "a@example.com"})

        self.assertTrue(out.startswith('<div class="cm-doc">'))
        self.assertLess(out.index('<section lang="en">'), out.index('<section lang="zh-CN">'))
        self.assertEqual(1, out.count('<hr class="cm-sep">'))
        self.assertIn("<p>Mail a@example.com</p>", out)
        self.assertIn("<p>邮箱 a@example.com</p>", out)
        self.assertNotIn("<script", out)
        self.assertNotIn("<input", out)

    def test_styles_survive_the_reset_new_api_copies_into_the_fragment(self):
        out = build.build_doc("about", {"en": "# A", "zh-CN": "# B"}, {})
        # new-api puts the fragment under its fixed header and applies Tailwind's reset inside it.
        self.assertIn("padding: calc(3.5rem + 32px)", out)
        self.assertIn("list-style: disc", out)
        self.assertIn("list-style: decimal", out)
        self.assertRegex(out, r"\.cm-doc h2 \{[^}]*font-weight: 700")


if __name__ == "__main__":
    unittest.main()
