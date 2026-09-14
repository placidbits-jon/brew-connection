#!/usr/bin/env python3
"""Read-only query lab integration verification; run against a ready seeded API."""
import argparse
import json
import urllib.error
import urllib.request

parser = argparse.ArgumentParser()
parser.add_argument('base_url', nargs='?', default='http://localhost:5000')
args = parser.parse_args()
base = args.base_url.rstrip('/')

def request(path, payload=None, raw=None):
    data = raw.encode() if raw is not None else json.dumps(payload).encode() if payload is not None else None
    req = urllib.request.Request(base + path, data=data, headers={'Content-Type': 'application/json'})
    try:
        with urllib.request.urlopen(req, timeout=180) as response:
            return response.status, json.load(response)
    except urllib.error.HTTPError as error:
        return error.code, error.read().decode()

def run(identifier):
    status, result = request('/api/demo/lab/run', {'id': identifier})
    assert status == 200, (identifier, status, result)
    assert result['executionMs'] >= 0
    assert result['recordCount'] == len(result['records'])
    return result

status, catalog = request('/api/demo/lab')
assert status == 200, ('catalog endpoint', status, catalog)
ids = [example['id'] for example in catalog['examples']]
assert len(ids) == len(set(ids))
assert {'sql-brew', 'cypher-brew', 'redis-counter', 'fulltext-coffee', 'vector-coffee', 'timeseries-brew', 'geo-vendors', 'compatibility-summary'} <= set(ids)
before_status, before = request('/api/demo/schema')
assert before_status == 200
before_counts = {item['name']: item['records'] for item in before['types']}
counter = run('redis-counter')['records']
results = {identifier: run(identifier) for identifier in ids}
for identifier, result in results.items():
    if 'transaction-brew' in identifier:
        assert result['recordCount'] <= 1
        if not result['records']:
            assert result['emptyMessage']
    elif identifier != 'redis-counter':
        assert result['records'], (identifier, result)
    if result['language'] == 'sql':
        assert result['plan']['status'] == 'available' and result['plan']['text']
assert results['sql-brew']['records'][0]['rid'] == results['cypher-brew']['records'][0]['rid']
assert results['sql-transaction-brew']['records'] == results['cypher-transaction-brew']['records'], 'Transaction SQL/Cypher identity differs'
assert len(results['vector-coffee']['parameters']['embedding']) == 768
assert len(results['compatibility-summary']['checks']) == 7
for payload in [
    {'id': 'missing'}, {'id': "sql-brew; DELETE FROM Brew"},
    {'id': 'sql-brew', 'command': 'DELETE FROM Brew'},
    {'id': 'redis-counter', 'language': 'redis', 'command': 'INCR injected'},
    {'id': 'sql-brew', 'parameters': {'slug': "x' OR 1=1 --"}},
    {'command': 'DELETE FROM Brew'}, {'id': None}, [], 'DELETE FROM Brew',
]:
    status, body = request('/api/demo/lab/run', payload)
    assert status == 400, (payload, status, body)
for raw in ['{"id":"sql-brew","id":"redis-counter"}', '{', '{"ID":"sql-brew"}', json.dumps({'id': 'x' * 1025})]:
    assert request('/api/demo/lab/run', raw=raw)[0] == 400
assert request('/api/demo/lab/run?command=DELETE', {'id': 'sql-brew'})[0] == 400
assert run('redis-counter')['records'] == counter, 'Lab mutated the Redis counter'
after_status, after = request('/api/demo/schema')
assert after_status == 200
assert {item['name']: item['records'] for item in after['types']} == before_counts, 'Lab changed record counts'
print(f'Query lab verified: {len(ids)} examples, shared SQL/Cypher RID, actual plans, strict request rejection, unchanged counter and schema record counts.')
