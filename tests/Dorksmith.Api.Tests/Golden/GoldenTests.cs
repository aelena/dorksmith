using System.Text.Json;
using System.Text.Json.Serialization;
using Dorksmith.Api.Contracts;
using Dorksmith.Api.Http;

namespace Dorksmith.Api.Tests.Golden;

/// <summary>
/// Golden fixtures: input + type + intent + options -> expected ordered query variants.
/// Every catalog template must be covered by at least one fixture. Set DORKSMITH_UPDATE_GOLDEN=1 to rewrite
/// the expected queries from the current generator output (then review the diff).
/// </summary>
public class GoldenTests
{
    private static readonly string FixtureDir = Path.Combine(DorksmithFactory.RepoRoot, "tests", "Dorksmith.Api.Tests", "Golden", "fixtures");
    private static readonly bool Update = Environment.GetEnvironmentVariable("DORKSMITH_UPDATE_GOLDEN") == "1";

    public sealed class Fixture
    {
        public string Name { get; set; } = "";
        public string Input { get; set; } = "";
        public string InputType { get; set; } = "";
        public string Intent { get; set; } = "";
        public GenerateOptions? Options { get; set; }
        /// <summary>Exact ordered variants (id + query).</summary>
        public List<ExpectedVariant> Expected { get; set; } = [];
        /// <summary>Loose assertions: each string must appear in at least one variant query.</summary>
        public List<string> ExpectedContains { get; set; } = [];
    }

    public sealed record ExpectedVariant(string Id, string Query);

    public static IEnumerable<object[]> Fixtures()
        => Directory.EnumerateFiles(FixtureDir, "*.json").Order(StringComparer.Ordinal).Select(f => new object[] { Path.GetFileNameWithoutExtension(f) });

    private static Fixture Load(string name)
        => JsonSerializer.Deserialize<Fixture>(File.ReadAllText(Path.Combine(FixtureDir, name + ".json")), AppJson.Options)!;

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Fixture_matches_generator_output(string name)
    {
        var fixture = Load(name);
        var result = TestServices.Generator().Generate(new GenerateRequest(fixture.Input, fixture.InputType, fixture.Intent, "google", fixture.Options));
        var actual = result.Variants.Select(v => new ExpectedVariant(v.Id, v.Query)).ToList();

        if (Update)
        {
            fixture.Expected = actual;
            var opts = new JsonSerializerOptions(AppJson.Options) { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            File.WriteAllText(Path.Combine(FixtureDir, name + ".json"), JsonSerializer.Serialize(fixture, opts) + "\n");
        }

        foreach (var needle in fixture.ExpectedContains)
            Assert.Contains(actual, v => v.Query.Contains(needle, StringComparison.Ordinal));

        Assert.Equal(fixture.Expected.Select(e => e.Id), actual.Select(a => a.Id));
        Assert.Equal(fixture.Expected.Select(e => e.Query), actual.Select(a => a.Query));
    }

    [Fact]
    public void Every_template_is_covered_by_a_golden_fixture()
    {
        var covered = Directory.EnumerateFiles(FixtureDir, "*.json")
            .Select(f => Load(Path.GetFileNameWithoutExtension(f)))
            .SelectMany(f => f.Expected.Select(e => e.Id))
            .ToHashSet(StringComparer.Ordinal);
        var all = TestServices.Catalogs.Value.Intents.Intents.SelectMany(i => i.Templates.Select(t => t.Id)).ToList();
        var missing = all.Except(covered).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, $"{missing.Count} template(s) without golden coverage: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Fixture_directory_exists_and_is_not_empty()
        => Assert.NotEmpty(Directory.EnumerateFiles(FixtureDir, "*.json"));
}
