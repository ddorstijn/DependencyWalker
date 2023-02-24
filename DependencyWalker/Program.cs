using DependencyWalker;
using NuGet.Frameworks;
using NuGet.Versioning;

internal class Program
{
    private static async Task Main(string[] args)
    {
        try
        {
            var dependencyList = await DependencyList.Build(new("Nuget.Protocol", NuGetVersion.Parse("6.5.0")), NuGetFramework.AnyFramework, null)!;
            Console.WriteLine(dependencyList.InstallScript);
            foreach (var dependency in dependencyList)
            {
                Console.WriteLine(dependency);
            }
        }
        catch (PackageNotFoundException e)
        {
            Console.WriteLine(e);
        }
    }
}