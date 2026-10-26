"""Validate project-relative website links, anchors and provider tab targets."""
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1] / "site"


class Page(HTMLParser):
    def __init__(self, path):
        super().__init__()
        self.path, self.ids, self.links, self.tabs = path, set(), [], []
        self.feed(path.read_text(encoding="utf-8-sig"))

    def handle_starttag(self, tag, attrs):
        attr = dict(attrs)
        if "id" in attr:
            assert attr["id"] not in self.ids, f"Duplicate ID {attr['id']}"
            self.ids.add(attr["id"])
        for name in ("href", "src"):
            if name in attr:
                self.links.append(attr[name])
        if "aria-controls" in attr:
            self.tabs.append(attr["aria-controls"])


pages = {p.resolve(): Page(p) for p in ROOT.glob("*.html")}
for path, page in pages.items():
    for link in page.links:
        parsed = urlsplit(link)
        if parsed.scheme or parsed.netloc:
            continue
        target = (path.parent / unquote(parsed.path or path.name)).resolve()
        if target.is_dir():
            target /= "index.html"
        assert target.is_relative_to(ROOT.resolve()) and target.is_file(), f"Broken link in {path.name}: {link}"
        if parsed.fragment:
            assert parsed.fragment in pages[target].ids, f"Broken anchor: {link}"
    assert all(target in page.ids for target in page.tabs), f"Missing tab panel in {path.name}"
print(f"PASS: {len(pages)} pages; all local links, anchors and tab panels resolve.")
