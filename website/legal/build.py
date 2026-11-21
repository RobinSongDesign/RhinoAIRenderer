#!/usr/bin/env python3
"""
Builds the About, user agreement and privacy policy pages in English and Chinese.

Sources are Markdown in src/<doc>.<lang>.md. Each output <doc>.html is one self-contained HTML
fragment with the English text followed by the Chinese text. The same fragment is pasted into
new-api (About / user agreement / privacy policy) and shown on rhchameleon.top.

new-api renders these settings in a shadow root without a page container, directly under its
fixed 3.5rem header, and copies its own stylesheets (including Tailwind's reset) into that root.
The fragment therefore keeps clear of the header and sets list markers and heading weights
explicitly instead of relying on browser defaults.

Operator details come from legal.json; empty values show a placeholder in each language.

Usage: python3 website/legal/build.py [--check]
"""
from __future__ import annotations

import html
import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
DOCS = ("about", "terms", "privacy")

# Languages in the order they appear on the page.
LANGUAGES = ("en", "zh-CN")

PLACEHOLDERS = {
    "operator": {"zh-CN": "〔运营者名称〕", "en": "[Operator name]"},
    "email": {"zh-CN": "〔联系邮箱〕", "en": "[Contact e-mail]"},
    "effective": {"zh-CN": "〔生效日期〕", "en": "[Effective date]"},
}

STYLE = """
<style>
/* Clears new-api's fixed header (3.5rem); the website resets this padding. */
.cm-doc { line-height: 1.75; font-size: 15px; max-width: 860px; margin: 0 auto; padding: calc(3.5rem + 32px) 20px 48px; }
/* Explicit styles: new-api's Tailwind reset reaches into the fragment. */
.cm-doc h1 { font-size: 28px; line-height: 1.3; font-weight: 700; margin: 0 0 8px; }
.cm-doc h2 { font-size: 19px; line-height: 1.4; font-weight: 700; margin: 32px 0 10px; padding-top: 16px; border-top: 1px solid rgba(128, 128, 128, 0.25); }
.cm-doc h3 { font-size: 16px; font-weight: 700; margin: 20px 0 6px; }
.cm-doc p { margin: 0 0 12px; }
.cm-doc ul, .cm-doc ol { margin: 0 0 12px; padding-left: 1.5em; }
.cm-doc ul { list-style: disc outside; }
.cm-doc ol { list-style: decimal outside; }
.cm-doc li { display: list-item; margin: 4px 0; }
.cm-doc strong { font-weight: 700; }
.cm-doc .cm-meta { opacity: 0.7; font-size: 14px; margin-bottom: 20px; }
.cm-doc a { color: inherit; text-decoration: underline; text-decoration-color: #e6007e; text-underline-offset: 3px; }
.cm-doc code { font-size: 0.92em; padding: 1px 6px; border-radius: 6px; background: rgba(128, 128, 128, 0.15); }
.cm-doc table { border-collapse: collapse; width: 100%; margin: 0 0 16px; display: block; overflow-x: auto; }
.cm-doc th, .cm-doc td { border: 1px solid rgba(128, 128, 128, 0.3); padding: 8px 10px; text-align: left; vertical-align: top; }
.cm-doc th { font-weight: 700; background: rgba(128, 128, 128, 0.1); }
.cm-doc .cm-brand { display: block; width: 44px; height: 6px; border-radius: 3px; background: linear-gradient(90deg, #00c8e0, #e6007e); margin: 0 0 16px; }
.cm-doc .cm-sep { border: 0; height: 2px; margin: 56px 0 48px; background: linear-gradient(90deg, #00c8e0, #e6007e); opacity: 0.6; }
</style>
"""

_INLINE_CODE = re.compile(r"`([^`]+)`")
_BOLD = re.compile(r"\*\*(.+?)\*\*")
_LINK = re.compile(r"\[([^\]]+)\]\((https?://[^)\s]+|/[^)\s]*)\)")
_AUTOLINK = re.compile(r"&lt;(https?://[^&\s]+)&gt;")


def inline(text: str) -> str:
    """Markdown inline subset: code, bold, [text](url), <url>. Escapes everything else."""
    codes: list[str] = []

    def keep_code(m: re.Match) -> str:
        codes.append("<code>" + html.escape(m.group(1)) + "</code>")
        return "\x00%d\x00" % (len(codes) - 1)

    out = _INLINE_CODE.sub(keep_code, text)
    out = html.escape(out, quote=False)
    out = _BOLD.sub(r"<strong>\1</strong>", out)
    out = _LINK.sub(lambda m: '<a href="%s" target="_blank" rel="noopener">%s</a>' % (html.escape(m.group(2)), m.group(1)), out)
    out = _AUTOLINK.sub(lambda m: '<a href="%s" target="_blank" rel="noopener">%s</a>' % (m.group(1), m.group(1)), out)
    return re.sub("\x00(\\d+)\x00", lambda m: codes[int(m.group(1))], out)


def markdown(text: str) -> str:
    """Block subset used by the legal sources: headings, paragraphs, lists, tables."""
    out: list[str] = []
    lines = text.strip("\n").splitlines()
    i = 0
    while i < len(lines):
        line = lines[i].rstrip()
        if not line.strip():
            i += 1
            continue
        heading = re.match(r"^(#{1,3})\s+(.*)$", line)
        if heading:
            level = len(heading.group(1))
            out.append("<h%d>%s</h%d>" % (level, inline(heading.group(2)), level))
            i += 1
            continue
        if line.startswith("|"):
            rows = []
            while i < len(lines) and lines[i].startswith("|"):
                rows.append([c.strip() for c in lines[i].strip().strip("|").split("|")])
                i += 1
            head, body = rows[0], [r for r in rows[1:] if not all(re.fullmatch(r":?-+:?", c) for c in r)]
            cells = "".join("<th>%s</th>" % inline(c) for c in head)
            trs = "".join("<tr>%s</tr>" % "".join("<td>%s</td>" % inline(c) for c in r) for r in body)
            out.append("<table><thead><tr>%s</tr></thead><tbody>%s</tbody></table>" % (cells, trs))
            continue
        ordered = re.match(r"^\d+\.\s+", line)
        if line.startswith("- ") or ordered:
            tag = "ol" if ordered else "ul"
            pattern = r"^\d+\.\s+" if ordered else r"^- "
            items = []
            while i < len(lines) and re.match(pattern, lines[i]):
                items.append("<li>%s</li>" % inline(re.sub(pattern, "", lines[i].rstrip())))
                i += 1
            out.append("<%s>%s</%s>" % (tag, "".join(items), tag))
            continue
        para = []
        while i < len(lines) and lines[i].strip() and not re.match(r"^(#{1,3}\s|\||- |\d+\.\s)", lines[i]):
            para.append(lines[i].strip())
            i += 1
        text_block = " ".join(para)
        css = ' class="cm-meta"' if text_block.startswith("~ ") else ""
        out.append("<p%s>%s</p>" % (css, inline(text_block[2:] if css else text_block)))
    return "\n".join(out)


def fill(text: str, lang: str, values: dict) -> str:
    """Replaces {{operator}}, {{email}}, {{effective}} with legal.json values or placeholders."""
    def value(m: re.Match) -> str:
        key = m.group(1)
        if key not in PLACEHOLDERS:
            raise KeyError("Unknown field {{%s}}" % key)
        given = (values.get(key) or "").strip()
        return given or PLACEHOLDERS[key][lang]
    return re.sub(r"\{\{(\w+)\}\}", value, text)


def build_doc(doc: str, sources: dict[str, str], values: dict) -> str:
    """One fragment: each language in LANGUAGES order, separated by a rule."""
    sections = ['<section lang="%s">\n<div class="cm-brand"></div>\n%s\n</section>'
                % (code, markdown(fill(sources[code], code, values))) for code in LANGUAGES]
    return '<div class="cm-doc">\n%s\n%s\n</div>\n' % (STYLE.strip(), '\n<hr class="cm-sep">\n'.join(sections))


def load_sources(doc: str) -> dict[str, str]:
    """Markdown sources of a document, by language."""
    return {code: (HERE / "src" / ("%s.%s.md" % (doc, code))).read_text(encoding="utf-8") for code in LANGUAGES}


def main(argv: list[str]) -> int:
    """Writes the pages; with --check, only reports pages that differ from their sources."""
    check = "--check" in argv
    values = json.loads((HERE / "legal.json").read_text(encoding="utf-8"))
    stale = []
    for doc in DOCS:
        out = HERE / ("%s.html" % doc)
        built = build_doc(doc, load_sources(doc), values)
        if check:
            if not out.exists() or out.read_text(encoding="utf-8") != built:
                stale.append(out.name)
            continue
        out.write_text(built, encoding="utf-8")
        print("wrote", out.relative_to(HERE.parent.parent), out.stat().st_size, "bytes", file=sys.stderr)
    if stale:
        print("Out of date (run website/legal/build.py): " + ", ".join(stale), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
