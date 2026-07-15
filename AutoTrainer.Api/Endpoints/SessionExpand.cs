namespace AutoTrainer.Api.Endpoints;

// Parses the sessions `expand` query param. Tokens: trials, trials.reaches, batches (trials.reaches implies
// trials). Unknown tokens fail with a message rather than being silently ignored.
public readonly record struct SessionExpand(bool Trials, bool TrialsReaches, bool Batches)
{
    public static bool TryParse(string? raw, out SessionExpand expand, out string? error)
    {
        expand = default;
        error = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;

        bool trials = false, reaches = false, batches = false;

        foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "trials": trials = true; break;
                case "trials.reaches": trials = true; reaches = true; break;
                case "batches": batches = true; break;
                default:
                    error = $"Unknown expand token '{token}'. Valid: trials, trials.reaches, batches.";
                    return false;
            }
        }

        expand = new SessionExpand(trials, reaches, batches);
        return true;
    }
}
