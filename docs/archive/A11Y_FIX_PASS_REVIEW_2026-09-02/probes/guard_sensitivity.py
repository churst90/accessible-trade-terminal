"""Q5: replay ChromeAccessibilityScanTests.TheChartsStatusChromeTakesItsColoursFromTheTheme's regexes
against MUTATED COPIES of ChartArea.razor (production file untouched)."""
import re
SRC="/home/cody/external-rescue/Github/accessible-trade-terminal/AccessibleTrader.BlazorClient.Components/ChartArea.razor"
orig=open(SRC).read()
def code_only(t):
    t=re.sub(r"@\*.*?\*@","",t,flags=re.S); t=re.sub(r"/\*.*?\*/","",t,flags=re.S); return re.sub(r"(?m)^\s*//.*$","",t)
HEADER='<div style="font-size: 1.8rem; margin-bottom: 1rem;">'
PARENT='color: @(GetThemeTextHex());'
assert orig.count(HEADER)==1 and orig.count(PARENT)==1
def guard(text):
    chart=code_only(text)
    ov=re.search(r"blackout-overlay.*?(?=@code)",chart,re.S)
    assert ov
    white=re.search(r"color\s*:\s*#(fff|ffffff)\b",ov.group(0),re.I) is not None
    return ("CAUGHT" if white else "MISSED")
cases={
 "baseline (as committed)": orig,
 "header  color: white":            orig.replace(HEADER,'<div style="font-size: 1.8rem; margin-bottom: 1rem; color: white;">'),
 "header  color:#FFF":              orig.replace(HEADER,'<div style="font-size: 1.8rem; margin-bottom: 1rem; color:#FFF;">'),
 "header  color: #ffffff":          orig.replace(HEADER,'<div style="font-size: 1.8rem; margin-bottom: 1rem; color: #ffffff;">'),
 "header  color: rgb(255,255,255)": orig.replace(HEADER,'<div style="font-size: 1.8rem; margin-bottom: 1rem; color: rgb(255,255,255);">'),
 "header  color: #eee":             orig.replace(HEADER,'<div style="font-size: 1.8rem; margin-bottom: 1rem; color: #eee;">'),
 "header  color: #000 (invisible on 9 dark themes)": orig.replace(HEADER,'<div style="font-size: 1.8rem; margin-bottom: 1rem; color: #000;">'),
 "parent  color: #fff":             orig.replace(PARENT,'color: #fff;'),
 "parent  color: white":            orig.replace(PARENT,'color: white;'),
 "parent  color: #FFFFFF":          orig.replace(PARENT,'color: #FFFFFF;'),
 "parent  color: #000":             orig.replace(PARENT,'color: #000;'),
 "parent  color removed entirely (inherits page --text-primary? no: inherits body)": orig.replace(PARENT,''),
}
for k,v in cases.items(): print(f"{guard(v):7} {k}")
