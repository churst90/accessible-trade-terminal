import re
src = open('/home/cody/external-rescue/Github/accessible-trade-terminal/AccessibleTrader.Core/Services/ThemeService.cs').read()
def lin(c):
    c/=255
    return c/12.92 if c<=0.03928 else ((c+0.055)/1.055)**2.4
def L(rgb): r,g,b=rgb; return 0.2126*lin(r)+0.7152*lin(g)+0.0722*lin(b)
def cr(a,b):
    la,lb=L(a),L(b); hi,lo=max(la,lb),min(la,lb); return (hi+0.05)/(lo+0.05)
named={'Black':(0,0,0),'White':(255,255,255),'Yellow':(255,255,0)}
def col(s):
    s=s.strip()
    m=re.match(r'new SKColor\(\s*([^)]*)\)',s)
    if m:
        parts=[p.strip() for p in m.group(1).split(',')]
        return tuple(int(p,0) for p in parts[:3])
    m=re.match(r'SKColors\.(\w+)',s)
    if m: return named[m.group(1)]
    return None
blocks=re.split(r"private static ChartTheme (\w+)\(\) => new\(\)",src)[1:]; blocks=[blocks[i]+"\nName = \""+blocks[i]+"\"\n"+blocks[i+1] for i in range(0,len(blocks),2)]
print(f"{'theme':28} {'ring':8} {'vs Background':>14} {'vs ChromeBottom':>16} {'vs BottomEnd':>13} {'vs toolbar(SurfaceRaised)':>26}")
for b in blocks:
    f={}
    for k in ['Name','Id','DisplayName','BackgroundGradientEnd','Background','SurfaceRaised','ChromeBottom','ChromeBottomEnd','Crosshair']:
        m=re.search(r'\b'+k+r'\s*=\s*([^\n]+)',b)
        if m: f[k]=m.group(1).strip()
    name=f.get('Name') or f.get('Id') or f.get('DisplayName') or '?'
    name=name.strip('"')
    bg=col(f.get('Background','')); sr=col(f.get('SurfaceRaised','')); cb=col(f.get('ChromeBottom','')); cbe=col(f.get('ChromeBottomEnd','')) or cb
    if not (bg and sr): print(name,'(incomplete)',f); continue
    ring=(0,32,176) if L(sr)>0.5 else (255,255,0)
    print(f"{name:28} {'#%02x%02x%02x'%ring:8} {cr(ring,bg):14.2f} {cr(ring,cb):16.2f} {cr(ring,cbe):13.2f} {cr(ring,sr):26.2f}")
