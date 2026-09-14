#!/usr/bin/env python3
"""Read-only checks of actual local models managed by Aspire (no database writes)."""
import argparse
import json
import math
import urllib.error
import urllib.request

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--embedding-url', required=True, help='Embedding resource URL from aspire describe')
parser.add_argument('--interpret', action='store_true', help='Also exercise the optional local query interpreter')
args = parser.parse_args()


def call(path, body=None, status=200):
    request = urllib.request.Request(
        args.embedding_url.rstrip('/') + path,
        None if body is None else json.dumps(body).encode(),
        {'Content-Type': 'application/json'},
    )
    try:
        with urllib.request.urlopen(request, timeout=300) as response:
            code, data = response.status, json.load(response)
    except urllib.error.HTTPError as error:
        code, data = error.code, error.read().decode()
    assert code == status, (path, code, data)
    return data


models = call('/api/models')
assert models['embedding']['ready'], models
assert models['embedding']['model'] == 'embeddinggemma:300m'
assert models['embedding']['digest'] == '85462619ee721b466c5927d109d4cb765861907d5417b9109caebc4e614679f1'
query = {'text': 'bright fruity floral coffee', 'purpose': 'query'}
first = call('/api/embed', query)
second = call('/api/embed', query)
assert first['provider'] == 'ollama' and first['model'] == models['embedding']['model']
assert first['digest'] == models['embedding']['digest'] and first['dimensions'] == 768
assert max(abs(a - b) for a, b in zip(first['embedding'], second['embedding'])) < 1e-5
texts = [
    'Summer Orchard. Summer orchard: ripe berry nectar, fragrant blossom, delicate bright cup. Origin: Origin 2. Process: natural.',
    'Blueberry Label Dark Roast. Blueberry label collector: dark smoky bitter roast, keyword-only match. Origin: Origin 1. Process: natural.',
]
batch = call('/api/embed/batch', {'texts': texts, 'purpose': 'document'})
assert len(batch['embeddings']) == 2
for vector in [first['embedding'], second['embedding'], *batch['embeddings']]:
    assert len(vector) == 768 and all(math.isfinite(value) for value in vector)
    assert abs(sum(value * value for value in vector) - 1) < 0.001
similarities = [sum(a * b for a, b in zip(first['embedding'], vector)) for vector in batch['embeddings']]
assert similarities[0] > similarities[1] + 0.05, similarities
for path, body in [
    ('/api/embed', {'text': ''}),
    ('/api/embed', {'text': 'coffee', 'purpose': 'invalid'}),
    ('/api/embed', {'text': 'a' * 8001}),
    ('/api/embed/batch', {'texts': []}),
    ('/api/embed/batch', {'texts': ['coffee'] * 65}),
    ('/api/embed/batch', {'texts': ['coffee', '']}),
]:
    call(path, body, status=400)
print(f'PASS pinned real model, finite normalized 768d vectors, repeatability, batches, validation; semantic cosine orchard={similarities[0]:.4f} dark={similarities[1]:.4f}')

if args.interpret:
    assert models['interpreter']['ready'], models
    for invalid in ['', ' ', 'a' * 501]:
        call('/api/interpret', {'text': invalid}, status=400)
    interpreted = call('/api/interpret', {'text': 'I would like a bright fruity floral coffee, without smoky bitterness'})
    assert set(interpreted) == {'query', 'model', 'provider'}, interpreted
    assert interpreted['model'] == 'qwen3:0.6b' and interpreted['provider'] == 'ollama'
    assert 0 < len(interpreted['query'].strip()) <= 300, interpreted
    # Verify useful flavor extraction without fixing stochastic wording or claiming generated facts.
    assert any(word in interpreted['query'].lower() for word in ['fruity', 'fruit', 'floral', 'bright']), interpreted
    print('PASS optional local interpreter returns bounded query only; empty/oversized inputs rejected')
    print(json.dumps(interpreted))
