# Codes, live counters, and nearby coffee

`/demo/lookup?code=badge-0001` resolves Priya Nair through the unique `BadgeLookup.slug` index and follows the stored target link to her passport. `short-blueberry` resolves to Ethiopia Blueberry Bloom and its provenance page. Unknown codes return 404; empty or oversized codes return 400. The inspector shows the actual lookup and index plan. These records persist across API and database-container restarts.

The live tasting counter on `/demo/pulse` uses ArcadeDB's native Redis `GET` and `INCR` commands through its authenticated HTTP command endpoint. Its fixed key is `coffee_demo:brew-connection-2026:live-counter`. Concurrent increments return distinct increasing values. The database server holds this counter in memory: restarting the API preserves its value, while restarting the ArcadeDB container loses it. It is separate from the durable 360-event historical time-series total. Commands are not automatically retried because a lost response could conceal a successful increment; the UI asks you to refresh before retrying an unconfirmed action. Resetting the story database does not restart the server or clear its transient Redis map.

The pinned 26.9.1 TCP plugin was observed creating a separate transient map per connection. A new TCP connection per API call would return 1 repeatedly. The HTTP Redis executor was verified to share its map across requests and API restarts, so it provides the intended demonstration without adding another database or application counter store.

`/demo/map` defaults to latitude 42.3314, longitude −83.0458, radius 100 meters, and the pour-over bar. Native `geo.within` uses the stored boundary and geospatial index to select vendor candidates. Native `geo.distance(..., 'm')` applies the exact radius and sorts by meters, then slug. The graph's `SELLS` edges supply linked coffees and the vendor record supplies availability. The drawn markers use an approximate local projection; the displayed distances come from ArcadeDB.

Great Lakes Coffee Table is first at 0.0 m. Vendor Table 1 is approximately 8.2 m away. A 5-meter radius returns only the first table. All three seeded venue areas currently share the same convention polygon; changing the area selector changes the selected record but does not imply distinct room boundaries. Latitude, longitude, and radius must be finite and in range; the radius is limited to 10,000 meters.

Run the checks with Aspire active:

```sh
python3 scripts/verify-keygeo.py --api-url http://localhost:5130
aspire resource arcadedb restart --non-interactive
aspire wait arcadedb --non-interactive --timeout 30
aspire wait api --non-interactive --timeout 30
python3 scripts/verify-keygeo.py --api-url http://localhost:5130 --after-restart
```

The second invocation must precede any new counter increment. It asserts zero immediately after the database restart while persistent code targets still resolve, then exercises four concurrent increments and verifies lookup/index and location/filter behavior.

References: [ArcadeDB Redis command language](https://docs.arcadedb.com/arcadedb/reference/redis-ql/redis), [ArcadeDB geospatial queries](https://docs.arcadedb.com/arcadedb/how-to/data-modeling/geospatial).
