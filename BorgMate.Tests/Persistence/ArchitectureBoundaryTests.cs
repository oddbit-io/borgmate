using System.Reflection;
using System.Text.RegularExpressions;
using BorgMate.Presentation.ViewModels;
using BorgMate.Runtime.Operations;
using BorgMate.Runtime.Orchestration;

namespace BorgMate.Tests.Persistence;

public class ArchitectureBoundaryTests
{
    private static readonly Assembly AppAssembly = typeof(IRepositoryOperationRunner).Assembly;
    private static readonly Regex PublicTopLevelTypePattern = new(
        @"^public\s+(?:(?:sealed|abstract|static|partial)\s+)*(?:class|interface|record|enum)\s+([A-Za-z_]\w*)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    [Theory]
    [InlineData("BorgMate.Persistence.Configuration")]
    [InlineData("BorgMate.Persistence.Journal")]
    [InlineData("BorgMate.Persistence.Keychain")]
    public void Stores_DoNotDependOnRuntimeOrPresentation(string namespacePrefix)
    {
        AssertNoDependency(namespacePrefix, "BorgMate.Runtime");
        AssertNoDependency(namespacePrefix, "BorgMate.Presentation.ViewModels");
        AssertNoDependency(namespacePrefix, "BorgMate.Presentation.Views");
    }

    [Fact]
    public void TransientRetryPolicy_HasNoPresentationOrLifecycleDependencies()
    {
        var type = typeof(TransientRetryPolicy);
        var constructorParameterTypes = type
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType.FullName)
            .ToArray();

        Assert.DoesNotContain(constructorParameterTypes, n => n is not null && n.Contains("BorgMate.Runtime.Jobs"));
        Assert.DoesNotContain(constructorParameterTypes, n => n is not null && n.Contains("BorgMate.Persistence.Journal"));
        Assert.DoesNotContain(constructorParameterTypes, n => n is not null && n.Contains("BorgMate.Runtime.Security.PromptingPassphraseProvider"));
        Assert.DoesNotContain(constructorParameterTypes, n => n is not null && n.Contains("BorgMate.Presentation.ViewModels"));

        AssertNoDependency(type, "Avalonia");
        AssertNoDependency(type, "BorgMate.Localization");
        AssertNoDependency(type, "BorgMate.Runtime.Jobs");
        AssertNoDependency(type, "BorgMate.Persistence.Journal");
        AssertNoDependency(type, "BorgMate.Runtime.Security.PromptingPassphraseProvider");
        AssertNoDependency(type, "BorgMate.Presentation.ViewModels");
    }

    [Fact]
    public void ViewModels_DoNotDependOnForbiddenRuntimeImplementationServices()
    {
        var viewModelTypes = AppAssembly.GetTypes()
            .Where(t => t.Namespace == "BorgMate.Presentation.ViewModels" && t.IsClass && t != typeof(ViewModelBase))
            .ToArray();

        var forbiddenTypes = new[]
        {
            "BorgMate.Infrastructure.Borg.BorgServiceFactory",
            "BorgMate.Infrastructure.Borg.IBorgService",
            "BorgMate.Infrastructure.Borg.BorgProgressParser",
            "BorgMate.Infrastructure.Borg.BorgDiffParser",
            "BorgMate.Infrastructure.Borg.ArchiveJsonParser",
            "BorgMate.Infrastructure.Borg.ArchiveFileTreeBuilder",
            "BorgMate.Infrastructure.Borg.BorgJobProgressSink",
            "BorgMate.Infrastructure.Borg.BorgCacheService",
            "BorgMate.Runtime.Jobs.BorgJobQueue",
            "BorgMate.Persistence.Keychain.ICredentialStore",
            "BorgMate.Infrastructure.Platform.WslHelper",
            typeof(BorgOperationOrchestrator).FullName!,
            typeof(TransientRetryPolicy).FullName!
        };

        var violations = viewModelTypes
            .SelectMany(t => GetReferencedTypes(t).Where(r => forbiddenTypes.Contains(r.FullName)).Select(r => (ViewModel: t, Forbidden: r)))
            .ToArray();

        Assert.Empty(violations.Select(v => $"{v.ViewModel.FullName} -> {v.Forbidden.FullName}"));
    }

    [Fact]
    public void RuntimeInterfaces_DoNotDependOnPresentation()
    {
        var runtimeInterfaceTypes = AppAssembly.GetTypes()
            .Where(t => t.Namespace is not null &&
                        t.Namespace.StartsWith("BorgMate.Runtime", StringComparison.Ordinal) &&
                        t.IsInterface)
            .ToArray();

        var violations = runtimeInterfaceTypes
            .SelectMany(t => GetReferencedTypes(t)
                .Where(r => r.Namespace is not null &&
                    (r.Namespace.StartsWith("BorgMate.Presentation.ViewModels", StringComparison.Ordinal) ||
                     r.Namespace.StartsWith("BorgMate.Presentation.Views", StringComparison.Ordinal)))
                .Select(r => (RuntimeType: t, PresentationType: r)))
            .ToArray();

        Assert.Empty(violations.Select(v => $"{v.RuntimeType.FullName} -> {v.PresentationType.FullName}"));
    }

    [Theory]
    [InlineData(typeof(RepositoryOperationRunner))]
    [InlineData(typeof(ArchiveOperationRunner))]
    [InlineData(typeof(RepositoryVerificationOperationRunner))]
    public void OperationRunnerImplementations_DoNotDependOnPresentation(Type runnerType)
    {
        AssertNoDependency(runnerType, "BorgMate.Presentation.ViewModels");
        AssertNoDependency(runnerType, "BorgMate.Presentation.Views");
    }

    [Fact]
    public void SourceFiles_AreNotLeftInLegacyMixedFolders()
    {
        var appRoot = GetAppProjectRoot();
        var legacyFolders = new[]
        {
            Path.Combine(appRoot, "Models"),
            Path.Combine(appRoot, "Services"),
            Path.Combine(appRoot, "Services", "Config"),
            Path.Combine(appRoot, "Services", "Journal"),
            Path.Combine(appRoot, "Services", "Queue"),
            Path.Combine(appRoot, "Services", "UI"),
            Path.Combine(appRoot, "ViewModels"),
            Path.Combine(appRoot, "Views")
        };

        var leftovers = legacyFolders
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(appRoot, path))
            .Order()
            .ToArray();

        Assert.Empty(leftovers);
    }

    [Theory]
    [InlineData("Presentation/State/RepositoryPresentationState.cs")]
    [InlineData("Persistence/Configuration/IRepositoryConfigStore.cs")]
    [InlineData("Persistence/Configuration/JsonAppConfigStore.cs")]
    [InlineData("Persistence/Journal/SqliteJournalStore.cs")]
    [InlineData("Runtime/Scheduling/BackupScheduler.cs")]
    [InlineData("Runtime/Jobs/BorgJobQueue.cs")]
    [InlineData("Runtime/Security/PromptingPassphraseProvider.cs")]
    [InlineData("Persistence/Keychain/ICredentialStore.cs")]
    public void KeyFiles_LiveInPlannedFolders(string relativePath)
    {
        Assert.True(File.Exists(Path.Combine(GetAppProjectRoot(), relativePath)), relativePath);
    }

    [Fact]
    public void SourceFiles_ContainAtMostOnePublicTopLevelType()
    {
        var appRoot = GetAppProjectRoot();
        var violations = Directory
            .EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedPath(path))
            .Select(path => new
            {
                Path = Path.GetRelativePath(appRoot, path),
                Types = PublicTopLevelTypePattern.Matches(File.ReadAllText(path)).Select(m => m.Groups[1].Value).ToArray()
            })
            .Where(item => item.Types.Length > 1)
            .Select(item => $"{item.Path}: {string.Join(", ", item.Types)}")
            .Order()
            .ToArray();

        Assert.Empty(violations);
    }

    private static void AssertNoDependency(string namespacePrefix, string dependency)
    {
        var violations = AppAssembly.GetTypes()
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith(namespacePrefix, StringComparison.Ordinal))
            .Where(t => GetReferencedTypes(t).Any(r => IsDependency(r, dependency)))
            .Select(t => t.FullName)
            .Order()
            .ToArray();

        Assert.Empty(violations.Select(t => $"{t} must not depend on {dependency}"));
    }

    private static void AssertNoDependency(Type type, string dependency)
    {
        var violations = GetReferencedTypes(type)
            .Where(r => IsDependency(r, dependency))
            .Select(t => t.FullName)
            .Order()
            .ToArray();

        Assert.Empty(violations.Select(t => $"{type.FullName} must not depend on {dependency}: {t}"));
    }

    private static bool IsDependency(Type type, string dependency)
    {
        var fullName = type.FullName;
        return fullName == dependency ||
               fullName is not null && fullName.StartsWith(dependency + ".", StringComparison.Ordinal);
    }

    private static string GetAppProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BorgMate.Tests.csproj")))
            directory = directory.Parent;

        if (directory?.Parent is null)
            throw new InvalidOperationException("Could not locate BorgMate.Tests project root.");

        return Path.Combine(directory.Parent.FullName, "BorgMate");
    }

    private static bool IsGeneratedPath(string path)
    {
        var normalized = path.Replace(Path.DirectorySeparatorChar, '/');
        return normalized.Contains("/bin/", StringComparison.Ordinal) ||
               normalized.Contains("/obj/", StringComparison.Ordinal);
    }

    private static IEnumerable<Type> GetReferencedTypes(Type type)
    {
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(c => c.GetParameters().Select(p => p.ParameterType));
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Select(f => f.FieldType);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Select(p => p.PropertyType);
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType));

        return constructors
            .Concat(fields)
            .Concat(properties)
            .Concat(methods)
            .Select(UnwrapType)
            .Distinct();
    }

    private static Type UnwrapType(Type type)
    {
        while (type.HasElementType && type.GetElementType() is { } elementType)
            type = elementType;

        if (type.IsGenericType)
            return type.GetGenericArguments().Select(UnwrapType).FirstOrDefault() ?? type;

        return type;
    }
}
