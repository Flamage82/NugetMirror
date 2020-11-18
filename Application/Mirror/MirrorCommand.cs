using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
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
            foreach (var sourcePackage in sourcePackages)
            {
                Log.Information("Processing {Package}", sourcePackage.Identity.Id);

                var sourceVersions = await sourceFindPackageByIdResource.GetAllVersionsAsync(sourcePackage.Identity.Id, cache, logger, cancellationToken);
                var destinationVersions = await destinationFindPackageByIdResource.GetAllVersionsAsync(sourcePackage.Identity.Id, cache, logger, cancellationToken);

                await sourceVersions
                    .Except(destinationVersions)
                    .ForEachInParallelAsync(
                        settings.MaxDegreeOfParallelism,
                        async version =>
                        {
                            Log.Information("Uploading {Package}.{Version}", sourcePackage.Identity.Id, version);
                            var path = $"{tempPath}{sourcePackage.Identity.Id.ToLower()}.{version.ToString().ToLower()}.nupkg";
                            await using (var fileStream = File.Create(path))
                            {
                                var isSuccessful = await sourceFindPackageByIdResource.CopyNupkgToStreamAsync(sourcePackage.Identity.Id, version, fileStream, cache, logger, cancellationToken);
                                if (!isSuccessful)
                                {
                                    Log.Warning("Failed to upload {Package}.{Version}", sourcePackage.Identity.Id, version);
                                }
                            }

                            await packageUpdateResource.Push(path, null, settings.UploadTimeout, false, _ => settings.ApiKey, null, false, true, null, logger);

                            File.Delete(path);
                        });
            }

            Log.Information("Mirror complete!");
            return 0;
        }
    }
}
