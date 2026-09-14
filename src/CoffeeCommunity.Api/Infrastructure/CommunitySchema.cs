using System.Text;

namespace CoffeeCommunity.Api.Infrastructure;

public static class CommunitySchema
{
    public const string Version = "002-community";
    public static readonly string[] Vertices = ["Person", "Organization", "Event", "VenueArea", "VendorTable", "CoffeeLot", "RoastBatch", "Recipe", "Brew", "Game", "GameSession"];
    public static readonly string[] Edges = ["ATTENDED", "MET", "WANTS_TO_RECONNECT", "MEMBER_OF", "ROASTED", "SELLS", "FROM_LOT", "BREWED", "USED_BATCH", "USED_RECIPE", "TASTED", "LOVED", "LIKED_RECIPE", "PLAYED_IN", "BEAT_IN_GAME"];
    public static string Build()
    {
        var sql = new StringBuilder();
        void Add(string statement) => sql.AppendLine(statement + ";");
        void Property(string type, string name, string dataType) => Add($"CREATE PROPERTY {type}.{name} {dataType}");
        foreach (var type in Vertices.Concat(new[] { "RecipeRevision", "Note", "RoastProfile", "EventConfiguration", "BadgeLookup", "SearchEmbedding", "SchemaMigration" }))
        {
            Add($"CREATE {(Vertices.Contains(type) ? "VERTEX" : "DOCUMENT")} TYPE {type}");
            Property(type, "slug", "STRING");
            Add($"CREATE INDEX ON {type} (slug) UNIQUE_HASH");
            Property(type, "name", "STRING");
        }
        Property("SchemaMigration", "id", "STRING");
        Add("CREATE INDEX ON SchemaMigration (id) UNIQUE_HASH");
        foreach (var edge in Edges)
        {
            Add($"CREATE EDGE TYPE {edge}");
            Property(edge, "occurredAt", "DATETIME");
            Property(edge, "context", "STRING");
            Property(edge, "location", "STRING");
        }
        foreach (var type in new[] { "Recipe", "RoastBatch", "CoffeeLot", "Note", "VendorTable", "VenueArea", "RoastProfile" })
        {
            Property(type, "searchText", "STRING");
            Add($"CREATE INDEX ON {type} (searchText) FULL_TEXT METADATA {{ \"analyzer\": \"org.apache.lucene.analysis.en.EnglishAnalyzer\" }}");
        }
        foreach (var type in new[] { "VenueArea", "VendorTable" })
        {
            Property(type, "coords", "STRING");
            Add($"CREATE INDEX ON {type} (coords) GEOSPATIAL METADATA {{ \"precision\": 9 }}");
        }
        foreach (var (type, name) in new[] { ("Recipe", "currentRevision"), ("RecipeRevision", "author"), ("RecipeRevision", "recipe"), ("Note", "owner"), ("Note", "subject"), ("RoastProfile", "roastBatch"), ("BadgeLookup", "target"), ("SearchEmbedding", "subject") })
            Property(type, name, "LINK");
        Property("USED_RECIPE", "revision", "LINK");
        Property("GameSession", "game", "LINK");
        Property("GameSession", "area", "LINK");
        Property("RecipeRevision", "steps", "LIST");
        Property("RecipeRevision", "revision", "INTEGER");
        Property("Note", "visibility", "STRING");
        Property("Note", "ownerSlug", "STRING");
        Add("CREATE INDEX ON Note (ownerSlug, visibility) NOTUNIQUE_HASH");
        Property("Brew", "startedAt", "DATETIME");
        Add("CREATE INDEX ON Brew (startedAt) NOTUNIQUE");
        Property("SearchEmbedding", "embedding", "ARRAY_OF_FLOATS");
        Add("CREATE INDEX ON SearchEmbedding (embedding) LSM_VECTOR METADATA { dimensions: 768, similarity: 'COSINE' }");
        Add("CREATE TIMESERIES TYPE BrewTelemetry TIMESTAMP ts PRECISION MILLISECOND TAGS (brew_id STRING, brewer_id STRING, device STRING, method STRING) FIELDS (water_grams DOUBLE, flow_rate DOUBLE, temperature_c DOUBLE) SHARDS 4");
        Add("CREATE TIMESERIES TYPE EventActivity TIMESTAMP ts PRECISION MILLISECOND TAGS (event_id STRING, area STRING, kind STRING) FIELDS (count DOUBLE, duration DOUBLE) SHARDS 2");
        Add($"INSERT INTO SchemaMigration SET slug = '{Version}', id = '{Version}', appliedAt = sysdate()");
        return sql.ToString();
    }
}
