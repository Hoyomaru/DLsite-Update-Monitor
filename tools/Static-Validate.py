#!/usr/bin/env python3
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
errors=[]
warnings=[]

# XML / XAML well-formedness
for path in list(ROOT.rglob('*.csproj')) + list(ROOT.rglob('*.xaml')):
    try:
        ET.parse(path)
    except Exception as exc:
        errors.append(f'XML parse failed: {path.relative_to(ROOT)}: {exc}')

# Project references exist
for csproj in ROOT.rglob('*.csproj'):
    try:
        tree=ET.parse(csproj)
    except Exception:
        continue
    base=csproj.parent
    for node in tree.getroot().iter():
        if node.tag.endswith('ProjectReference'):
            inc=node.attrib.get('Include','')
            target=(base / inc.replace('\\','/')).resolve()
            if not target.exists():
                errors.append(f'Missing ProjectReference target: {csproj.relative_to(ROOT)} -> {inc}')

# extension.yaml contract
manifest=ROOT/'src/DLsiteUpdateMonitor.Plugin/extension.yaml'
text=manifest.read_text(encoding='utf-8') if manifest.exists() else ''
for pattern,msg in [
    (r'(?m)^Module:\s*DLsiteUpdateMonitor\.dll\s*$', 'extension.yaml Module mismatch'),
    (r'(?m)^Type:\s*GenericPlugin\s*$', 'extension.yaml Type mismatch'),
    (r'(?m)^Id:\s*DLsiteUpdateMonitor_[0-9a-fA-F-]{36}\s*$', 'extension.yaml Id mismatch')]:
    if not re.search(pattern,text): errors.append(msg)

# Very small C# lexical delimiter scanner that ignores strings/chars/comments.
def scan_csharp(path: Path):
    s=path.read_text(encoding='utf-8-sig')
    stack=[]
    pairs={')':'(',']':'[','}':'{'}
    opens=set(pairs.values())
    i=0; state='code'; line=1
    while i < len(s):
        c=s[i]; n=s[i+1] if i+1 < len(s) else ''
        if c=='\n': line+=1
        if state=='code':
            if c=='/' and n=='/': state='line'; i+=2; continue
            if c=='/' and n=='*': state='block'; i+=2; continue
            if c=='@' and n=='"': state='vstr'; i+=2; continue
            if c=='$' and n=='@' and i+2<len(s) and s[i+2]=='"': state='vstr'; i+=3; continue
            if c=='@' and n=='$' and i+2<len(s) and s[i+2]=='"': state='vstr'; i+=3; continue
            if c=='$' and n=='"': state='str'; i+=2; continue
            if c=='"': state='str'; i+=1; continue
            if c=="'": state='char'; i+=1; continue
            if c in opens: stack.append((c,line))
            elif c in pairs:
                if not stack or stack[-1][0]!=pairs[c]:
                    errors.append(f'C# delimiter mismatch: {path.relative_to(ROOT)}:{line}: unexpected {c}')
                    return
                stack.pop()
            i+=1; continue
        if state=='line':
            if c=='\n': state='code'
            i+=1; continue
        if state=='block':
            if c=='*' and n=='/': state='code'; i+=2; continue
            i+=1; continue
        if state=='str':
            if c=='\\': i+=2; continue
            if c=='"': state='code'
            i+=1; continue
        if state=='vstr':
            if c=='"' and n=='"': i+=2; continue
            if c=='"': state='code'; i+=1; continue
            i+=1; continue
        if state=='char':
            if c=='\\': i+=2; continue
            if c=="'": state='code'
            i+=1; continue
    if state in {'str','vstr','char','block'}:
        errors.append(f'C# unterminated lexical construct: {path.relative_to(ROOT)} state={state}')
    if stack:
        c,l=stack[-1]
        errors.append(f'C# unclosed delimiter: {path.relative_to(ROOT)}:{l}: {c}')

for path in ROOT.rglob('*.cs'):
    scan_csharp(path)

# Static test-case estimate: facts + InlineData rows.
facts=0; inline=0
for path in (ROOT/'tests').rglob('*.cs'):
    t=path.read_text(encoding='utf-8-sig')
    facts += len(re.findall(r'\[Fact\]',t))
    inline += len(re.findall(r'\[InlineData\s*\(',t))
print(f'Static xUnit case estimate: {facts + inline} ({facts} Fact + {inline} InlineData rows)')
if facts + inline < 1: errors.append('No tests discovered statically')

# Dependency policy checks
core=(ROOT/'src/DLsiteUpdateMonitor.Core/DLsiteUpdateMonitor.Core.csproj').read_text(encoding='utf-8')
plugin=(ROOT/'src/DLsiteUpdateMonitor.Plugin/DLsiteUpdateMonitor.Plugin.csproj').read_text(encoding='utf-8')
required=[('AngleSharp','0.9.9',core),('Newtonsoft.Json','10.0.3',core),('PlayniteSDK','6.16.0',plugin)]
for name,version,src in required:
    if not re.search(rf'Include="{re.escape(name)}"\s+Version="{re.escape(version)}"',src):
        errors.append(f'Expected dependency not pinned: {name} {version}')

# net462 Core uses HttpClient directly, so the framework reference must be explicit.
if not re.search(r'<Reference\s+Include="System\.Net\.Http"\s*/>', core):
    errors.append('net462 Core is missing explicit System.Net.Http framework reference')

if errors:
    print('STATIC VALIDATION: FAIL')
    for e in errors: print('ERROR:',e)
    for w in warnings: print('WARN:',w)
    sys.exit(1)
print('STATIC VALIDATION: PASS')
for w in warnings: print('WARN:',w)
