namespace CoffeeCommunity.Api.Infrastructure;

public static class CommunityDocumentsEndpoints
{
    public static void MapCommunityDocuments(this WebApplication app)
    {
        var group = app.MapGroup("/api/demo");
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (GraphRequestException error) { return Results.Json(new { message = error.Message }, statusCode: error.Status); }
        });
        group.MapGet("/recipes/{slug}", (string slug, string? persona, CommunityDocuments docs, CancellationToken ct) => docs.Run(() => docs.Recipe(slug, persona ?? "maya-chen", ct), false, ct));
        group.MapPost("/recipes", (CreateRecipeRequest request, CommunityDocuments docs, CancellationToken ct) => docs.Run(() => docs.Create(request, ct), true, ct));
        group.MapPost("/recipes/{slug}/revisions", (string slug, PublishRecipeRequest request, CommunityDocuments docs, CancellationToken ct) => docs.Run(() => docs.Publish(slug, request, ct), true, ct));
        group.MapGet("/notes", (string? persona, string? subjectType, string? subjectSlug, CommunityDocuments docs, CancellationToken ct) => docs.Run(() => docs.Notes(persona ?? "maya-chen", subjectType, subjectSlug, ct), false, ct));
        group.MapPost("/notes", (CreateNoteRequest request, CommunityDocuments docs, CancellationToken ct) => docs.Run(() => docs.AddNote(request, ct), true, ct));
        group.MapGet("/coffee/{slug}", (string slug, string? persona, CommunityDocuments docs, CancellationToken ct) => docs.Run(() => docs.Coffee(slug, persona ?? "maya-chen", ct), false, ct));
    }
}
