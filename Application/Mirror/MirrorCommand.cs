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
            var tempPath = Path.GetTempPath();
            var cancellationToken = CancellationToken.None;

            var sourceRepo = Repository.Factory.GetCoreV3(settings.Source);
            var destinationRepo = Repository.Factory.GetCoreV3(settings.Destination);

            var packageSearchResource = await sourceRepo.GetResourceAsync<PackageSearchResource>(cancellationToken);
            var sourceFindPackageByIdResource = await sourceRepo.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
            var destinationFindPackageByIdResource = await destinationRepo.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
            var packageUpdateResource = await destinationRepo.GetResourceAsync<PackageUpdateResource>(cancellationToken);

            Log.Information("Querying source...");
            var sourcePackages = await packageSearchResource.SearchAsync(settings.SearchTerm, new SearchFilter(true), 0, int.MaxValue, logger, cancellationToken);

            var packagesToUpgrade = new List<(string PackageId, NuGetVersion Version, int Index)>();
            await sourcePackages
                .Select(metadata => metadata.Identity.Id)
                .OrderBy(packageId => packageId)
                .ForEachInParallelAsync(
                    settings.MaxDegreeOfParallelism,
                    async packageId =>
                    {
                        Log.Information("Scanning {Package}", packageId);
                        var sourceVersions = await sourceFindPackageByIdResource.GetAllVersionsAsync(packageId, cache, logger, cancellationToken);
                        var destinationVersions = await destinationFindPackageByIdResource.GetAllVersionsAsync(packageId, cache, logger, cancellationToken);
                        var versionsToUpgrade = sourceVersions
                            .Except(destinationVersions)
                            .OrderByDescending(version => version)
                            .Select((version, index) => (PackageId: packageId, Version: version, Index: index));
                        packagesToUpgrade.AddRange(versionsToUpgrade);
                    });

            Log.Information("There are {Count} packages to upload", packagesToUpgrade.Count);

            await packagesToUpgrade
                .OrderBy(package => package.Index)
                .ThenBy(package => package.PackageId)
                .ForEachInParallelAsync(
                settings.MaxDegreeOfParallelism,
                async packageVersion =>
                {
                    var (packageId, version, _) = packageVersion;
                    Log.Information("Uploading {Package}.{Version}", packageId, version);
                    var path = $"{tempPath}{packageId.ToLower()}.{version.ToString().ToLower()}.nupkg";
                    await using (var fileStream = File.Create(path))
                    {
                        var isSuccessful = await sourceFindPackageByIdResource.CopyNupkgToStreamAsync(packageId, version, fileStream, cache, logger, cancellationToken);
                        if (!isSuccessful)
                        {
                            Log.Warning("Failed to upload {Package}.{Version}", packageId, version);
                        }
                    }

                    await packageUpdateResource.Push(path, null, settings.UploadTimeout, false, _ => settings.ApiKey, null, false, true, null, logger);

                    File.Delete(path);
                });

            Log.Information("Mirror complete!");
            return 0;
        }
    }
}
