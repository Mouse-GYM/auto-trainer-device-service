using AutoTrainer.Api.Data;

namespace AutoTrainer.Api.Endpoints;

// The single animal-resolution rule for every animal-scoped endpoint. Decoupled from AutotrainerDevice:
// the caller passes device.Animal?.Identifier as selectedIdentifier, keeping this unit-testable.
public readonly struct AnimalResolution
{
    public string? Identifier { get; private init; }   // resolved -> query the store
    public IResult? Error { get; private init; }        // 400/404 -> return immediately
    public bool Empty { get; private init; }            // no animal selected -> empty success

    public static AnimalResolution Resolve(string? animalParam, string? selectedIdentifier, ISqliteStorage storage)
    {
        if (!string.IsNullOrWhiteSpace(animalParam))
        {
            if (!SqliteStorage.IsValidIdentifier(animalParam))
                return new() { Error = TypedResults.BadRequest($"Invalid animal identifier '{animalParam}'.") };

            // A PROVIDED animal whose database file is missing is an unknown animal -> 404. (The omitted/selected
            // branch below deliberately skips this check: the store's read-without-create returns empty instead.)
            if (!storage.AnimalDatabaseExists(animalParam))
                return new() { Error = TypedResults.NotFound($"No data for animal '{animalParam}'.") };

            return new() { Identifier = animalParam };
        }

        // Omitted -> the device's currently selected animal; none selected is a normal empty result, not an error.
        if (string.IsNullOrWhiteSpace(selectedIdentifier))
            return new() { Empty = true };

        return new() { Identifier = selectedIdentifier };
    }
}
