using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Platform.Modules.People.Domain;
using Platform.Modules.People.Infrastructure;
using Platform.SharedKernel.Results;

namespace Platform.Modules.People.Features;

/// <summary>Validates custom field values against the tenant's active definitions and normalises to JSON.</summary>
internal sealed class CustomFieldValidator(PeopleDbContext db)
{
    public async Task<Result<string>> ValidateAsync(Dictionary<string, JsonElement>? values, CancellationToken ct)
    {
        values ??= [];
        var definitions = await db.CustomFields.AsNoTracking().Where(f => f.IsActive).ToListAsync(ct);
        var errors = new Dictionary<string, string[]>();
        var clean = new Dictionary<string, JsonElement>();

        foreach (var def in definitions)
        {
            var present = values.TryGetValue(def.Key, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
            if (!present)
            {
                if (def.IsRequired)
                {
                    errors[$"customFields.{def.Key}"] = [$"{def.Label} is required."];
                }

                continue;
            }

            var ok = def.FieldType switch
            {
                CustomFieldType.Text or CustomFieldType.LongText => value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 4000,
                CustomFieldType.Number => value.ValueKind == JsonValueKind.Number,
                CustomFieldType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                CustomFieldType.Date => value.ValueKind == JsonValueKind.String && DateOnly.TryParse(value.GetString(), out _),
                CustomFieldType.Select => value.ValueKind == JsonValueKind.String && def.Options.Contains(value.GetString()!),
                CustomFieldType.MultiSelect => value.ValueKind == JsonValueKind.Array &&
                    value.EnumerateArray().All(v => v.ValueKind == JsonValueKind.String && def.Options.Contains(v.GetString()!)),
                _ => false,
            };

            if (!ok)
            {
                errors[$"customFields.{def.Key}"] = [$"Invalid value for {def.Label}."];
                continue;
            }

            clean[def.Key] = value;
        }

        if (errors.Count > 0)
        {
            return Error.Validation("person.invalid_custom_fields", "Some custom fields are invalid.", errors);
        }

        return JsonSerializer.Serialize(clean);
    }
}
