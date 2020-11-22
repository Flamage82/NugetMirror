using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Serilog;
using Spectre.Cli;
using ILogger = NuGet.Common.ILogger;

namespace NugetMirror.Application.Mirror
{
    public class MirrorCommand : AsyncCommand<MirrorCommandSettings>
    {
        private readonly ILogger logger;

        private readonly SourceCacheContext cache;

        public MirrorCommand(ILogger logger, SourceCacheContext cache)
        {
            this.logger = logger;
            this.cache = cache;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, MirrorCommandSettings settings)
        {
            Log.Information("Starting Nuget package mirroring");

            var cancellationToken = CancellationToken.None;

            Log.Verbose("Querying source packages");
            var (leftRepository, leftFindPackageByIdResource, leftPackages) = await GetRepositoryPackages(settings.Source);
            Log.Verbose("Querying destination packages");
            var (rightRepository, rightFindPackageByIdResource, rightPackages) = await GetRepositoryPackages(settings.Destination);

            var packageIdsToMirror =
                leftPackages.Union(rightPackages)
                .Select(metadata => metadata.Identity.Id)
                .Distinct()
                .ToList();
            if (!settings.MirrorOldVersions)
            {
                packageIdsToMirror = packageIdsToMirror.Where(packageId =>
                    {
                        var leftPackage = leftPackages.SingleOrDefault(package => package.Identity.Id == packageId);
                        var rightPackage = rightPackages.SingleOrDefault(package => package.Identity.Id == packageId);
                        return leftPackage?.Identity.Version != rightPackage?.Identity.Version;
                    })
                    .ToList();
            }

            var leftPackageVersions = await GetPackageVersions(leftFindPackageByIdResource, leftPackages).ToListAsync(cancellationToken);
            var rightPackageVersions = await GetPackageVersions(rightFindPackageByIdResource, rightPackages).ToListAsync(cancellationToken);

            Log.Information("Mirroring packages to the destination");
            await MirrorPackages(leftFindPackageByIdResource, rightRepository, Filter(leftPackageVersions, rightPackages), rightPackageVersions);

            if (settings.Bidirectional)
            {
                Log.Information("Mirroring packages to the source");
                await MirrorPackages(rightFindPackageByIdResource, leftRepository, Filter(rightPackageVersions, leftPackages), leftPackageVersions);
            }

            Log.Information("Mirror complete");
            return 0;

            async Task MirrorPackages(FindPackageByIdResource sourceFindPackageByIdResource, SourceRepository destinationRepository, IEnumerable<PackageVersion> sourcePackages, IEnumerable<PackageVersion> destinationPackages)
            {
                var packagesToUpgrade = sourcePackages
                    .Except(destinationPackages)
                    .ToList();

                Log.Information("There are {Count} packages to upload", packagesToUpgrade.Count);
                if (packagesToUpgrade.Count == 0)
                {
                    return;
                }

                var tempPath = Path.GetTempPath();
                var packageUpdateResource = await destinationRepository.GetResourceAsync<PackageUpdateResource>(cancellationToken);

                await packagesToUpgrade
                    .OrderBy(package => package.Id)
                    .ThenByDescending(version => version.Version)
                    .ForEachInParallelAsync(
                        settings.MaxDegreeOfParallelism,
                        async packageVersion =>
                        {
                            Log.Information("Uploading {Package}.{Version}", packageVersion.Id, packageVersion.Version);
                            if (settings.IsDryRunEnabled)
                            {
                                return;
                            }

                            var path = $"{tempPath}{packageVersion.Id.ToLower()}.{packageVersion.Version.ToString().ToLower()}.nupkg";
                            try
                            {
                                await using (var fileStream = File.Create(path))
                                {
                                    var isSuccessful = await sourceFindPackageByIdResource.CopyNupkgToStreamAsync(packageVersion.Id, packageVersion.Version, fileStream, cache, logger, cancellationToken);
                                    if (!isSuccessful)
                                    {
                                        Log.Warning("Failed to upload {Package}.{Version}", packageVersion.Id, packageVersion.Version);
                                    }
                                }

                                await packageUpdateResource.Push(path, null, settings.UploadTimeout, false, _ => settings.ApiKey, null, false, true, null, logger);
                            }
                            finally
                            {
                                File.Delete(path);
                            }
                        });
            }

            async Task<(SourceRepository Repository, FindPackageByIdResource FindPackageByIdResource, List<IPackageSearchMetadata> Packages)> GetRepositoryPackages(string source)
            {
                var repository = Repository.Factory.GetCoreV3(source);
                var packageSearchResource = await repository.GetResourceAsync<PackageSearchResource>(cancellationToken);
                var findPackageByIdResource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
                var packages = (await packageSearchResource.SearchAsync(settings.SearchTerm, new SearchFilter(true), 0, int.MaxValue, logger, cancellationToken))
                    .OrderBy(metadata => metadata.Identity.Id)
                    .ToList();

                return (repository, findPackageByIdResource, packages);
            }

            async IAsyncEnumerable<PackageVersion> GetPackageVersions(FindPackageByIdResource findPackageByIdResource, IEnumerable<IPackageSearchMetadata> packages)
            {
                foreach (var package in packages.Where(package => packageIdsToMirror.Contains(package.Identity.Id)))
                {
                    var packageId = package.Identity.Id;
                    Log.Verbose("Getting package versions for {Package}", packageId);
                    var versions = await findPackageByIdResource.GetAllVersionsAsync(packageId, cache, logger, cancellationToken);
                    foreach (var version in versions)
                    {
                        yield return new PackageVersion(packageId, version);
                    }
                }
            }

            IEnumerable<PackageVersion> Filter(IEnumerable<PackageVersion> sourcePackageVersions, IEnumerable<IPackageSearchMetadata> destinationPackages)
            {
                if (settings.MirrorOldVersions)
                {
                    return sourcePackageVersions;
                }

                return sourcePackageVersions
                    .GroupBy(version => version.Id)
                    .SelectMany(grouping =>
                    {
                        var destinationPackage = destinationPackages.SingleOrDefault(metadata => metadata.Identity.Id == grouping.Key);
                        if (destinationPackage is null)
                        {
                            return grouping;
                        }

                        return grouping.Where(packageVersion => packageVersion.Version > destinationPackage.Identity.Version);
                    });
            }
        }

        public record PackageVersion
        {
            public string Id { get; set; }

            public NuGetVersion Version { get; set; }

            public PackageVersion(string id, NuGetVersion version) => (Id, Version) = (id, version);
        }
    }
}
