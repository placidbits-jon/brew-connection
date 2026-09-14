#!/usr/bin/env python3
"""Check read-only slide APIs and ensure invalid reset input cannot replace data."""
import argparse
import json
import urllib.error
import urllib.request

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--api-url', default='http://localhost:4200')
args = parser.parse_args()


def request(path, method='GET'):
    with urllib.request.urlopen(urllib.request.Request(args.api_url + path, method=method), timeout=30) as response:
        return json.load(response)


def expect_error(path, status, method='GET'):
    try:
        request(path, method)
    except urllib.error.HTTPError as error:
        assert error.code == status, (path, error.code)
    else:
        raise AssertionError(f'{path} should return {status}')


maya = request('/api/demo/story?persona=maya')
assert maya['persona']['slug'] == 'maya-chen'
assert any(person['slug'] == 'priya-nair' for person in maya['connections'])
assert any(brew['slug'] == 'blueberry-bloom-v60' for brew in maya['tastings'])
assert any(person['slug'] == 'luis-ortega' for person in maya['rematches'])
priya = request('/api/demo/story?persona=priya-nair')
assert {person['slug'] for person in priya['connections']} == {'maya-chen', 'luis-ortega'}
luis = request('/api/demo/story?persona=luis')
assert {person['slug'] for person in luis['connections']} == {'priya-nair'}
expect_error('/api/demo/story?persona=missing-persona', 404)
expect_error('/api/demo/reset?profile=invalid', 400, 'POST')
assert request('/api/demo/story?persona=maya') == maya
schema = request('/api/demo/schema')
assert any(record['name'] == 'Person' and record['type'] == 'vertex' for record in schema['types'])
assert any(index['name'] == 'Person[slug]' and index['unique'] for index in schema['indexes'])
assert len({index['name'] for index in schema['indexes']}) == len(schema['indexes'])
print('PASS: slide APIs, both meeting participants, missing persona, invalid reset preserves story, live schema')
