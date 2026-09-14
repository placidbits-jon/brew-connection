namespace CoffeeCommunity.Api.Infrastructure;

public sealed class ArcadeDbOptions
{
    public const string SectionName = "ArcadeDb";

    public required Uri BaseUrl { get; init; }
    public string Database { get; init; } = "coffee_demo";
    public string UserName { get; init; } = "root";
    public required string Password { get; init; }
}
