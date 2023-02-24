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

        /// <summary>
        /// Generate list based on package. This is a set with only unique items.
        /// </summary>
        /// <param name="package">Id and Version of the package the dependency list will be build from</param>
        /// <param name="framework">The target framework for which the package should be picked</param>
        /// <param name="logger">Optional: Enable logging for Fetching and Resolving packages</param>
        /// <returns>List with unique packages to be installed</returns>
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

        /// <summary>
        /// Recursively find packages dependencies
        /// </summary>
        /// <param name="package">Id and Version of the package the dependency list will be build from</param>
        /// <param name="framework">The target framework for which the package should be picked</param>
        /// <param name="cacheContext">Used for caching already fetched items. This speeds up information gathering</param>
        /// <param name="logger"></param>
        /// <param name="availablePackages">Set of possible packages. Packages with the same Id but a different version can later be resolved by the PackageResolver</param>
        /// <exception cref="PackageNotFoundException">Exception thrown when no package can be found. Either due to a misspelling of the base package or no package existing of the specific version or target framework.</exception>
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

        /// <summary>
        /// The list as a script to install with UBlendIt
        /// </summary>
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
