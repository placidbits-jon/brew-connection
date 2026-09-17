#!/usr/bin/env python3
"""Native telemetry golden checks. Run against the Aspire API (worker running)."""
import argparse,json,time,urllib.request,urllib.error,uuid
p=argparse.ArgumentParser();p.add_argument('--api',default='http://localhost:5100');p.add_argument('--retention',action='store_true');a=p.parse_args()
def req(path,data=None,status=200):
 r=urllib.request.Request(a.api+'/api/demo'+path,data=None if data is None else json.dumps(data).encode(),headers={'Content-Type':'application/json'})
 try:
  with urllib.request.urlopen(r,timeout=60) as x: assert x.status==status;return json.load(x)
 except urllib.error.HTTPError as e:
  assert e.code==status,(e.code,e.read());return None
slug='blueberry-bloom-v60'
b=req('/brews/'+slug)
assert len(b['samples'])==60
assert 'FETCH FROM TIMESERIES' in b['indexPlan'] and 'TAGS' in b['indexPlan']
assert b['samples'][30]['targetWaterGrams']==140 and b['samples'][30]['waterDeviation']==-20
assert b['brewer']['slug']=='priya-nair' and b['recipeRevision']['revision']==2
assert b['samples'][30]['flowRate']==12 and b['samples'][30]['deviation']==8
assert all(x['flowRate']==4 for x in b['samples'] if x['second']!=30)
pulse=req('/pulse');assert pulse['sampleCount']==120 and pulse['totalCount']==360
assert [x['count'] for x in pulse['buckets']]==[5,10,15,20,30,40,60,70,50,30,20,10]
assert pulse['percentile95']==7 and pulse['ratePerMinute']==3 and pulse['waterGramsPerSecond']==4
assert [x['averageCount'] for x in pulse['downsampled']]==[1,3,6,2]
assert [x['count'] for x in pulse['downsampled']]==[30,90,180,60]
assert all(x['timestamp']==1789401600000+i*600000 for i,x in enumerate(pulse['buckets']))
for minutes,bucket_count in ((1,120),(5,24),(30,4),(60,2)):
 buckets=req(f'/pulse?bucketMinutes={minutes}')['buckets']
 assert len(buckets)==bucket_count and sum(x['count'] for x in buckets)==360
 assert len({x['count'] for x in buckets})>1
req('/pulse?bucketMinutes=0',status=400);req('/brews/missing',status=404)
run='verify-'+uuid.uuid4().hex[:12]
from concurrent.futures import ThreadPoolExecutor
with ThreadPoolExecutor(max_workers=4) as pool:
 list(pool.map(lambda _: req('/brews/'+slug+'/replay',{'runId':run}),range(4)))
req('/brews/brew-001/replay',{'runId':run},status=409)
req('/brews/'+slug+'/replay',{'runId':'bad,tag'},status=400)
req('/telemetry/runs/'+run+'/ingest',{'samples':[]},status=400)
req('/telemetry/runs/'+run+'/ingest',{'samples':[None]},status=400)
partial={'samples':[{'second':0,'waterGrams':0,'flowRate':4,'temperatureC':93}]}
for _ in range(2):
 progress=req('/telemetry/runs/'+run+'/ingest',partial)
 assert (progress['status']=='complete')==(progress['sampleCount']==60)
 assert 1<=progress['sampleCount']<=60

for _ in range(60):
 b=req('/brews/'+slug+'?runId='+run)
 if b['status']=='complete': break
 time.sleep(1)
assert b['status']=='complete' and len(b['samples'])==60,b
req('/brews/'+slug+'/replay',{'runId':run})
assert len(req('/brews/'+slug+'?runId='+run)['samples'])==60
assert len(req('/brews/'+slug)['samples'])==60
print('Telemetry golden buckets, anomaly, validation and idempotent worker replay passed.')

if a.retention:
 initial=req('/pulse/retention',{})['retention']
 assert initial['beforeCount']==2
 for _ in range(45):
  r=req('/pulse')['retention']
  if r['status']=='complete':break
  time.sleep(2)
 assert r['afterCount']==1,r
 assert len(req('/brews/'+slug)['samples'])==60
 print('Native retention removed the expired sample and preserved authored telemetry.')
