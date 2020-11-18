using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace Application
{
    public class ListPackagesCommand
    {
        public async Task Execute(CancellationToken cancellationToken)
        {
            var logger = NullLogger.Instance;
            var cache = new SourceCacheContext();

            var sourceRepo = Repository.Factory.GetCoreV3(Environment.GetEnvironmentVariable("MyGetSource"));
            var destinationRepo = Repository.Factory.GetCoreV3("http://localhost:5555/v3/index.json");

            var packageSearchResource = await sourceRepo.GetResourceAsync<PackageSearchResource>(cancellationToken);
            var sourceFindPackageByIdResource = await sourceRepo.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
            var destinationFindPackageByIdResource = await destinationRepo.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
            var packageUpdateResource = await destinationRepo.GetResourceAsync<PackageUpdateResource>(cancellationToken);

            var sourcePackages = await packageSearchResource.SearchAsync(string.Empty, new SearchFilter(true), 0, int.MaxValue, logger, cancellationToken);
            foreach (var sourcePackage in sourcePackages)
            {
                var sourceVersions = await sourceFindPackageByIdResource.GetAllVersionsAsync(sourcePackage.Identity.Id, cache, logger, cancellationToken);
                var destinationVersions = await destinationFindPackageByIdResource.GetAllVersionsAsync(sourcePackage.Identity.Id, cache, logger, cancellationToken);

                foreach (var version in sourceVersions.Except(destinationVersions).Take(2))
                {
                    var path = $"C:\\Temp\\Packages\\{sourcePackage.Identity.Id.ToLower()}.{version.ToString().ToLower()}.nupkg";
                    await using (var fileStream = File.Create(path))
                    {
                        var isSuccessful = await sourceFindPackageByIdResource.CopyNupkgToStreamAsync(sourcePackage.Identity.Id, version, fileStream, cache, logger, cancellationToken);
                        if (!isSuccessful)
                        {
                            continue;
                        }
                    }

                    await packageUpdateResource.Push(path, null, 30, false, _ => "NUGET-SERVER-API-KEY", null, false, true, null, logger);
                }
            }
        }
    }
}
