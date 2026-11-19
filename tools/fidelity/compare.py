# python3 compare.py <dir>  — compares the effective inline styles, element by element, of <dir>/ours-inlined.html and <dir>/theirs-inlined.html
import re,sys
from html.parser import HTMLParser
class P(HTMLParser):
    def __init__(s): super().__init__(); s.els=[]
    def handle_starttag(s,tag,attrs):
        a=dict(attrs); s.els.append((tag,a.get('class',''),a.get('style','')))
def els(path):
    p=P(); p.feed(open(path).read()); return p.els
def norm(style):
    d={}
    for decl in style.split(';'):
        if ':' not in decl: continue
        k,v=decl.split(':',1); k=k.strip().lower(); v=re.sub(r'\s+',' ',v.strip().lower()).replace(' !important','')
        if k.startswith('-moz-') or k.startswith('-webkit-'): continue
        v=re.sub(r'\b0px\b','0',v)
        v=re.sub(r'(\d+\.\d+)',lambda m: ('%.2f'%float(m.group(1))).rstrip('0').rstrip('.'),v)
        v=re.sub(r'rgb\(([\d. ]+),([\d. ]+),([\d. ]+)\)',lambda m:'#%02x%02x%02x'%tuple(int(round(float(x))) for x in m.groups()),v)
        d[k]=v
    # shorthands to longhands, so that `padding: 8px 16px` equals `padding: 8px 16px 8px 16px` and the four longhands
    for prop in ('padding','margin'):
        if prop in d:
            parts=d.pop(prop).split()
            if len(parts)==1: parts=parts*4
            elif len(parts)==2: parts=[parts[0],parts[1],parts[0],parts[1]]
            elif len(parts)==3: parts=[parts[0],parts[1],parts[2],parts[1]]
            for side,v in zip(('top','right','bottom','left'),parts):
                d.setdefault(prop+'-'+side,v) if False else d.__setitem__(prop+'-'+side,v)
    return d
o=[e for e in els(sys.argv[1]+'/ours-inlined.html')]
t=[e for e in els(sys.argv[1]+'/theirs-inlined.html') if e[0] not in ('html','head','body','style')]
o=[e for e in o if e[0] not in ('html','head','body','style')]
print("elements: ours",len(o),"theirs",len(t))
same=0; diffs=[]
for (a,b) in zip(o,t):
    if a[0]!=b[0]: print("STRUCTURE differs",a,b); break
    x,y=norm(a[2]),norm(b[2])
    if x==y: same+=1
    else: diffs.append((a[0],a[1],x,y))
print(f"elements with identical effective inline styles: {same} / {len(o)}")
for tag,cls,x,y in diffs[:20]:
    print(f"  <{tag} class='{cls}'>")
    for k in sorted(set(x)|set(y)):
        if x.get(k)!=y.get(k): print(f"      {k}: ours={x.get(k)}  foundation={y.get(k)}")
