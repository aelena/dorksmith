using Dorksmith.Api.Catalogs;
using Dorksmith.Api.Configuration;
using Dorksmith.Api.Generation;
using Dorksmith.Api.Tests.Unit;
using Microsoft.Extensions.Options;

namespace Dorksmith.Api.Tests;

/// <summary>Builds the generator against the real repository catalogs without hosting the web app.</summary>
public static class TestServices
{
    public static readonly Lazy<ICatalogProvider> Catalogs = new(() => CatalogTests.LoadRepoCatalogs());

    public static DorkGenerator Generator(GenerationOptions? generation = null)
    {
        var catalogs = Catalogs.Value;
        var gen = Options.Create(generation ?? new GenerationOptions());
        var usernames = Options.Create(new UsernameSearchOptions());
        return new DorkGenerator(catalogs, new RequestValidator(catalogs, gen, usernames), new PlaceholderResolver(catalogs));
    }

    public static OperatorCatalog GoogleOperators()
    {
        Catalogs.Value.TryGetOperators("google", out var ops);
        return ops;
    }
}
