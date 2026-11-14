#!/usr/bin/env python3
"""Import AetherWorkspace planning issues using GitHub CLI (DRY RUN unless --apply)."""
import argparse, subprocess, json, sys, pathlib, shlex
ROOT=pathlib.Path(__file__).resolve().parents[1]
MAN=json.loads((ROOT/'issues_manifest.json').read_text(encoding='utf-8'))
p=argparse.ArgumentParser();p.add_argument('--repo',required=True,help='OWNER/REPO')
p.add_argument('--apply',action='store_true',help='Explicitly allow remote writes')
a=p.parse_args()
if '/' not in a.repo or a.repo.count('/') != 1:p.error('--repo must be OWNER/REPO')

def cmd(argv):
    print(('APPLY' if a.apply else 'DRY-RUN')+': '+' '.join(shlex.quote(x) for x in argv))
    if a.apply:
        r=subprocess.run(argv, text=True, capture_output=True)
        if r.returncode:
            sys.stderr.write(r.stderr+'\n'); raise SystemExit(r.returncode)
        if r.stdout.strip():print(r.stdout.strip())

labels=sorted({x for item in MAN['issues'] for x in item['labels']})
existing_milestones=set()
existing_issues=set()
if a.apply:
    subprocess.run(['gh','auth','status'],check=True,stdout=subprocess.DEVNULL)
    r=subprocess.run(['gh','api',f'repos/{a.repo}/milestones?state=all','--paginate','--jq','.[].title'],capture_output=True,text=True,check=True)
    existing_milestones={x.strip() for x in r.stdout.splitlines() if x.strip()}
    r=subprocess.run(['gh','issue','list','--repo',a.repo,'--state','all','--limit','1000','--json','title','--jq','.[].title'],capture_output=True,text=True,check=True)
    existing_issues={x.strip() for x in r.stdout.splitlines() if x.strip()}
for mile in MAN['milestones']:
    if mile in existing_milestones:
        print('SKIP existing milestone:',mile)
        continue
    cmd(['gh','api','--method','POST',f'repos/{a.repo}/milestones','-f',f'title={mile}'])
for label in labels:
    cmd(['gh','label','create',label,'--repo',a.repo,'--color','B8CAD9','--force'])
for item in MAN['issues']:
    issue_title=f"[{item['id']}] {item['title']}"
    if issue_title in existing_issues:
        print('SKIP existing issue:',issue_title)
        continue
    argv=['gh','issue','create','--repo',a.repo,'--title',issue_title,'--body-file',str(ROOT/item['file']),'--milestone',item['milestone']]
    for l in item['labels']:argv.extend(['--label',l])
    cmd(argv)
print('NOTE: Stable AW-... dependencies must be linked to the created GitHub issue numbers after import.')
