namespace CoffeeCommunity.Api.Infrastructure;

public static class CommunityGamesEndpoints
{
    public static void MapCommunityGames(this WebApplication app)
    {
        app.MapGet("/api/demo/games/{personSlug}", async (string personSlug, CommunityGames games, CancellationToken ct) =>
        {
            try { return Results.Ok(await games.JourneyAsync(personSlug, ct)); }
            catch (GraphRequestException error) { return Results.Json(new { message = error.Message }, statusCode: error.Status); }
        });
    }
}
