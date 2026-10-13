using System.Text.Json.Serialization;
using Passenger.App.CSharp;
using Passenger.Languages.CSharp;

namespace Passenger.App.Settings;

/// <summary>What a person changed of a live template, kept in the settings under its key: of a built-in one only the
/// parts that differ, of one they added every part. A part left out is the built-in one's, so what a later Passenger
/// changes in it arrives, and a built-in template it adds is there at once.</summary>
public sealed record TemplateChange
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Abbreviation { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    /// <summary>Where its shortcut may be typed, each context by name.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TemplateScope[]? Contexts { get; init; }

    /// <summary>Where Surround With offers it, each by name.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SurroundScope[]? Surround { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TemplateVariable[]? Variables { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Enabled { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Live { get; init; }

    /// <summary>Its digit in Surround With's list; empty for none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mnemonic { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Reformat { get; init; }

    /// <summary>The templates in force: the built-in ones with the person's changes, then those they added, in the order
    /// they were added. One added without a shortcut or a text, as a hand edit may leave it, is passed over.</summary>
    public static IReadOnlyList<LiveTemplate> Applied(IReadOnlyDictionary<string, TemplateChange> changes)
    {
        List<LiveTemplate> templates = [.. LiveTemplates.BuiltIn.Select(t => changes.TryGetValue(t.Key, out TemplateChange? change) ? change.Over(t) : t)];
        foreach ((string key, TemplateChange change) in changes)
        {
            if (templates.All(t => t.Key != key) && change is { Abbreviation.Length: > 0, Text: not null })
            {
                templates.Add(change.Over(new LiveTemplate(change.Abbreviation, string.Empty, TemplateScope.None, change.Text, []) { Key = key }));
            }
        }
        return templates;
    }

    /// <summary>What to keep for templates as the person left them: for each built-in one that differs, its differences;
    /// for each of their own, the whole of it.</summary>
    public static Dictionary<string, TemplateChange> Of(IEnumerable<LiveTemplate> templates)
    {
        Dictionary<string, TemplateChange> changes = [];
        foreach (LiveTemplate template in templates)
        {
            LiveTemplate? shipped = template.BuiltIn ? LiveTemplates.BuiltIn.FirstOrDefault(t => t.Key == template.Key) : null;
            if (shipped is null || !shipped.SameAs(template))
            {
                changes[template.Key] = Between(shipped, template);
            }
        }
        return changes;
    }

    /// <summary>The parts of a template that differ from the built-in one, all of them for one of the person's own.</summary>
    private static TemplateChange Between(LiveTemplate? shipped, LiveTemplate now) => new()
    {
        Abbreviation = shipped?.Abbreviation == now.Abbreviation ? null : now.Abbreviation,
        Description = shipped?.Description == now.Description ? null : now.Description,
        Text = shipped?.Text == now.Text ? null : now.Text,
        Contexts = shipped?.Scope == now.Scope ? null : Flags(now.Scope),
        Surround = shipped?.Surround == now.Surround ? null : Flags(now.Surround),
        Variables = shipped is not null && shipped.Variables.SequenceEqual(now.Variables) ? null : [.. now.Variables],
        Enabled = shipped?.Enabled == now.Enabled ? null : now.Enabled,
        Live = shipped?.Live == now.Live ? null : now.Live,
        Mnemonic = shipped is not null && shipped.Mnemonic == now.Mnemonic ? null : now.Mnemonic?.ToString() ?? string.Empty,
        Reformat = shipped?.Reformat == now.Reformat ? null : now.Reformat,
    };

    /// <summary>A template with this change's parts in place of its own.</summary>
    private LiveTemplate Over(LiveTemplate template) => template with
    {
        Abbreviation = Abbreviation ?? template.Abbreviation,
        Description = Description ?? template.Description,
        Text = Text ?? template.Text,
        Scope = Contexts is null ? template.Scope : Contexts.Aggregate(TemplateScope.None, (all, one) => all | one),
        Surround = Surround is null ? template.Surround : Surround.Aggregate(SurroundScope.None, (all, one) => all | one),
        Variables = Variables ?? template.Variables,
        Enabled = Enabled ?? template.Enabled,
        Live = Live ?? template.Live,
        Mnemonic = Mnemonic is null ? template.Mnemonic : Mnemonic is [char digit] && char.IsAsciiDigit(digit) ? digit : null,
        Reformat = Reformat ?? template.Reformat,
    };

    /// <summary>The change with what this build cannot use left out: a context it does not know, a variable without a name.</summary>
    public TemplateChange Clamped() => this with
    {
        Contexts = Contexts?.Where(Enum.IsDefined).ToArray(),
        Surround = Surround?.Where(Enum.IsDefined).ToArray(),
        Variables = Variables?.Where(v => v is { Name.Length: > 0 }).Select(v => v with { Macro = v.Macro ?? string.Empty, Parameter = v.Parameter ?? string.Empty }).ToArray(),
    };

    private static T[] Flags<T>(T value) where T : struct, Enum =>
        [.. Enum.GetValues<T>().Where(f => Convert.ToInt32(f, System.Globalization.CultureInfo.InvariantCulture) != 0 && value.HasFlag(f))];
}
