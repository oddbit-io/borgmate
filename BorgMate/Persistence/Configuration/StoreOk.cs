namespace BorgMate.Persistence.Configuration;

public sealed record StoreOk
{
    private StoreOk()
    {
    }

    public static StoreOk Instance { get; } = new();
}