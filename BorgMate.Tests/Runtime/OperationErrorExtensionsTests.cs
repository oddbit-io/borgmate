using BorgMate.Infrastructure.Borg;
using BorgMate.Runtime.Orchestration;

namespace BorgMate.Tests.Runtime;

public class OperationErrorExtensionsTests
{
    [Fact]
    public void WslFailure_PreservesDiagnosticThroughRuntimeAndJobConversions()
    {
        const string diagnostic = "Error code: Wsl/Service/WSL_E_DISTRO_NOT_FOUND";
        var result = new BorgResult(-1, diagnostic, "");

        var error = result.ToOperationError();
        var jobResult = error.ToBorgResult();

        Assert.Equal(diagnostic, error.ToDisplayMessage());
        Assert.Equal(diagnostic, jobResult.ErrorMessage);
        Assert.Equal(diagnostic, jobResult.ToOperationError().ToDisplayMessage());
    }
}
