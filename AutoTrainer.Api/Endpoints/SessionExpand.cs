namespace AutoTrainer.Api.Endpoints;

// Parses the sessions `expand` query param. Tokens: trials, trials.rightHandReaches, batches
// (trials.rightHandReaches implies trials). The tree stops there: a trial's raw and non-right-hand reaches are
// not reachable from a session, only through /raw-reaches, /hand-reaches and /other-reaches. Unknown tokens
// fail with a message rather than being silently ignored.
public readonly record struct SessionExpand(bool Trials, bool TrialsRightHandReaches, bool Batches)
{
    public static bool TryParse(string? raw, out SessionExpand expand, out string? error)
    {
        expand = default;
        error = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;

        bool trials = false, rightHandReaches = false, batches = false;

        foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "trials": trials = true; break;
                case "trials.righthandreaches": trials = true; rightHandReaches = true; break;
                case "batches": batches = true; break;
                default:
                    error = $"Unknown expand token '{token}'. Valid: trials, trials.rightHandReaches, batches.";
                    return false;
            }
        }

        expand = new SessionExpand(trials, rightHandReaches, batches);
        return true;
    }
}
