#!/usr/bin/env python3
"""Verify document publication, privacy, and historical provenance against Aspire."""
import argparse
import concurrent.futures
import copy
import json
import urllib.request
import urllib.error
p = argparse.ArgumentParser()
p.add_argument('--api-url', required=True)
p.add_argument('--reset', action='store_true')
a = p.parse_args()

def call(path, body=None, status=200):
    req = urllib.request.Request(a.api_url + '/api/demo/' + path, None if body is None else json.dumps(body).encode(), {'Content-Type': 'application/json'})
    try:
        with urllib.request.urlopen(req, timeout=1800) as r:
            code, data = (r.status, json.load(r))
    except urllib.error.HTTPError as e:
        code, data = (e.code, e.read().decode())
    assert code in (status if isinstance(status, tuple) else (status,)), (path, code, data)
    if code == 200 and (not path.startswith('reset')):
        assert data['queries'] and all((q['command'] for q in data['queries']))
    return data
if a.reset:
    call('reset', {})
original = call('recipes/blueberry-v60?persona=maya-chen')
assert original['currentRevision']['revision'] == 2
old = copy.deepcopy(original['revisions'])
fields = ['steps', 'equipment', 'grind', 'temperatureC', 'coffeeGrams', 'waterGrams', 'commentary']
revision = {k: original['currentRevision'][k] for k in fields}
revision['grind']['clicks'] = 18
revision['commentary'] = 'Integration sweeter finish'
request = {'personSlug': 'maya-chen', 'expectedRevision': 2, 'revision': revision}
with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
    results = list(pool.map(lambda _: call('recipes/blueberry-v60/revisions', request), range(4)))
assert all((r['currentRevision']['revision'] == 3 and len(r['revisions']) == 3 for r in results))
after = call('recipes/blueberry-v60')
assert [r for r in after['revisions'] if r['revision'] <= 2] == old
conflict = copy.deepcopy(request)
conflict['revision']['commentary'] = 'conflicting content'
call('recipes/blueberry-v60/revisions', conflict, 409)
call('recipes/blueberry-v60/revisions', {'personSlug': 'maya-chen', 'expectedRevision': 50, 'revision': revision}, 409)
call('recipes/blueberry-v60/revisions', {}, 400)
for field in ['steps', 'equipment', 'grind']:
    invalid = copy.deepcopy(request)
    invalid['revision'][field] = None
    call('recipes/blueberry-v60/revisions', invalid, 400)
invalid = copy.deepcopy(request)
invalid['revision']['steps'] = [None]
call('recipes/blueberry-v60/revisions', invalid, 400)
new = call('recipes', {'personSlug': 'priya-nair', 'slug': 'integration-recipe', 'name': 'Integration recipe', 'revision': revision})
assert new['currentRevision']['revision'] == 1
call('recipes', {'personSlug': 'priya-nair', 'slug': 'integration-recipe', 'name': 'Other recipe', 'revision': revision}, 409)

# Two competing publishers must produce one winner and one conflict, never two v2 documents.
competing = []
for commentary in ['First proposed revision', 'Second proposed revision']:
    document = copy.deepcopy(revision)
    document['commentary'] = commentary
    competing.append({'personSlug': 'priya-nair', 'expectedRevision': 1, 'revision': document})
with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
    outcomes = list(pool.map(lambda body: call('recipes/integration-recipe/revisions', body, (200, 409)), competing))
assert sum(isinstance(outcome, dict) for outcome in outcomes) == 1
created = call('recipes/integration-recipe')
assert len(created['revisions']) == 2 and created['currentRevision']['revision'] == 2
assert created['revisions'][0] == new['revisions'][0]

for kind, slug in [('Person', 'priya-nair'), ('Brew', 'blueberry-bloom-v60'), ('RoastBatch', 'ethiopia-blueberry-bloom'), ('Recipe', 'blueberry-v60'), ('GameSession', 'maya-luis-rematch')]:
    for visibility in ['private', 'public']:
        call('notes', {'slug': f'integration-{kind}-{visibility}', 'personSlug': 'maya-chen', 'subjectType': kind, 'subjectSlug': slug, 'visibility': visibility, 'body': f'Integration {visibility} secret for {kind}: <b>plain text</b>'})
    path = f'notes?subjectType={kind}&subjectSlug={slug}&persona='
    own = call(path + 'maya-chen')['notes']
    other = call(path + 'priya-nair')['notes']
    assert any((n['slug'] == f'integration-{kind}-private' for n in own))
    assert all((n['slug'] != f'integration-{kind}-private' for n in other))
    assert any((n['slug'] == f'integration-{kind}-public' for n in other))
    assert all((n['subjectType'] == kind and n['subjectSlug'] == slug for n in own))
    assert f'Integration private secret for {kind}' not in json.dumps(call(path + 'priya-nair'))
    note = next((n for n in own if n['slug'] == f'integration-{kind}-private'))
    assert note['owner'] and note['subject'] and (note['searchText'] == '')
for route in ['recipes/blueberry-v60', 'coffee/ethiopia-blueberry-bloom']:
    foreign = json.dumps(call(route + '?persona=priya-nair'))
    assert 'Integration private secret' not in foreign
    assert "Priya's blueberry brew: ask for" not in foreign
call('notes', {'slug': 'bad', 'personSlug': 'maya-chen', 'subjectType': 'Organization', 'subjectSlug': 'roaster-0', 'visibility': 'public', 'body': 'no'}, 400)
call('notes', {'slug': 'bad', 'personSlug': 'maya-chen', 'subjectType': 'Person', 'subjectSlug': 'missing', 'visibility': 'private', 'body': 'no'}, 404)
call('notes?persona=missing', status=404)
provenance = call('coffee/ethiopia-blueberry-bloom')
assert provenance['lot']['slug'] == 'lot-0000' and provenance['roaster']['slug'] == 'roaster-0' and (provenance['vendor']['slug'] == 'vendor-0')
brew = next((b for b in provenance['brews'] if b['brew']['slug'] == 'blueberry-bloom-v60'))
assert brew['revision']['revision'] == 2 and brew['recipe']['slug'] == 'blueberry-v60'
assert brew['brewer']['slug'] == 'priya-nair' and brew['reactions']
assert provenance['batch']['scenario'] == 'provenance' and provenance['roastProfile']
print('Document verification passed: immutable revisions, concurrent retries/conflicts, all note links/privacy, pinned provenance.')
