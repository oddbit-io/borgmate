using System;

namespace BorgMate.Persistence.Configuration;

public abstract record StoreError
{
    public sealed record Corrupt(string Detail) : StoreError;

    public sealed record Io(Exception Exception) : StoreError;

    public sealed record Unavailable(string Detail) : StoreError;

    public sealed record Validation(string MessageKey) : StoreError;
}