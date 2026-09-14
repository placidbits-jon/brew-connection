#!/usr/bin/env python3
"""Prove idempotent in-place index upgrades preserve records and missing index IDs.
Run against the local Aspire story profile. Leaves its test recipe for inspection.
"""
import argparse, base64, json, os, subprocess, time, urllib.request, urllib.error
p=argparse.ArgumentParser(); p.add_argument('--api-url',required=True); p.add_argument('--db-url',required=True); a=p.parse_args()
auth=base64.b64encode(('root:'+os.getenv('ARCADEDB_PASSWORD','CoffeeDemo_Local_2026!')).encode()).decode()
def api(path,body=None):
    request=urllib.request.Request(a.api_url+'/api/demo/'+path,None if body is None else json.dumps(body).encode(),{'Content-Type':'application/json'})
    with urllib.request.urlopen(request,timeout=1800) as r:return json.load(r)
def db(sql,params=None):
    request=urllib.request.Request(a.db_url+'/api/v1/command/coffee_demo',json.dumps({'language':'sql','command':sql,'params':params}).encode(),{'Content-Type':'application/json','Authorization':'Basic '+auth})
    with urllib.request.urlopen(request,timeout=1800) as r:return json.load(r)['result']
def restart():
    subprocess.run(['aspire','resource','api','restart'],check=True,stdout=subprocess.DEVNULL)
    for _ in range(120):
        try:
            if api('status')['schema']=='ready':return
        except (urllib.error.URLError,KeyError):pass
        time.sleep(.5)
    raise AssertionError('API did not recover after index migration')
source=api('recipes/blueberry-v60')['currentRevision']
revision={k:source[k] for k in ['steps','equipment','grind','temperatureC','coffeeGrams','waterGrams','commentary']}
revision['commentary']='Migration custard fingerprint'
created=api('recipes',{'slug':'migration-preserved-recipe','name':'Migration custard','personSlug':'maya-chen','revision':revision})
db("DELETE FROM SearchEmbedding WHERE subjectSlug='migration-preserved-recipe' AND subjectType='Recipe'")
db("UPDATE RoastBatch SET description=null, searchText='Original bergamot migration source' WHERE slug='roast-batch-0004'")
db("UPDATE EventConfiguration SET searchIndexVersion='old-compatibility', preservationSentinel=true WHERE slug='brew-connection-2026'")
restart()
after=api('recipes/migration-preserved-recipe')
assert after['revisions']==created['revisions']
assert db("SELECT preservationSentinel AS ok FROM EventConfiguration")[0]['ok']
embedding=db("SELECT slug, provider, dimensions, text, embedding FROM SearchEmbedding WHERE subjectSlug='migration-preserved-recipe'")[0]
assert embedding['slug']=='Recipe:migration-preserved-recipe' and embedding['provider']=='ollama' and len(embedding['embedding'])==768
first=db("SELECT searchText, description FROM RoastBatch WHERE slug='roast-batch-0004'")[0]
assert first['description']=='Original bergamot migration source'
db("UPDATE EventConfiguration SET searchIndexVersion='force-second-rebuild' WHERE slug='brew-connection-2026'")
restart()
assert db("SELECT searchText, description FROM RoastBatch WHERE slug='roast-batch-0004'")[0]==first
assert db("SELECT slug, provider, dimensions, text, embedding FROM SearchEmbedding WHERE subjectSlug='migration-preserved-recipe'")[0]==embedding
assert api('recipes/migration-preserved-recipe')['revisions']==created['revisions']
# Force a late unique-index failure, after vertex and immutable revision writes.
db("INSERT INTO SearchEmbedding SET slug='Recipe:rollback-proof', subjectSlug='unrelated-rollback-fixture', subjectType='Recipe'")
try:
    api('recipes',{'slug':'rollback-proof','name':'Rollback proof','personSlug':'maya-chen','revision':revision})
    raise AssertionError('Expected embedding identity conflict')
except urllib.error.HTTPError as e:
    assert e.code==500
assert db("SELECT count(*) AS n FROM Recipe WHERE slug='rollback-proof'")[0]['n']==0
assert db("SELECT count(*) AS n FROM RecipeRevision WHERE slug='rollback-proof-v1'")[0]['n']==0
db("DELETE FROM SearchEmbedding WHERE slug='Recipe:rollback-proof'")
# Availability comes from the current graph; a sold-out favorite loses its boost.
try:
    db("UPDATE VendorTable SET available=false")
    filtered=api('discover?query=blueberry&mode=personalized&availableOnly=true')
    assert filtered['results'] and all(r['type']=='Recipe' and r['available'] for r in filtered['results'])
    unfiltered=api('discover?query=blueberry&mode=personalized')
    favorite=next(r for r in unfiltered['results'] if r['slug']=='roast-batch-0003')
    assert not favorite['available'] and favorite['graphContribution']==0
finally:
    db("UPDATE VendorTable SET available=true")
print('PASS: idempotent index migration preserves revisions/text/vectors; late embedding failure rolls back recipe/revision; availability controls filtering and boost')
