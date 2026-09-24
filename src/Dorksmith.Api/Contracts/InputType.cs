namespace Dorksmith.Api.Contracts;

/// <summary>Explicit input classification. The UI may suggest one, the user can always override.</summary>
public enum InputType
{
    Keyword,
    Person,
    Organization,
    Domain,
    Url,
    Username,
    Email,
    Filename,
    Technology,
}

public static class InputTypes
{
    public static readonly IReadOnlyList<InputType> All = Enum.GetValues<InputType>();

    public static string ToId(this InputType t) => t switch
    {
        InputType.Keyword => "keyword",
        InputType.Person => "person",
        InputType.Organization => "organization",
        InputType.Domain => "domain",
        InputType.Url => "url",
        InputType.Username => "username",
        InputType.Email => "email",
        InputType.Filename => "filename",
        InputType.Technology => "technology",
        _ => throw new ArgumentOutOfRangeException(nameof(t)),
    };

    public static bool TryParse(string? id, out InputType type)
    {
        foreach (var t in All)
            if (string.Equals(t.ToId(), id, StringComparison.OrdinalIgnoreCase)) { type = t; return true; }
        type = default;
        return false;
    }
}
