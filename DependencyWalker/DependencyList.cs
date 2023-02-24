using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Resolver;
using System.Collections;
using System.Xml.Linq;

namespace DependencyWalker
{
    public class PackageNotFoundException : Exception
    {
        public readonly PackageIdentity Package;
        public readonly NuGetFramework Framework;

        public PackageNotFoundException(PackageIdentity package, NuGetFramework framework)
        {
            Package = package;
            Framework = framework;
        }

        public override string ToString() => $"Package {Package.Id} with version {Package.Version} could not be found in the NuGet repository for framework {Framework.Framework}";
    }

    public class DependencyList: IEnumerable<SourcePackageDependencyInfo>
    {
        private static readonly SourceRepository repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");

        private static readonly XElement pc_root = XElement.Parse("<Nugets pc:ignorewhitespace=\"yes\" pc:hideme=\"true\" xmlns:pc=\"Processing.Command\" xmlns:assign=\"Processing.Command.Assign\" />");
        private static readonly XAttribute pc_result_attr = new("assign" + "result", ".");

        private readonly IEnumerable<SourcePackageDependencyInfo> _dependencyList;
        
        private DependencyList(IEnumerable<SourcePackageDependencyInfo> dependencyList)
        {
            _dependencyList = dependencyList;
        }

        public static async Task<DependencyList> Build(PackageIdentity package, NuGetFramework framework, ILogger? logger)
        {
            logger ??= NullLogger.Instance;

            using SourceCacheContext cacheContext = new();

            HashSet<SourcePackageDependencyInfo> availablePackages = new(PackageIdentityComparer.Default);
            await GetPackageDependencies(
                package,
                framework,
                cacheContext,
                logger,
                availablePackages
            );

            PackageResolverContext ctx = new(
                DependencyBehavior.Highest,
                new[] { package.Id },
                Enumerable.Empty<string>(),
                Enumerable.Empty<PackageReference>(),
                Enumerable.Empty<PackageIdentity>(),
                availablePackages,
                new[] { repository.PackageSource },
                logger);

            IEnumerable<SourcePackageDependencyInfo> dependencies = new PackageResolver()
                .Resolve(ctx, CancellationToken.None)
                .Select(p => availablePackages.Single(x => PackageIdentityComparer.Default.Equals(x, p))
            );

            return new DependencyList(dependencies);
        }

        private static async Task GetPackageDependencies(PackageIdentity package, NuGetFramework framework, SourceCacheContext cacheContext, ILogger logger, ISet<SourcePackageDependencyInfo> availablePackages)
        {
            if (availablePackages.Contains(package)) return;

            DependencyInfoResource dependencyInfoResource = await repository.GetResourceAsync<DependencyInfoResource>()!;
            SourcePackageDependencyInfo? dependencyInfo = await dependencyInfoResource.ResolvePackage(
                package, framework, cacheContext, logger, CancellationToken.None);

            if (dependencyInfo is null) throw new PackageNotFoundException(package, framework);

            availablePackages.Add(dependencyInfo);
            foreach (PackageDependency? dependency in dependencyInfo.Dependencies)
            {
                await GetPackageDependencies(
                    new PackageIdentity(dependency.Id, dependency.VersionRange.MinVersion),
                    framework, cacheContext, logger, availablePackages);
            }
            
        }

        public XElement InstallScript
        {
            get
            {
                XElement root = new(pc_root);

                root.Add(_dependencyList.Select(d =>
                {
                    string content = $"nuget:installPackageIntoFolder('{d.Id}', '{d.Version}', '')";
                    return new XElement("pc" + "evaluate", new XAttribute("select", content), pc_result_attr);
                }));

                return root;
            }
        }

        public IEnumerator<SourcePackageDependencyInfo> GetEnumerator() => _dependencyList.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _dependencyList.GetEnumerator();
    }

}
