using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddOpenApi();
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapPost("/api/embed", (EmbeddingRequest request) =>
{
    const int dimensions = 768;
    var vector = new float[dimensions];
    foreach (Match match in Regex.Matches(request.Text.ToLowerInvariant(), "[a-z0-9]+"))
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(match.Value));
        var index = BitConverter.ToUInt32(hash, 0) % dimensions;
        vector[index] += (hash[4] & 1) == 0 ? 1 : -1;
    }

    var magnitude = MathF.Sqrt(vector.Sum(value => value * value));
    if (magnitude > 0)
    {
        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] /= magnitude;
        }
    }

    return Results.Ok(new
    {
        embedding = vector,
        dimensions,
        provider = "deterministic-compatibility",
        targetModel = "embeddinggemma"
    });
});

app.MapDefaultEndpoints();
app.Run();

public sealed record EmbeddingRequest(string Text);
