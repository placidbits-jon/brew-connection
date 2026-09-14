#!/usr/bin/env python3
"""Verify actual HTTP transaction staging, rollback, retry and cross-language identity."""
import argparse, concurrent.futures, json, urllib.request, urllib.error
p=argparse.ArgumentParser(); p.add_argument('--api-url',required=True); a=p.parse_args()
def call(path='',body=None,status=200):
    req=urllib.request.Request(a.api_url+'/api/demo/transactions'+path,data=None if body is None else json.dumps(body).encode(),headers={'Content-Type':'application/json'})
    try:
        with urllib.request.urlopen(req,timeout=120) as r: code,data=r.status,json.load(r)
    except urllib.error.HTTPError as e: code,data=e.code,e.read().decode()
    assert code==status,(code,data)
    return data
for invalid in [{},{'scenario':None},{'scenario':'DELETE FROM Brew'},{'scenario':'commit','command':'DELETE FROM Brew'}]: call('/run',invalid,400)
initial=call()
rolled=call('/run',{'scenario':'rollback'})
assert rolled['outcome']=='rolled-back' and rolled['before']==rolled['after']
assert rolled['staged']['counts']['Brew']==rolled['before']['counts']['Brew']+1
assert rolled['staged']['counts']['BREWED']==rolled['before']['counts']['BREWED']+1
assert not rolled['after']['brew'] and not rolled['after']['note'] and not rolled['after']['edges']
assert len(rolled['staged']['edges'])==2
with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool: results=list(pool.map(lambda _:call('/run',{'scenario':'commit'}),range(4)))
assert all(r['outcome'] in ('committed','already-committed') and r['sameRecord'] for r in results)
current=call(); assert current['outcome']=='committed' and current['sameRecord']
assert len(current['after']['edges'])==4 and len(current['after']['note'])==1
assert next(e for e in current['after']['edges'] if e['type']=='USED_RECIPE')['revisionSlug']=='blueberry-v60-v2'
assert current['after']['note'][0]['subjectRid']==current['sql'][0]['rid'] and current['after']['note'][0]['ownerSlug']=='maya-chen'
assert {(e['type'],e['fromSlug'],e['toSlug']) for e in current['after']['edges']}=={('BREWED','priya-nair','transaction-blueberry-v60'),('USED_BATCH','transaction-blueberry-v60','ethiopia-blueberry-bloom'),('USED_RECIPE','transaction-blueberry-v60','blueberry-v60'),('TASTED','maya-chen','transaction-blueberry-v60')}
assert len(current['sql'])==len(current['cypher'])==1 and current['sql'][0]['rid']==current['cypher'][0]['rid']
assert current['after']['counts']['Brew']==initial['after']['counts']['Brew']+(initial['outcome']=='ready')
changed={'Brew','Note','BREWED','USED_BATCH','USED_RECIPE','TASTED'}
assert all(current['after']['counts'][t]==count+int(initial['outcome']=='ready' and t in changed) for t,count in initial['after']['counts'].items())
again=call('/run',{'scenario':'rollback'}); assert again['before']==again['after'] and again['outcome']=='rolled-back'
assert call()['after']==current['after']
print('PASS: real staged writes, all graph/document counts rollback, concurrent and repeated retries, same SQL/Cypher RID, invalid input.')
