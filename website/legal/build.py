#!/usr/bin/env python3
"""
Builds the multilingual About, user agreement and privacy policy pages.

Sources are Markdown per language in src/<doc>.<lang>.md (Traditional Chinese is converted from
Simplified with OpenCC, so it is never edited by hand). Each output <doc>.html is one
self-contained HTML fragment holding every language, switched by CSS-only tabs: new-api strips
scripts from these settings and never sets the page language, so the reader picks the language.
The same fragment is pasted into new-api (About / user agreement / privacy policy) and shown on
rhchameleon.top.

Operator details come from legal.json; empty values show a placeholder in each language.

Usage: python3 website/legal/build.py [--check]   (needs: pip install opencc-python-reimplemented)
"""
from __future__ import annotations

import html
import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
DOCS = ("about", "terms", "privacy")

# new-api's interface languages, in its own order of preference for Chinese users.
LANGUAGES = (
    ("zh-CN", "简体中文"),
    ("zh-TW", "繁體中文"),
    ("en", "English"),
    ("ja", "日本語"),
    ("fr", "Français"),
    ("ru", "Русский"),
    ("vi", "Tiếng Việt"),
)
DEFAULT_LANGUAGE = "zh-CN"

PLACEHOLDERS = {
    "operator": {"zh-CN": "〔运营者名称〕", "en": "[Operator name]", "ja": "〔運営者名〕", "fr": "[Nom de l’exploitant]", "ru": "[Наименование оператора]", "vi": "[Tên đơn vị vận hành]"},
    "email": {"zh-CN": "〔联系邮箱〕", "en": "[Contact e-mail]", "ja": "〔連絡先メール〕", "fr": "[Adresse e-mail de contact]", "ru": "[Контактный e-mail]", "vi": "[Email liên hệ]"},
    "effective": {"zh-CN": "〔生效日期〕", "en": "[Effective date]", "ja": "〔施行日〕", "fr": "[Date d’entrée en vigueur]", "ru": "[Дата вступления в силу]", "vi": "[Ngày hiệu lực]"},
}

# Mainland terms OpenCC's Taiwan phrase table leaves in a mainland form.
TW_FIXES = {"賬號": "帳號", "賬戶": "帳戶", "賬": "帳"}

STYLE = """
<style>
.cm-doc { line-height: 1.75; font-size: 15px; max-width: 860px; margin: 0 auto; padding: 8px 4px 32px; }
.cm-doc .cm-lang-input { position: absolute; opacity: 0; pointer-events: none; width: 1px; height: 1px; }
.cm-doc .cm-lang-tabs { display: flex; flex-wrap: wrap; gap: 8px; margin: 0 0 24px; }
.cm-doc .cm-lang-tabs label { cursor: pointer; padding: 4px 14px; border-radius: 999px; border: 1px solid rgba(128, 128, 128, 0.35); font-size: 13px; opacity: 0.8; user-select: none; }
.cm-doc .cm-lang-tabs label:hover { opacity: 1; }
.cm-doc .cm-lang { display: none; }
.cm-doc h1 { font-size: 28px; line-height: 1.3; margin: 0 0 8px; }
.cm-doc h2 { font-size: 19px; margin: 32px 0 10px; padding-top: 16px; border-top: 1px solid rgba(128, 128, 128, 0.25); }
.cm-doc h3 { font-size: 16px; margin: 20px 0 6px; }
.cm-doc p, .cm-doc ul, .cm-doc ol { margin: 0 0 12px; }
.cm-doc ul, .cm-doc ol { padding-left: 1.4em; }
.cm-doc li { margin: 4px 0; }
.cm-doc .cm-meta { opacity: 0.7; font-size: 14px; margin-bottom: 20px; }
.cm-doc a { color: inherit; text-decoration: underline; text-decoration-color: #e6007e; text-underline-offset: 3px; }
.cm-doc code { font-size: 0.92em; padding: 1px 6px; border-radius: 6px; background: rgba(128, 128, 128, 0.15); }
.cm-doc table { border-collapse: collapse; width: 100%; margin: 0 0 16px; display: block; overflow-x: auto; }
.cm-doc th, .cm-doc td { border: 1px solid rgba(128, 128, 128, 0.3); padding: 8px 10px; text-align: left; vertical-align: top; }
.cm-doc th { background: rgba(128, 128, 128, 0.1); }
.cm-doc .cm-brand { display: inline-block; width: 44px; height: 6px; border-radius: 3px; background: linear-gradient(90deg, #00c8e0, #e6007e); margin: 0 0 16px; }
__SWITCH__
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
        return given or PLACEHOLDERS[key].get(lang, PLACEHOLDERS[key]["en"])
    return re.sub(r"\{\{(\w+)\}\}", value, text)


def to_traditional(text: str) -> str:
    import opencc  # Only needed for zh-TW; imported here so tests run without it.
    converted = opencc.OpenCC("s2twp").convert(text)
    for wrong, right in TW_FIXES.items():
        converted = converted.replace(wrong, right)
    return converted


def build_doc(doc: str, sources: dict[str, str], values: dict) -> str:
    """One fragment with every language. sources maps language code to Markdown; Traditional
    Chinese is always converted from the Simplified Chinese source, never taken from its own."""
    ids = {code: "cm-%s-%s" % (doc, code.lower()) for code, _ in LANGUAGES}
    switch = "\n".join(
        "#%s:checked ~ .cm-lang-%s { display: block; }\n"
        "#%s:checked ~ .cm-lang-tabs label[for=\"%s\"] { background: linear-gradient(135deg, #0097b2, #c4006f); border-color: transparent; color: #fff; opacity: 1; }"
        % (ids[c], c.lower(), ids[c], ids[c]) for c, _ in LANGUAGES)
    parts = ['<div class="cm-doc">', STYLE.replace("__SWITCH__", switch).strip()]
    for code, _ in LANGUAGES:
        checked = " checked" if code == DEFAULT_LANGUAGE else ""
        parts.append('<input type="radio" class="cm-lang-input" name="cm-%s-lang" id="%s"%s>' % (doc, ids[code], checked))
    parts.append('<div class="cm-lang-tabs">%s</div>' % "".join(
        '<label for="%s" lang="%s">%s</label>' % (ids[c], c, html.escape(n)) for c, n in LANGUAGES))
    for code, _ in LANGUAGES:
        if code == "zh-TW":
            md = to_traditional(fill(sources["zh-CN"], "zh-CN", values))
        else:
            md = fill(sources[code], code, values)
        parts.append('<section class="cm-lang cm-lang-%s" lang="%s">\n<div class="cm-brand"></div>\n%s\n</section>' % (code.lower(), code, markdown(md)))
    parts.append("</div>")
    return "\n".join(parts) + "\n"


def load_sources(doc: str) -> dict[str, str]:
    """Markdown sources of a document; there is none for zh-TW (see build_doc)."""
    return {code: (HERE / "src" / ("%s.%s.md" % (doc, code))).read_text(encoding="utf-8")
            for code, _ in LANGUAGES if code != "zh-TW"}


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
