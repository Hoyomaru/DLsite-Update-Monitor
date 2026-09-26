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

# Update Center UX contract: keep the audited selection/filter behavior explicit.
update_center_xaml = ROOT/'src/DLsiteUpdateMonitor.Plugin/UpdateCenterView.xaml'
update_center_code = ROOT/'src/DLsiteUpdateMonitor.Plugin/UpdateCenterView.xaml.cs'
if update_center_xaml.exists() and update_center_code.exists():
    ux = update_center_xaml.read_text(encoding='utf-8-sig')
    uc = update_center_code.read_text(encoding='utf-8-sig')
    for needle, msg in [
        ('Text="ゲーム名・作品IDを検索"', 'Update Center search needs a visible label'),
        ('Text="表示:"', 'Update Center filter needs a visible label'),
        ('Content="表示を更新"', 'Update Center refresh wording must distinguish local refresh from network recheck'),
        ('Foreground="{DynamicResource TextBrush}"', 'Update Center must inherit Playnite theme text color'),
        ('x:Name="SelectionText"', 'Update Center must show selection count'),
        ('SelectionChanged="ItemsGrid_SelectionChanged"', 'Update Center must react to selection changes'),
        ('Content="エラー・要確認を再チェック"', 'Update Center attention action wording mismatch'),
        ('Header="変更状態"', 'Update Center state column wording mismatch'),
        ('Header="チェック結果"', 'Update Center health column wording mismatch'),
    ]:
        if needle not in ux: errors.append(msg)
    for needle, msg in [
        ('DetailsButton.IsEnabled = single', 'Update Center details must require a single selection'),
        ('OpenPageButton.IsEnabled = single', 'Update Center DLsite open must require a single selection'),
        ('AppliedButton.IsEnabled = hasSelection', 'Update Center acknowledge action must require selection'),
        ('IgnoreButton.IsEnabled = hasSelection', 'Update Center ignore action must require selection'),
    ]:
        if needle not in uc: errors.append(msg)

    plugin_assign = uc.find('this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));')
    init_component = uc.find('InitializeComponent();')
    if plugin_assign < 0 or init_component < 0 or plugin_assign > init_component:
        errors.append('Update Center must assign plugin dependency before InitializeComponent can raise XAML events')

# Automatic-check contract: opt-in, tracked-only, silent, and routed through the shared batch path.
settings_cs = ROOT/'src/DLsiteUpdateMonitor.Plugin/PluginSettings.cs'
settings_xaml = ROOT/'src/DLsiteUpdateMonitor.Plugin/PluginSettingsView.xaml'
plugin_cs = ROOT/'src/DLsiteUpdateMonitor.Plugin/DLsiteUpdateMonitorPlugin.cs'
if settings_cs.exists() and settings_xaml.exists() and plugin_cs.exists():
    st = settings_cs.read_text(encoding='utf-8-sig')
    sx = settings_xaml.read_text(encoding='utf-8-sig')
    pc = plugin_cs.read_text(encoding='utf-8-sig')
    for needle, msg in [
        ('public bool EnableAutomaticChecks { get; set; } = false;', 'Automatic checks must default to OFF'),
        ('public int AutomaticCheckIntervalHours', 'Automatic check interval setting missing'),
        ('自動チェック間隔は1～168時間', 'Automatic check interval validation missing'),
    ]:
        if needle not in st: errors.append(msg)
    for needle, msg in [
        ('<ScrollViewer VerticalScrollBarVisibility="Auto"', 'Automatic-check settings must remain scrollable in Playnite settings'),
        ('Foreground="{DynamicResource TextBrush}"', 'Automatic-check settings must inherit Playnite theme text color'),
        ('自動チェック（既定OFF）', 'Automatic-check section heading missing'),
        ('x:Name="AutomaticCheckBox"', 'Automatic-check opt-in control missing'),
        ('追跡中のゲームを自動チェックする', 'Automatic-check opt-in UI missing'),
        ('自動チェック間隔（時間）', 'Automatic-check interval UI missing'),
        ('IsEnabled="{Binding IsChecked, ElementName=AutomaticCheckBox}"', 'Automatic-check interval must be disabled while automatic checks are OFF'),
        ('既に追跡中のゲームだけが対象です。', 'Tracked-only automatic-check explanation missing'),
    ]:
        if needle not in sx: errors.append(msg)
    for needle, msg in [
        ('ConfigureAutomaticCheckTimer(TimeSpan.FromMinutes(2))', 'Automatic-check startup grace period missing'),
        ('automaticCheckTimer.Interval = TimeSpan.FromMinutes(15)', 'Automatic-check scheduler polling cadence missing'),
        ('record.LastAttemptAtUtc.Value > dueBefore', 'Automatic checks must select only due tracking records'),
        ('var game = PlayniteApi.Database.Games.Get(pair.Key)', 'Automatic checks must resolve only existing Playnite games'),
        ('if (resolution.Status == LinkResolutionStatus.NoDlsiteLink) continue;', 'Automatic scheduler must skip tracked records whose DLsite link was removed'),
        ('RunCheckBatchAsync(', 'Manual/automatic checks must share the batch path'),
        ('Logger.Error(ex, "Automatic DLsite update check failed.")', 'Automatic-check failures must be logged'),
    ]:
        if needle not in pc: errors.append(msg)
    auto_start = pc.find('private async void AutomaticCheckTimer_Tick')
    auto_end = pc.find('private List<Game> GetDueAutomaticCheckGames', auto_start)
    if auto_start >= 0 and auto_end > auto_start:
        auto_body = pc[auto_start:auto_end]
        if 'ShowMessage(' in auto_body or 'ShowErrorMessage(' in auto_body:
            errors.append('Automatic check path must not display modal dialogs')

# Plugin settings discoverability contract.
plugin_cs_path = ROOT/'src/DLsiteUpdateMonitor.Plugin/DLsiteUpdateMonitorPlugin.cs'
if plugin_cs_path.exists():
    pc_settings = plugin_cs_path.read_text(encoding='utf-8-sig')
    for needle, msg in [
        ('Properties = new GenericPluginProperties', 'Plugin must initialize GenericPluginProperties'),
        ('HasSettings = true', 'Plugin must advertise settings to Playnite'),
        ('Description = "設定を開く"', 'Plugin must expose a direct settings menu entry'),
        ('Action = _ => OpenSettingsView()', 'Direct settings menu must open the Playnite plugin settings view'),
    ]:
        if needle not in pc_settings: errors.append(msg)

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
