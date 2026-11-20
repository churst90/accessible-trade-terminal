# Faithful replica of ChromeAccessibilityScanTests.NoContainerDeclaresToolbarWithoutAnArrowKeyModel
# (lines 150-165) plus ModalContractScanTests.CodeOnly (lines 42-49), fed synthetic file bodies
# to show exactly which spellings of a toolbar role the guard can and cannot see.
import re
def code_only(t):
    t = re.sub(r"@\*.*?\*@", "", t, flags=re.S)
    t = re.sub(r"/\*.*?\*/", "", t, flags=re.S)
    t = re.sub(r"(?m)^\s*//.*$", "", t)
    return t
def guard(text):
    text = code_only(text)
    if 'role="toolbar"' not in text and "role='toolbar'" not in text:
        return "SKIPPED (Contains gate)"
    f = []
    if "@onkeydown" not in text: f.append("no-onkeydown")
    if re.search(r"""<nav\b[^>]*role\s*=\s*["']toolbar""", text): f.append("nav-override")
    return "RED " + ",".join(f) if f else "GREEN"
cases = {
 'baseline double-quoted on div':        '<div role="toolbar" aria-label="x">',
 'baseline single-quoted on div':        "<div role='toolbar' aria-label='x'>",
 'nav double-quoted':                    '<nav role="toolbar" aria-label="x">',
 'nav with spaces around =':             '<nav role = "toolbar" aria-label="x">',
 'unquoted role=toolbar (valid HTML)':   '<div role=toolbar aria-label="x">',
 'multi-token role="toolbar group"':     '<div role="toolbar group">',
 'multi-token role="group toolbar"':     '<div role="group toolbar">',
 'uppercase role="TOOLBAR"':             '<div role="TOOLBAR">',
 'razor expression role=@("toolbar")':   '<div role=@("toolbar")>',
 'razor string role="@Role" (C# var)':   '<div role="@Role">',
 'in a razor comment only':              '@* <nav role="toolbar"> *@ <nav aria-label="x">',
 'has onkeydown, on div':                '<div role="toolbar" @onkeydown="K">',
 'has onkeydown, on nav':                '<nav role="toolbar" @onkeydown="K">',
}
for k,v in cases.items(): print(f"{k:40s} -> {guard(v)}")
