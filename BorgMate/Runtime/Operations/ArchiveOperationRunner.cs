using System;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Domain.Repositories;
using BorgMate.Infrastructure.Borg;
using BorgMate.Runtime.Jobs;
using BorgMate.Runtime.Orchestration;
using BorgMate.Runtime.Progress;
using Microsoft.Extensions.Logging;
using OneOf;
using BorgDiffParser = BorgMate.Infrastructure.Borg.BorgDiffParser;

namespace BorgMate.Runtime.Operations;

/// <summary>Default archive query runner backed by the borg job queue and cache.</summary>
public class ArchiveOperationRunner : IArchiveOperationRunner
{
    private readonly BorgServiceFactory borgServiceFactory;
    private readonly BorgJobQueue jobQueue;
    private readonly BorgCacheService cache;
    private readonly BorgOperationOrchestrator orchestrator;
    private readonly RepositoryStorageQuery repositoryStorageQuery;
    private readonly ILogger<ArchiveOperationRunner> logger;

    public ArchiveOperationRunner(
        BorgServiceFactory borgServiceFactory,
        BorgJobQueue jobQueue,
        BorgCacheService cache,
        BorgOperationOrchestrator orchestrator,
        RepositoryStorageQuery repositoryStorageQuery,
        ILogger<ArchiveOperationRunner> logger)
    {
        this.borgServiceFactory = borgServiceFactory;
        this.jobQueue = jobQueue;
        this.cache = cache;
        this.orchestrator = orchestrator;
        this.repositoryStorageQuery = repositoryStorageQuery;
        this.logger = logger;
    }

    public async Task<OneOf<ArchiveListResult, OperationError>> ListArchivesAsync(
        ArchiveListOperationOptions options, CancellationToken ct = default)
    {
        var repo = options.Repository;
        var service = borgServiceFactory.GetService(repo.BorgVersion);
        var result = await RunQueryAsync(
            repo,
            options.ProgressSink,
            $"{Strings.Get("Job.ListArchives")}: {repo.Name}",
            $"list:{repo.Path}",
            (_, operationToken) => service.ListArchivesAsync(repo, operationToken),
            ct);

        if (result.IsT1)
            return result.AsT1;

        return new ArchiveListResult(ArchiveJsonParser.ParseArchiveList(result.AsT0.Result.StandardOutput));
    }

    public async Task<OneOf<ArchiveDetailResult, OperationError>> GetArchiveDetailAsync(
        ArchiveDetailOperationOptions options, CancellationToken ct = default)
    {
        var repo = options.Repository;
        var service = borgServiceFactory.GetService(repo.BorgVersion);
        var result = await RunQueryAsync(
            repo,
            options.ProgressSink,
            $"Info: {repo.Name}::{options.ArchiveName}",
            $"info:{repo.Path}::{options.ArchiveName}",
            (_, operationToken) => service.InfoArchiveAsync(repo, options.ArchiveName, operationToken),
            ct);

        if (result.IsT1)
            return result.AsT1;

        var detail = ArchiveJsonParser.ParseArchiveDetail(result.AsT0.Result.StandardOutput);
        return detail is { } d
            ? new ArchiveDetailResult(d.OriginalSize, d.FileCount)
            : new OperationError.BorgFailure(null, -1, result.AsT0.Result.StandardOutput);
    }

    public async Task<OneOf<RepositoryStatsResult, OperationError>> GetRepositoryStatsAsync(
        RepositoryStatsOperationOptions options, CancellationToken ct = default)
    {
        var repo = options.Repository;
        var service = borgServiceFactory.GetService(repo.BorgVersion);
        StorageSpaceSnapshot? storageSpace = null;
        var result = await RunQueryAsync(
            repo,
            options.ProgressSink,
            $"{Strings.Get("Statistics")}: {repo.Name}",
            null,
            async (_, operationToken) =>
            {
                var infoResult = await service.InfoRepoAsync(repo, operationToken);
                if (infoResult.Success)
                    storageSpace = await repositoryStorageQuery.GetAsync(repo, operationToken);
                return infoResult;
            },
            ct);

        if (result.IsT1)
            return result.AsT1;

        var stats = ArchiveJsonParser.ParseRepoStats(result.AsT0.Result.StandardOutput);
        if (stats is not { } s)
            return new OperationError.BorgFailure(null, -1, result.AsT0.Result.StandardOutput);

        if (s.Size is { } repositorySize && storageSpace is { } space && repositorySize > space.UsedBytes)
        {
            logger.LogWarning(
                "Repository size exceeds used volume space: Name={RepositoryName}, Location={RepositoryLocation}, " +
                "LocationKind={LocationKind}, BorgVersion={BorgVersion}, RepositorySizeBytes={RepositorySizeBytes}, " +
                "UsedBytes={UsedBytes}, AvailableBytes={AvailableBytes}, CapacityBytes={CapacityBytes}",
                repo.Name,
                BorgCommandRedactor.RedactRepositoryLocation(repo),
                repo.LocationKind,
                repo.BorgVersion,
                repositorySize,
                space.UsedBytes,
                space.AvailableBytes,
                space.CapacityBytes);
        }

        return new RepositoryStatsResult
        {
            TotalSize = s.Size,
            TotalChunks = s.Chunks,
            StorageSpace = storageSpace
        };
    }

    public async Task<OneOf<ArchiveContentsResult, OperationError>> ListArchiveContentsAsync(
        ArchiveContentsOperationOptions options, CancellationToken ct = default)
    {
        var repo = options.Repository;
        var stdout = options.ForceRefresh ? null : cache.GetArchiveContents(repo.Path, options.ArchiveName);

        if (stdout is null)
        {
            var service = borgServiceFactory.GetService(repo.BorgVersion);
            var result = await RunQueryAsync(
                repo,
                options.ProgressSink,
                $"{Strings.Get("Job.ListContents")}: {repo.Name}::{options.ArchiveName}",
                $"contents:{repo.Path}::{options.ArchiveName}",
                (_, operationToken) => service.ListArchiveContentsAsync(repo, options.ArchiveName, operationToken),
                ct);

            if (result.IsT1)
                return result.AsT1;

            stdout = result.AsT0.Result.StandardOutput;
            cache.SetArchiveContents(repo.Path, options.ArchiveName, stdout);
        }

        var roots = await Task.Run(() => ArchiveFileTreeBuilder.Build(stdout), ct);
        return new ArchiveContentsResult(roots, stdout);
    }

    public async Task<OneOf<ArchiveDiffResult, OperationError>> GetArchiveDiffAsync(
        ArchiveDiffOperationOptions options, CancellationToken ct = default)
    {
        var repo = options.Repository;
        var diff = options.ForceRefresh
            ? null
            : cache.GetDiff(repo.Path, options.PreviousArchiveName, options.ArchiveName);

        if (diff is null)
        {
            var service = borgServiceFactory.GetService(repo.BorgVersion);
            var tag = $"{repo.Path}::{options.PreviousArchiveName}..{options.ArchiveName}";
            var result = await RunQueryAsync(
                repo,
                options.ProgressSink,
                $"{Strings.Get("Job.Diff")}: {repo.Name}::{options.ArchiveName}",
                $"diff:{tag}",
                (_, operationToken) => service.DiffArchivesAsync(
                    repo,
                    options.PreviousArchiveName,
                    options.ArchiveName,
                    operationToken),
                ct);

            if (result.IsT1)
                return result.AsT1;

            diff = await Task.Run(
                () => BorgDiffParser.ParseChangedPaths(result.AsT0.Result.StandardOutput),
                ct);
            cache.SetDiff(repo.Path, options.PreviousArchiveName, options.ArchiveName, diff);
        }

        return new ArchiveDiffResult(diff);
    }

    public void InvalidateRepositoryCache(BorgRepository repository)
    {
        cache.InvalidateRepo(repository.Path);
    }

    private async Task<OneOf<BorgExecutionResult, OperationError>> RunQueryAsync(
        BorgRepository repo,
        IOperationProgressSink progressSink,
        string jobName,
        string? tag,
        Func<BorgOperationContext, CancellationToken, Task<BorgResult>> operation,
        CancellationToken ct)
    {
        var job = jobQueue.Enqueue(
            jobName,
            async (j, token, progress) =>
            {
                var sink = new BorgJobProgressSink(j, progressSink);
                var result = await orchestrator.ExecuteAsync(
                    new BorgOperationRequest(repo, sink),
                    operation,
                    token);
                return result.ToBorgResult();
            },
            BorgJobKind.Query,
            tag,
            repo.Path);

        using var registration = ct.Register(() => job.Cts.Cancel());
        var borgResult = await job.Completion.Task;

        if (borgResult.Success)
            return new BorgExecutionResult(borgResult);

        return borgResult.WasCancelled
            ? new OperationError.Cancelled()
            : borgResult.ToOperationError();
    }
}
