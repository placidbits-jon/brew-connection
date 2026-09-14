#!/usr/bin/env python3
"""Check Phase 6 key/value + geospatial contracts. Restart via Aspire separately, then --after-restart."""
import argparse, concurrent.futures, json, urllib.request, urllib.error
p = argparse.ArgumentParser(); p.add_argument('--api-url', required=True); p.add_argument('--after-restart', action='store_true'); a = p.parse_args()
def req(path, method='GET', status=200):
    try:
        with urllib.request.urlopen(urllib.request.Request(a.api_url + '/api/demo/' + path, method=method), timeout=30) as r:
            assert r.status == status; return json.load(r)
    except urllib.error.HTTPError as e:
        assert e.code == status, (path, e.code, e.read()); return None
badge = req('lookup?code=badge-0001'); assert badge['target']['slug'] == 'priya-nair' and badge['persistent']
assert 'INDEX' in str(badge['queries']).upper()
assert req('lookup?code=badge-0001')['target'] == badge['target']
req('lookup?code=%27%20OR%201%3D1', status=404)
coffee = req('lookup?code=short-blueberry'); assert coffee['target']['slug'] == 'ethiopia-blueberry-bloom'
assert coffee['target']['route'] == '/demo/coffee/ethiopia-blueberry-bloom'
req('lookup?code=missing-code', status=404); req('lookup?code=', status=400)
counter = req('counter'); assert counter['transient'] and counter['event']['slug'] == 'brew-connection-2026'
if a.after_restart: assert counter['value'] == 0, counter
with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
    values = list(pool.map(lambda _: req('counter', 'POST')['value'], range(4)))
assert sorted(values) == list(range(counter['value']+1, counter['value']+5)), values
m = req('map'); assert m['results'][0]['slug'] == 'vendor-0' and m['results'][0]['distanceMeters'] == 0
assert 'FETCH FROM INDEXED FUNCTION' in str(m['queries'])
assert abs(m['results'][1]['distanceMeters'] - 8.22022006) < 0.00001
assert len(m['results']) == 8 and all(v['contained'] for v in m['results'])
assert any(c['slug'] == 'ethiopia-blueberry-bloom' for c in m['results'][0]['coffees'])
assert [v['distanceMeters'] for v in m['results']] == sorted(v['distanceMeters'] for v in m['results'])
assert len(req('map?radius=1')['results']) == 1
assert req('map?latitude=0&longitude=0&radius=1')['results'] == []
req('map?latitude=NaN', status=400); req('map?latitude=91', status=400); req('map?longitude=181', status=400)
req('map?radius=-1', status=400); req('map?radius=Infinity', status=400); req('map?area=missing', status=404)
print('Key/value and geospatial checks passed' + ('; restart reset and durable lookup verified' if a.after_restart else ''))
