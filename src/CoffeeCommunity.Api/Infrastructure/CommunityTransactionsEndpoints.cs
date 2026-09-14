using System.Text.Json;

namespace CoffeeCommunity.Api.Infrastructure;

public static class CommunityTransactionsEndpoints
{
    public static void MapCommunityTransactions(this WebApplication app)
    {
        var group = app.MapGroup("/api/demo/transactions");
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (GraphRequestException error) { return Results.Json(new { message = error.Message }, statusCode: error.Status); }
        });
        group.MapGet("", (CommunityTransactions transactions, CancellationToken ct) => transactions.Execute(null, ct));
        group.MapPost("/run", (JsonElement body, CommunityTransactions transactions, CancellationToken ct) =>
        {
            if (body.ValueKind != JsonValueKind.Object || body.EnumerateObject().Count() != 1 ||
                !body.TryGetProperty("scenario", out var value) || value.ValueKind != JsonValueKind.String ||
                value.GetString() is not ("commit" or "rollback"))
                throw new GraphRequestException(400, "Choose exactly one authored scenario: commit or rollback.");
            return transactions.Execute(value.GetString(), ct);
        });
    }
}
