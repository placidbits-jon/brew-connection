#!/usr/bin/env python3
"""Exercise graph commands and queries against the Aspire API; --reset restores story first."""
import argparse
import concurrent.futures
from datetime import datetime, timezone
import json
import urllib.error
import urllib.request

p = argparse.ArgumentParser()
p.add_argument('--api-url', required=True)
p.add_argument('--reset', action='store_true')
a = p.parse_args()

def call(path, body=None, status=200):
    req = urllib.request.Request(a.api_url + '/api/demo/' + path,
        None if body is None else json.dumps(body).encode(), {'Content-Type': 'application/json'})
    try:
        with urllib.request.urlopen(req, timeout=1800) as response:
            code, result = response.status, json.load(response)
    except urllib.error.HTTPError as error:
        code, result = error.code, error.read().decode()
    assert code == status, (path, code, result)
    if code == 200 and path != 'reset':
        assert result['queries'] and all(q['command'] and q['language'] for q in result['queries'])
    return result

if a.reset:
    call('reset', {})
network = call('network/maya-chen?target=luis-ortega')
assert network['paths'][0]['slugs'] == ['maya-chen', 'priya-nair', 'luis-ortega']
assert any(r['slug']=='luis-ortega' and r['score']==10 for r in network['rematches'])
assert any(p['slug'] == 'luis-ortega' and 'fruit-forward' in p['interests'] for p in network['sharedInterests'])
call('passport/missing', status=404)
call('graph/meet', {}, 400)
call('graph/meet', {'personSlug':None,'badgeCode':'badge-0003','context':'chat','location':'bar'}, 400)
call('graph/tastings', {'personSlug':'maya-chen','brewSlug':''}, 400)
call('graph/reconnects', {'personSlug':'maya-chen','targetSlug':None}, 400)
call('network/maya-chen?target=missing', status=404)
call('graph/meet', {'personSlug':'maya-chen','badgeCode':'badge-0000','context':'self','location':'bar'}, 400)
call('graph/meet', {'personSlug':'maya-chen','badgeCode':'short-blueberry','context':'not badge','location':'bar'}, 404)
mutations = [
    ('meet', {'personSlug':'maya-chen','badgeCode':'badge-0003','context':'Integration coffee chat','location':'Pour-over bar'}),
    ('tastings', {'personSlug':'maya-chen','brewSlug':'brew-001'}),
    ('loves', {'personSlug':'maya-chen','brewSlug':'brew-001'}),
    ('reconnects', {'personSlug':'maya-chen','targetSlug':'attendee-0003'}),
    ('game-sessions', {'slug':'integration-game','gameSlug':'coffee-cards','personSlug':'maya-chen','opponentSlug':'priya-nair'}),
    ('game-results', {'sessionSlug':'integration-game','winnerSlug':'priya-nair','loserSlug':'maya-chen','winnerScore':21,'loserScore':17})]
for route, body in mutations:
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        list(pool.map(lambda _: call('graph/' + route, body), range(4)))
passport = call('passport/maya-chen')
seeded_meeting = next(e for e in passport['timeline'] if e['kind']=='MET' and e['slug']=='priya-nair')
assert datetime.fromisoformat(seeded_meeting['occurredAt']) == datetime(2026, 9, 14, 16, 0, tzinfo=timezone.utc)
assert all(e['occurredAt'].endswith('Z') for e in passport['timeline'])
for kind, slug in [('MET','attendee-0003'),('TASTED','brew-001'),('LOVED','brew-001'),('WANTS_TO_RECONNECT','attendee-0003'),('PLAYED_IN','integration-game')]:
    entries = [e for e in passport['timeline'] if e['kind'] == kind and e['slug'] == slug]
    assert len(entries) == 1, (kind, entries)
meeting = next(e for e in passport['timeline'] if e['kind']=='MET' and e['slug']=='attendee-0003')
assert meeting['context']=='Integration coffee chat' and meeting['location']=='Pour-over bar' and meeting['occurredAt']
call('graph/meet', {'personSlug':'attendee-0003','badgeCode':'badge-0000','context':'Repeat reverse scan','location':'bar'})
assert len([e for e in call('passport/maya-chen')['timeline'] if e['kind']=='MET' and e['slug']=='attendee-0003']) == 1
assert any(e['kind']=='MET' and e['slug']=='maya-chen' for e in call('passport/attendee-0003')['timeline'])
loss = next(e for e in passport['timeline'] if e['kind']=='BEAT_IN_GAME' and e.get('sessionSlug')=='integration-game')
assert loss['outcome']=='Lost' and loss['score']==17 and loss['opponentScore']==21
win = next(e for e in call('passport/priya-nair')['timeline'] if e['kind']=='BEAT_IN_GAME' and e.get('sessionSlug')=='integration-game')
assert win['outcome']=='Won' and win['score']==21 and win['opponentScore']==17
network = call('network/maya-chen?target=attendee-0003')
assert network['paths'][0]['slugs'] == ['maya-chen','attendee-0003']
assert len([r for r in network['rematches'] if r['sessionSlug']=='integration-game']) == 1
assert any(r['slug']=='attendee-0003' for r in network['reconnects'])
call('graph/tastings', {'personSlug':'maya-chen','brewSlug':'missing'}, 404)
call('graph/reconnects', {'personSlug':'maya-chen','targetSlug':'maya-chen'}, 400)
call('graph/game-results', dict(mutations[-1][1], winnerScore=12), 400)
call('graph/game-results', dict(mutations[-1][1], winnerSlug='luis-ortega'), 400)
call('graph/game-results', dict(mutations[-1][1], winnerScore=22), 409)
call('graph/game-sessions', dict(mutations[-2][1], opponentSlug='luis-ortega'), 409)
assert call('network/maya-chen?target=attendee-0039')['paths'] == []
# A three-hop path proves this is traversal rather than a direct/two-hop special case.
call('graph/meet', {'personSlug':'luis-ortega','badgeCode':'badge-0004','context':'Graph chain','location':'bar'})
assert call('network/maya-chen?target=attendee-0004')['paths'][0]['slugs'] == ['maya-chen','priya-nair','luis-ortega','attendee-0004']
print('PASS: graph traversal, passports, concurrent idempotence, game validation, and query inspection')
