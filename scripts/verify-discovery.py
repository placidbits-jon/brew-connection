#!/usr/bin/env python3
"""Golden retrieval, indexing, publication, validation and reseed checks."""
import argparse, json, urllib.request, urllib.error, urllib.parse
p=argparse.ArgumentParser(); p.add_argument('--api-url',required=True); p.add_argument('--reset',action='store_true'); a=p.parse_args()
def call(path,body=None,status=200):
    request=urllib.request.Request(a.api_url+'/api/demo/'+path,None if body is None else json.dumps(body).encode(),{'Content-Type':'application/json'})
    try:
        with urllib.request.urlopen(request,timeout=1800) as r: code,data=r.status,json.load(r)
    except urllib.error.HTTPError as e: code,data=e.code,e.read().decode()
    assert code==status,(path,code,data)
    return data
def search(mode='keyword',query='blueberry',**options):
    return call('discover?'+urllib.parse.urlencode(dict(query=query,mode=mode,persona='maya-chen',**options)))
def golden():
    modes={m:search(m) for m in ['keyword','semantic','hybrid','personalized']}
    slugs=lambda m:[r['slug'] for r in modes[m]['results']]
    assert 'roast-batch-0001' in slugs('keyword'),slugs('keyword')
    assert 'roast-batch-0002' not in slugs('keyword')
    assert 'roast-batch-0002' in slugs('semantic'),slugs('semantic')
    favorite=next(r for r in modes['personalized']['results'] if r['slug']=='roast-batch-0003')
    assert favorite['graphContribution']>0 and any('Priya' in e and 'LOVED' in e for e in favorite['explanations']),favorite
    assert slugs('personalized').index('roast-batch-0003')<slugs('hybrid').index('roast-batch-0003')
    for mode,data in modes.items():
        assert data['query']=='blueberry' and data['model']['dimensions']==768
        assert len(slugs(mode))==len(set(slugs(mode)))
        for r in data['results']:
            assert abs(r['totalScore']-sum(r[k] for k in ['keywordContribution','vectorContribution','graphContribution']))<1e-6
        assert any('FETCH FROM INDEXED FUNCTION SEARCH_INDEX' in (q.get('plan') or '') and 'SCORING' in q['plan'] for q in data['queries']) if mode!='semantic' else True
        assert any("vector.neighbors('SearchEmbedding[embedding]'" in (q.get('plan') or '') and 'FETCH FROM TYPE' not in q['plan'] for q in data['queries']) if mode!='keyword' else True
    one=search('cross-model',query='stonefruit honey',type='RoastBatch',syntax='plain',availableOnly='true')
    assert [r['slug'] for r in one['results']]==['roast-batch-0003'],one['results']
    assert len(one['queries'])==1,one['queries']
    statement=one['queries'][0]['command']
    assert all(marker in statement for marker in ['vector.neighbors','SEARCH_INDEX','MATCH','available = true']),statement
    assert one['queries'][0].get('plan'),one['queries'][0]
    return modes
if a.reset: call('reset',{})
first=golden()
for syntax,q in [('phrase','blueberry jasmine'),('fuzzy','bluebery'),('stemming','roasted'),('autocomplete','blueb')]:
    assert search(query=q,syntax=syntax)['results'],syntax
assert search(syntax='morelike',similarTo='ethiopia-blueberry-bloom')['results']
assert all(r['type']=='Recipe' for r in search('semantic',type='Recipe')['results'])
assert all(r['available'] for r in search('semantic',availableOnly='true')['results'])
for suffix in ['mode=nope','type=Note','syntax=nope','query=','persona=missing']:
    call('discover?'+suffix,status=404 if suffix.startswith('persona') else 400)
original=call('recipes/blueberry-v60')
revision={k:original['currentRevision'][k] for k in ['steps','equipment','grind','temperatureC','coffeeGrams','waterGrams','commentary']}
revision['commentary']='Quince marmalade publication marker'
slug='discovery-publication-check'
call('recipes',{'slug':slug,'name':'Index publication proof','personSlug':'maya-chen','revision':revision})
assert slug in [r['slug'] for r in search(query='quince')['results']]
assert slug in [r['slug'] for r in search('semantic',query='Quince marmalade publication marker',type='Recipe')['results']]
revision['commentary']='Pistachio nougat replacement marker'
call('recipes/'+slug+'/revisions',{'personSlug':'maya-chen','expectedRevision':1,'revision':revision})
assert slug not in [r['slug'] for r in search(query='quince')['results']]
assert slug in [r['slug'] for r in search(query='pistachio')['results']]
# A valid recipe slug can equal a seeded embedding slug. Index IDs have a type prefix.
revision['commentary']='Hazelnut '+('漢字🙂é arbitrary valid commentary ' * 350)[:7000]
created=call('recipes',{'slug':'embedding-00000','name':'Hazelnut long recipe','personSlug':'maya-chen','revision':revision})
assert len(created['recipe']['searchText'].encode())<=1500
assert 'embedding-00000' in [r['slug'] for r in search(query='hazelnut')['results']]
assert created['currentRevision']['commentary']==revision['commentary']
if a.reset:
    call('reset',{}); second=golden()
    for mode in first:
        assert [r['slug'] for r in first[mode]['results']]==[r['slug'] for r in second[mode]['results']]
        for x,y in zip(first[mode]['results'],second[mode]['results']):
            if x['vectorDistance'] is not None: assert abs(x['vectorDistance']-y['vectorDistance'])<1e-5
print('PASS: discovery golden modes, one-query cross-model proof, full-text examples, filters, validation, atomic publication and reproducibility')
