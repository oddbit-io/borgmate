namespace BorgMate.Runtime.Operations;

public class OperationOk
{
    private OperationOk()
    {
    }

    public static OperationOk Instance { get; } = new();
}
