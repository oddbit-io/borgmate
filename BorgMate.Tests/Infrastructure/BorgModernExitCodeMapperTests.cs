using BorgMate.Infrastructure.Borg;

namespace BorgMate.Tests.Infrastructure;

public class BorgModernExitCodeMapperTests
{
    public static IEnumerable<object[]> AllModernCodes =>
    [
        [1, BorgModernExitCode.Warning],
        [2, BorgModernExitCode.Error],
        [3, BorgModernExitCode.CancelledByUser],
        [4, BorgModernExitCode.CommandError],
        [5, BorgModernExitCode.PlaceholderError],
        [6, BorgModernExitCode.InvalidPlaceholder],
        [10, BorgModernExitCode.RepositoryAlreadyExists],
        [11, BorgModernExitCode.AtticRepository],
        [12, BorgModernExitCode.CheckNeeded],
        [13, BorgModernExitCode.RepositoryDoesNotExist],
        [14, BorgModernExitCode.InsufficientFreeSpaceError],
        [15, BorgModernExitCode.InvalidRepository],
        [16, BorgModernExitCode.InvalidRepositoryConfig],
        [17, BorgModernExitCode.ObjectNotFound],
        [18, BorgModernExitCode.ParentPathDoesNotExist],
        [19, BorgModernExitCode.PathAlreadyExists],
        [20, BorgModernExitCode.StorageQuotaExceeded],
        [21, BorgModernExitCode.PathPermissionDenied],
        [25, BorgModernExitCode.MandatoryFeatureUnsupported],
        [26, BorgModernExitCode.NoManifestError],
        [27, BorgModernExitCode.UnsupportedManifestError],
        [32, BorgModernExitCode.IncompatibleFilesystemEncodingError],
        [40, BorgModernExitCode.KeyfileInvalidError],
        [41, BorgModernExitCode.KeyfileMismatchError],
        [42, BorgModernExitCode.KeyfileNotFoundError],
        [43, BorgModernExitCode.NotABorgKeyFile],
        [44, BorgModernExitCode.RepoKeyNotFoundError],
        [45, BorgModernExitCode.RepoIdMismatch],
        [46, BorgModernExitCode.UnencryptedRepo],
        [47, BorgModernExitCode.UnknownKeyType],
        [48, BorgModernExitCode.UnsupportedPayloadError],
        [49, BorgModernExitCode.UnsupportedKeyFormatError],
        [50, BorgModernExitCode.NoPassphraseFailure],
        [51, BorgModernExitCode.PasscommandFailure],
        [52, BorgModernExitCode.PassphraseWrong],
        [53, BorgModernExitCode.PasswordRetriesExceeded],
        [60, BorgModernExitCode.CacheInitAbortedError],
        [61, BorgModernExitCode.EncryptionMethodMismatch],
        [62, BorgModernExitCode.RepositoryAccessAborted],
        [63, BorgModernExitCode.RepositoryIDNotUnique],
        [64, BorgModernExitCode.RepositoryReplay],
        [70, BorgModernExitCode.LockError],
        [71, BorgModernExitCode.LockErrorT],
        [72, BorgModernExitCode.LockFailed],
        [73, BorgModernExitCode.LockTimeout],
        [74, BorgModernExitCode.NotLocked],
        [75, BorgModernExitCode.NotMyLock],
        [80, BorgModernExitCode.ConnectionClosed],
        [81, BorgModernExitCode.ConnectionClosedWithHint],
        [82, BorgModernExitCode.InvalidRPCMethod],
        [83, BorgModernExitCode.PathNotAllowed],
        [84, BorgModernExitCode.RPCServerOutdated],
        [85, BorgModernExitCode.UnexpectedRPCDataFormatFromClient],
        [86, BorgModernExitCode.UnexpectedRPCDataFormatFromServer],
        [87, BorgModernExitCode.ConnectionBrokenWithHint],
        [90, BorgModernExitCode.IntegrityError],
        [91, BorgModernExitCode.FileIntegrityError],
        [92, BorgModernExitCode.DecompressionError],
        [95, BorgModernExitCode.ArchiveTAMInvalid],
        [96, BorgModernExitCode.ArchiveTAMRequiredError],
        [97, BorgModernExitCode.TAMInvalid],
        [98, BorgModernExitCode.TAMRequiredError],
        [99, BorgModernExitCode.TAMUnsupportedSuiteError],
        [100, BorgModernExitCode.FileChangedWarning],
        [101, BorgModernExitCode.IncludePatternNeverMatchedWarning],
        [102, BorgModernExitCode.BackupError],
        [103, BorgModernExitCode.BackupRaceConditionError],
        [104, BorgModernExitCode.BackupOSError],
        [105, BorgModernExitCode.BackupPermissionError],
        [106, BorgModernExitCode.BackupIOError],
        [107, BorgModernExitCode.BackupFileNotFoundError]
    ];

    [Theory]
    [MemberData(nameof(AllModernCodes))]
    public void TryMap_ReturnsExactModernCode(int exitCode, BorgModernExitCode expected)
    {
        Assert.Equal(expected, BorgModernExitCodeMapper.TryMap(exitCode));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(22)]
    [InlineData(88)]
    [InlineData(108)]
    [InlineData(128)]
    public void TryMap_ReturnsNull_WhenCodeIsNotBorgModernExitCode(int exitCode)
    {
        Assert.Null(BorgModernExitCodeMapper.TryMap(exitCode));
    }

    [Theory]
    [MemberData(nameof(AllModernCodes))]
    public void ExactModernCode_HasLocalizedMessage(int exitCode, BorgModernExitCode code)
    {
        var key = $"Error.BorgModernExitCode.{code}";
        var message = BorgErrorMessages.GetMessage(code);

        Assert.Equal(exitCode, (int)code);
        Assert.NotEqual(key, message);
    }

    [Theory]
    [InlineData(BorgModernExitCode.NoPassphraseFailure)]
    [InlineData(BorgModernExitCode.PasscommandFailure)]
    [InlineData(BorgModernExitCode.PassphraseWrong)]
    [InlineData(BorgModernExitCode.PasswordRetriesExceeded)]
    public void IsWrongPassphrase_True(BorgModernExitCode code)
    {
        Assert.True(BorgModernExitCodeMapper.IsWrongPassphrase(code));
    }

    [Theory]
    [InlineData(BorgModernExitCode.Warning)]
    [InlineData(BorgModernExitCode.Error)]
    [InlineData(BorgModernExitCode.RepositoryDoesNotExist)]
    [InlineData(BorgModernExitCode.ConnectionClosed)]
    public void IsWrongPassphrase_False(BorgModernExitCode code)
    {
        Assert.False(BorgModernExitCodeMapper.IsWrongPassphrase(code));
    }

    [Theory]
    [InlineData(BorgModernExitCode.LockError)]
    [InlineData(BorgModernExitCode.LockErrorT)]
    [InlineData(BorgModernExitCode.LockFailed)]
    [InlineData(BorgModernExitCode.LockTimeout)]
    [InlineData(BorgModernExitCode.NotLocked)]
    [InlineData(BorgModernExitCode.NotMyLock)]
    [InlineData(BorgModernExitCode.ConnectionClosed)]
    [InlineData(BorgModernExitCode.ConnectionClosedWithHint)]
    [InlineData(BorgModernExitCode.ConnectionBrokenWithHint)]
    public void IsRetryable_True(BorgModernExitCode code)
    {
        Assert.True(BorgModernExitCodeMapper.IsRetryable(code));
    }

    [Theory]
    [InlineData(BorgModernExitCode.Warning)]
    [InlineData(BorgModernExitCode.Error)]
    [InlineData(BorgModernExitCode.PassphraseWrong)]
    [InlineData(BorgModernExitCode.RepositoryDoesNotExist)]
    public void IsRetryable_False(BorgModernExitCode code)
    {
        Assert.False(BorgModernExitCodeMapper.IsRetryable(code));
    }
}
