using System.Reflection;
using System.Text.RegularExpressions;
using BorgMate.Presentation.ViewModels;
using BorgMate.Runtime.Operations;
using BorgMate.Runtime.Orchestration;
using NetArchTest.Rules;

namespace BorgMate.Tests.Persistence;

public class ArchitectureBoundaryTests
{
    private static readonly Assembly AppAssembly = typeof(IRepositoryOperationRunner).Assembly;
    private static readonly Regex PublicTopLevelTypePattern = new(
        @"^public\s+(?:(?:sealed|abstract|static|partial)\s+)*(?:class|interface|record|enum)\s+([A-Za-z_]\w*)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    [Theory]
    [InlineData("BorgMate.Services.Config")]
    [InlineData("BorgMate.Services.Journal")]
    [InlineData("BorgMate.Services.Keychain")]
    public void Stores_DoNotDependOnRuntimeOrPresentation(string namespacePrefix)
    {
        AssertNoDependency(namespacePrefix, "BorgMate.Runtime");
        AssertNoDependency(namespacePrefix, "BorgMate.ViewModels");
        AssertNoDependency(namespacePrefix, "BorgMate.Views");
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

        Assert.DoesNotContain(constructorParameterTypes, n => n is not null && n.Contains("BorgMate.Models.BorgJob"));
        Assert.DoesNotContain(constructorParameterTypes, n => n is not null && n.Contains("BorgMate.Services.Queue"));
        Assert.DoesNotContain(constructorParameterTypes, n => n is not null && n.Contains("BorgMate.Services.Journal"));
        Assert.DoesNotContain(constructorParameterTypes, n => n is not null && n.Contains("BorgMate.Services.UI.PromptingPassphraseProvider"));
        Assert.DoesNotContain(constructorParameterTypes, n => n is not null && n.Contains("BorgMate.ViewModels"));

        AssertNoDependency(type, "Avalonia");
        AssertNoDependency(type, "BorgMate.Localization");
        AssertNoDependency(type, "BorgMate.Services.Queue");
        AssertNoDependency(type, "BorgMate.Services.Journal");
        AssertNoDependency(type, "BorgMate.Services.UI.PromptingPassphraseProvider");
        AssertNoDependency(type, "BorgMate.ViewModels");
    }

    [Fact]
    public void ViewModels_DoNotDependOnForbiddenRuntimeImplementationServices()
    {
        var viewModelTypes = AppAssembly.GetTypes()
            .Where(t => t.Namespace == "BorgMate.ViewModels" && t.IsClass && t != typeof(ViewModelBase))
            .ToArray();

        var forbiddenTypes = new[]
        {
            "BorgMate.Services.Borg.BorgServiceFactory",
            "BorgMate.Services.Borg.IBorgService",
            "BorgMate.Services.Borg.BorgProgressParser",
            "BorgMate.Services.Borg.BorgDiffParser",
            "BorgMate.Services.Borg.ArchiveJsonParser",
            "BorgMate.Services.Borg.ArchiveFileTreeBuilder",
            "BorgMate.Services.Borg.BorgJobProgressSink",
            "BorgMate.Services.BorgCacheService",
            "BorgMate.Services.Queue.BorgJobQueue",
            "BorgMate.Services.Keychain.ICredentialStore",
            "BorgMate.Services.WslHelper",
            "BorgMate.Runtime.IBorgOperationOrchestrator",
            "BorgMate.Runtime.ITransientRetryPolicy"
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
            .Where(t => t.Namespace == "BorgMate.Runtime" && t.IsInterface)
            .ToArray();

        var violations = runtimeInterfaceTypes
            .SelectMany(t => GetReferencedTypes(t)
                .Where(r => r.Namespace is not null &&
                    (r.Namespace.StartsWith("BorgMate.ViewModels", StringComparison.Ordinal) ||
                     r.Namespace.StartsWith("BorgMate.Views", StringComparison.Ordinal)))
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
        AssertNoDependency(runnerType, "BorgMate.ViewModels");
        AssertNoDependency(runnerType, "BorgMate.Views");
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
        var result = Types.InAssembly(AppAssembly)
            .That()
            .ResideInNamespaceStartingWith(namespacePrefix)
            .Should()
            .NotHaveDependencyOn(dependency)
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure(namespacePrefix, dependency, result));
    }

    private static void AssertNoDependency(Type type, string dependency)
    {
        var result = Types.InAssembly(AppAssembly)
            .That()
            .HaveName(type.Name)
            .Should()
            .NotHaveDependencyOn(dependency)
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure(type.FullName!, dependency, result));
    }

    private static string FormatFailure(string subject, string dependency, TestResult result)
    {
        var failingTypes = result.FailingTypeNames is null
            ? string.Empty
            : string.Join(", ", result.FailingTypeNames);
        return $"{subject} must not depend on {dependency}. Failing types: {failingTypes}";
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
