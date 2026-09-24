using Dorksmith.Api.Catalogs;
using Dorksmith.Api.Contracts;

namespace Dorksmith.Api.Generation;

/// <summary>Fully validated and normalised inputs for one generation run.</summary>
public sealed record GenerationContext
{
    public required InputType InputType { get; init; }
    public required string Input { get; init; }
    public required IReadOnlyList<string> Words { get; init; }
    public required Intent Intent { get; init; }
    public required OperatorCatalog Operators { get; init; }
    public required int MaxVariants { get; init; }

    public string? Domain { get; init; }
    public Uri? Url { get; init; }
    public string? Username { get; init; }
    public string? Email { get; init; }
    public string? EmailUser { get; init; }
    public string? FilenameStem { get; init; }
    public string? FilenameExt { get; init; }

    public IReadOnlyList<string> FileTypes { get; init; } = [];
    public IReadOnlyList<string> ExcludeTerms { get; init; } = [];
    public DateOnly? After { get; init; }
    public DateOnly? Before { get; init; }
    public string? Site { get; init; }
    public string? Organization { get; init; }
    public string? Location { get; init; }
    public string? Role { get; init; }
    public string? DisplayName { get; init; }

    public string InputTypeId => InputType.ToId();
    public bool HasDates => After is not null || Before is not null;
    public bool HasContext => Organization is not null || Location is not null || Role is not null || DisplayName is not null;
}
