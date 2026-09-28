"""Names of every placeholder a template pattern may use, and which are optional by default."""
from __future__ import annotations

ALL_PLACEHOLDERS: frozenset[str] = frozenset({
    "terms", "termsAny", "quoted", "quotedReversed", "quotedInitialLast", "phraseWildcard",
    "intitle", "intitleAny", "inurl", "inurlAny", "intext", "intextAny",
    "domain", "domainQuoted", "domainLabel", "domainLabelBare", "site", "siteOption", "siteWildcard",
    "excludeSite", "excludeWww", "atDomainQuoted",
    "username", "quotedUsername", "atUsername", "inurlUsername", "intitleUsername", "hashtagUsername",
    "emailQuoted", "emailUserQuoted",
    "url", "urlQuoted", "urlBareQuoted", "inurlPath",
    "filename", "filenameStem", "filetypeFromFilename",
    "filetypeGroup", "filetypeFirst", "exclusions", "dates", "after", "before",
    "keywords", "keywordsAnd", "platformSites",
    "organization", "location", "role", "displayName", "context",
})

OPTIONAL_BY_DEFAULT: frozenset[str] = frozenset({"exclusions", "dates", "after", "before", "site", "siteOption", "context"})


def placeholders_in(pattern: str) -> list[str]:
    """Extract {placeholder} names from a pattern in order of appearance."""
    names: list[str] = []
    i = 0
    while True:
        open_ = pattern.find("{", i)
        if open_ < 0:
            break
        close = pattern.find("}", open_ + 1)
        if close < 0:
            break
        names.append(pattern[open_ + 1:close])
        i = close + 1
    return names
