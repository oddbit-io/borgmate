using BorgMate.Infrastructure.Borg;
using BorgMate.Runtime.Operations;
using OneOf;

namespace BorgMate.Runtime.Orchestration;

/// <summary>Helpers for bridging runtime result values to transitional borg result consumers.</summary>
public static class OperationErrorExtensions
{
    /// <summary>Converts an orchestrator result to the existing raw borg result type.</summary>
    public static BorgResult ToBorgResult(this OneOf<BorgExecutionResult, OperationError> result)
    {
        return result.IsT0 ? result.AsT0.Result : result.AsT1.ToBorgResult();
    }

    /// <summary>Converts a runtime error to the existing raw borg result type.</summary>
    public static BorgResult ToBorgResult(this OperationError error)
    {
        return error switch
        {
            OperationError.Cancelled => new BorgResult(-1, "", "", true),
            OperationError.BorgFailure failure => new BorgResult(failure.ExitCode, "", failure.StderrExcerpt),
            OperationError.Validation validation => new BorgResult(-1, "", validation.MessageKey),
            OperationError.Unexpected unexpected => new BorgResult(-1, "", unexpected.Exception.Message),
            _ => new BorgResult(-1, "", error.ToString() ?? string.Empty)
        };
    }

    /// <summary>Converts a failed borg result to a runtime operation error.</summary>
    public static OperationError ToOperationError(this BorgResult result)
    {
        return result.WasCancelled
            ? new OperationError.Cancelled()
            : new OperationError.BorgFailure(result.ErrorCode, result.ExitCode, result.StandardError);
    }

    /// <summary>Returns a displayable message for a runtime error.</summary>
    public static string ToDisplayMessage(this OperationError error)
    {
        return error switch
        {
            OperationError.Cancelled => string.Empty,
            OperationError.BorgFailure failure => new BorgResult(failure.ExitCode, "", failure.StderrExcerpt).ErrorMessage,
            OperationError.Validation validation => Strings.Get(validation.MessageKey),
            OperationError.Unexpected unexpected => unexpected.Exception.Message,
            _ => error.ToString() ?? string.Empty
        };
    }
}
