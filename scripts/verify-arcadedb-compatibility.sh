#!/usr/bin/env bash

set -euo pipefail

readonly ARCADEDB_IMAGE="${ARCADEDB_IMAGE:-arcadedata/arcadedb:26.9.1}"
readonly ARCADEDB_PASSWORD="${ARCADEDB_COMPAT_PASSWORD:-CoffeeDemo_Local_2026!}"
readonly DATABASE="coffee_demo_compat"
readonly CONTAINER_NAME="arcadedb-demo-compat-$$"
readonly VOLUME_NAME="arcadedb-demo-compat-data-$$"

for dependency in docker curl jq; do
  if ! command -v "${dependency}" >/dev/null 2>&1; then
    echo "Missing required command: ${dependency}" >&2
    exit 1
  fi
done

cleanup() {
  docker rm --force "${CONTAINER_NAME}" >/dev/null 2>&1 || true
  docker volume rm "${VOLUME_NAME}" >/dev/null 2>&1 || true
}

trap cleanup EXIT

request() {
  local endpoint="$1"
  local payload="${2:-}"
  local session_id="${3:-}"
  local args=(
    --fail-with-body
    --silent
    --show-error
    --user "root:${ARCADEDB_PASSWORD}"
    --request POST
  )

  if [[ -n "${session_id}" ]]; then
    args+=(--header "arcadedb-session-id: ${session_id}")
  fi

  if [[ -n "${payload}" ]]; then
    args+=(--header "Content-Type: application/json" --data-binary "${payload}")
  fi

  curl "${args[@]}" "${BASE_URL}${endpoint}"
}

statement_payload() {
  local language="$1"
  local statement="$2"
  jq --null-input --compact-output \
    --arg language "${language}" \
    --arg command "${statement}" \
    '{language:$language, command:$command}'
}

statement_with_embedding_payload() {
  local statement="$1"
  local embedding="$2"
  jq --null-input --compact-output \
    --arg command "${statement}" \
    --argjson embedding "${embedding}" \
    '{language:"sql", command:$command, params:{embedding:$embedding}}'
}

command() {
  local language="$1"
  local statement="$2"
  local session_id="${3:-}"
  request "/api/v1/command/${DATABASE}" "$(statement_payload "${language}" "${statement}")" "${session_id}"
}

query() {
  local language="$1"
  local statement="$2"
  request "/api/v1/query/${DATABASE}" "$(statement_payload "${language}" "${statement}")"
}

assert_json() {
  local label="$1"
  local expression="$2"
  local json="$3"

  if ! jq --exit-status "${expression}" >/dev/null <<<"${json}"; then
    echo "FAILED: ${label}" >&2
    jq . <<<"${json}" >&2
    exit 1
  fi

  echo "PASS: ${label}"
}

docker volume create "${VOLUME_NAME}" >/dev/null
docker run --detach \
  --name "${CONTAINER_NAME}" \
  --publish 127.0.0.1::2480 \
  --volume "${VOLUME_NAME}:/home/arcadedb/databases" \
  --env "ARCADEDB_SETTINGS=-Darcadedb.server.rootPassword=${ARCADEDB_PASSWORD} -Darcadedb.server.plugins=Redis:com.arcadedb.redis.RedisProtocolPlugin,MongoDB:com.arcadedb.mongo.MongoDBProtocolPlugin,Postgres:com.arcadedb.postgres.PostgresProtocolPlugin,GremlinServer:com.arcadedb.server.gremlin.GremlinServerPlugin" \
  "${ARCADEDB_IMAGE}" >/dev/null

readonly HTTP_PORT="$(docker port "${CONTAINER_NAME}" 2480/tcp | awk -F: 'NR == 1 { print $NF }')"
readonly BASE_URL="http://127.0.0.1:${HTTP_PORT}"

for attempt in {1..60}; do
  if curl --fail --silent --output /dev/null "${BASE_URL}/api/v1/health"; then
    break
  fi

  if [[ "${attempt}" == "60" ]]; then
    docker logs "${CONTAINER_NAME}" >&2
    echo "FAILED: ArcadeDB did not become healthy" >&2
    exit 1
  fi

  sleep 1
done

server_info="$(curl --fail-with-body --silent --show-error --user "root:${ARCADEDB_PASSWORD}" "${BASE_URL}/api/v1/server")"
assert_json "pinned server version" '.version | startswith("26.9.1")' "${server_info}"
assert_json "required query engines" '(["sql", "cypher", "redis", "graphql", "gremlin", "mongo"] - .languages) | length == 0' "${server_info}"

create_database="$(request "/api/v1/server" "$(jq -nc --arg command "create database ${DATABASE}" '{command:$command}')")"
assert_json "database creation" '.result == "ok"' "${create_database}"

schema=$'CREATE VERTEX TYPE Person;\nCREATE PROPERTY Person.slug STRING;\nCREATE PROPERTY Person.name STRING;\nCREATE INDEX ON Person (slug) UNIQUE_HASH;\nCREATE VERTEX TYPE Coffee;\nCREATE PROPERTY Coffee.slug STRING;\nCREATE PROPERTY Coffee.name STRING;\nCREATE PROPERTY Coffee.description STRING;\nCREATE PROPERTY Coffee.embedding ARRAY_OF_FLOATS;\nCREATE PROPERTY Coffee.coords STRING;\nCREATE INDEX ON Coffee (slug) UNIQUE_HASH;\nCREATE INDEX ON Coffee (description) FULL_TEXT METADATA { "analyzer": "org.apache.lucene.analysis.en.EnglishAnalyzer" };\nCREATE INDEX ON Coffee (embedding) LSM_VECTOR METADATA { dimensions: 768, similarity: \'COSINE\' };\nCREATE INDEX ON Coffee (coords) GEOSPATIAL METADATA { "precision": 9 };\nCREATE EDGE TYPE MET;\nCREATE DOCUMENT TYPE RecipeRevision;\nCREATE PROPERTY RecipeRevision.slug STRING;\nCREATE PROPERTY RecipeRevision.title STRING;\nCREATE PROPERTY RecipeRevision.steps LIST;\nCREATE INDEX ON RecipeRevision (slug) UNIQUE_HASH;\nCREATE TIMESERIES TYPE BrewTelemetry TIMESTAMP ts PRECISION MILLISECOND TAGS (brew_id STRING, method STRING) FIELDS (water_grams DOUBLE, flow_rate DOUBLE, temperature_c DOUBLE) SHARDS 2 RETENTION 7 DAYS COMPACTION_INTERVAL 1 MINUTES;'
schema_result="$(command sqlscript "${schema}")"
assert_json "graph, document, search, vector, geospatial, and time-series schema" '.result[-1].typeName == "BrewTelemetry"' "${schema_result}"

graph_write="$(command cypher "CREATE (maya:Person {slug:'maya-chen', name:'Maya Chen'}), (priya:Person {slug:'priya-nair', name:'Priya Nair'}), (maya)-[:MET {context:'pour-over table'}]->(priya)")"
assert_json "Cypher graph write" '.stats.nodesCreated == 2 and .stats.relationshipsCreated == 1' "${graph_write}"

graph_read="$(query cypher "MATCH (a:Person {slug:'maya-chen'})-[:MET]->(b:Person) RETURN a.name AS attendee, b.name AS met")"
assert_json "Cypher graph traversal" '.result == [{"attendee":"Maya Chen","met":"Priya Nair"}]' "${graph_read}"

readonly PRIMARY_VECTOR="$(jq -nc '[range(0; 768) | if . == 0 then 1.0 else 0.0 end]')"
readonly SECONDARY_VECTOR="$(jq -nc '[range(0; 768) | if . == 1 then 1.0 else 0.0 end]')"
readonly QUERY_VECTOR="$(jq -nc '[range(0; 768) | if . == 0 then 0.95 elif . == 1 then 0.05 else 0.0 end]')"

primary_payload="$(statement_with_embedding_payload "CREATE VERTEX Coffee SET slug = 'ethiopia-blueberry', name = 'Ethiopia Blueberry Bloom', description = 'A bright floral coffee with blueberry and jasmine notes', embedding = :embedding, coords = 'POINT(-83.0458 42.3314)'" "${PRIMARY_VECTOR}")"
primary_coffee="$(request "/api/v1/command/${DATABASE}" "${primary_payload}")"
assert_json "768-dimension vector insert" '.result[0].slug == "ethiopia-blueberry" and (.result[0].embedding | length) == 768' "${primary_coffee}"

secondary_payload="$(statement_with_embedding_payload "CREATE VERTEX Coffee SET slug = 'colombia-caramel', name = 'Colombia Caramel', description = 'A balanced caramel coffee with cocoa sweetness', embedding = :embedding, coords = 'POINT(-83.0460 42.3315)'" "${SECONDARY_VECTOR}")"
request "/api/v1/command/${DATABASE}" "${secondary_payload}" >/dev/null

document_write="$(command sql "INSERT INTO RecipeRevision CONTENT {slug:'v60-blueberry-v1', title:'Blueberry Bloom V60', steps:[{atSeconds:0, waterGrams:60},{atSeconds:45, waterGrams:180}]}")"
assert_json "nested document write" '.result[0].steps | length == 2' "${document_write}"

full_text="$(query sql "SELECT name, \$score AS score FROM Coffee WHERE SEARCH_INDEX('Coffee[description]', 'blueberry') = true ORDER BY \$score DESC")"
assert_json "BM25 full-text search" '.result[0].name == "Ethiopia Blueberry Bloom" and .result[0].score > 0' "${full_text}"

full_text_plan="$(query sql "EXPLAIN SELECT name, \$score AS score FROM Coffee WHERE SEARCH_INDEX('Coffee[description]', 'blueberry') = true ORDER BY \$score DESC")"
assert_json "full-text index query plan" '.explain | contains("FETCH FROM INDEXED FUNCTION") and contains("BM25")' "${full_text_plan}"

vector_payload="$(jq -nc --argjson embedding "${QUERY_VECTOR}" '{language:"sql", command:"SELECT vector.neighbors(\u0027Coffee[embedding]\u0027, :embedding, 2) AS neighbors FROM Coffee LIMIT 1", params:{embedding:$embedding}}')"
vector_search="$(request "/api/v1/query/${DATABASE}" "${vector_payload}")"
assert_json "vector similarity search" '.result[0].neighbors[0].record.slug == "ethiopia-blueberry" and .result[0].neighbors[0].distance < .result[0].neighbors[1].distance' "${vector_search}"

geo_search="$(query sql "SELECT name, geo.distance(coords, geo.geomFromText('POINT(-83.0459 42.3314)'), 'm') AS meters FROM Coffee ORDER BY meters")"
assert_json "geospatial distance ordering" '.result[0].name == "Ethiopia Blueberry Bloom" and .result[0].meters < .result[1].meters' "${geo_search}"

transient_kv="$(command redis $'SET compat:counter 41\nINCR compat:counter\nGET compat:counter')"
assert_json "transient Redis-compatible counter" '.result[0].value == ["OK", 42, 42]' "${transient_kv}"

persistent_kv_write="$(command redis 'HSET RecipeRevision {"slug":"aeropress-cocoa-v1","title":"Cocoa Aeropress","steps":[]}' )"
assert_json "persistent Redis-compatible document write" '.result[0].value == 1' "${persistent_kv_write}"

persistent_kv_read="$(query redis "HGET RecipeRevision[slug] aeropress-cocoa-v1")"
assert_json "persistent indexed key lookup" '.result[0].title == "Cocoa Aeropress"' "${persistent_kv_read}"

curl --fail-with-body --silent --show-error \
  --user "root:${ARCADEDB_PASSWORD}" \
  --header "Content-Type: text/plain" \
  --request POST \
  "${BASE_URL}/api/v1/ts/${DATABASE}/write?precision=ms" \
  --data-binary $'BrewTelemetry,brew_id=brew-compat,method=v60 water_grams=60.0,flow_rate=3.0,temperature_c=93.0 1789401600000\nBrewTelemetry,brew_id=brew-compat,method=v60 water_grams=120.0,flow_rate=4.0,temperature_c=92.5 1789401601000\nBrewTelemetry,brew_id=brew-compat,method=v60 water_grams=180.0,flow_rate=3.5,temperature_c=92.0 1789401602000' \
  >/dev/null

time_series_payload='{"type":"BrewTelemetry","from":1789401600000,"to":1789488000000,"fields":["water_grams","flow_rate","temperature_c"],"tags":{"brew_id":"brew-compat"},"aggregation":{"bucketInterval":1000,"requests":[{"field":"water_grams","type":"AVG","alias":"avg_water"},{"field":"flow_rate","type":"MAX","alias":"max_flow"}]}}'
time_series="$(request "/api/v1/ts/${DATABASE}/query" "${time_series_payload}")"
assert_json "time-series ingestion and aggregation" '.count == 3 and .buckets[1].values == [120.0, 4.0]' "${time_series}"

rollback_headers="$(curl --fail-with-body --silent --show-error --dump-header - --output /dev/null --user "root:${ARCADEDB_PASSWORD}" --request POST "${BASE_URL}/api/v1/begin/${DATABASE}")"
rollback_session="$(awk 'tolower($1) == "arcadedb-session-id:" { gsub("\r", "", $2); print $2 }' <<<"${rollback_headers}")"
command sql "CREATE VERTEX Person SET slug = 'rollback-proof', name = 'Must Not Persist'" "${rollback_session}" >/dev/null
request "/api/v1/rollback/${DATABASE}" "" "${rollback_session}" >/dev/null
rollback_count="$(query sql "SELECT count(*) AS count FROM Person WHERE slug = 'rollback-proof'")"
assert_json "HTTP transaction rollback" '.result[0].count == 0' "${rollback_count}"

commit_headers="$(curl --fail-with-body --silent --show-error --dump-header - --output /dev/null --user "root:${ARCADEDB_PASSWORD}" --request POST "${BASE_URL}/api/v1/begin/${DATABASE}")"
commit_session="$(awk 'tolower($1) == "arcadedb-session-id:" { gsub("\r", "", $2); print $2 }' <<<"${commit_headers}")"
command sql "CREATE VERTEX Person SET slug = 'commit-proof', name = 'Persists'" "${commit_session}" >/dev/null
request "/api/v1/commit/${DATABASE}" "" "${commit_session}" >/dev/null
commit_count="$(query sql "SELECT count(*) AS count FROM Person WHERE slug = 'commit-proof'")"
assert_json "HTTP transaction commit" '.result[0].count == 1' "${commit_count}"

deep_check="$(command sql "CHECK DATABASE DEEP")"
assert_json "deep database integrity check" 'has("error") | not' "${deep_check}"

echo "ArcadeDB compatibility verification passed for ${ARCADEDB_IMAGE}."
