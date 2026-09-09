namespace AutoTrainer.Api.Endpoints;

// Validation for the free-text body a note write accepts.
internal static class NotesText
{
    // The column is unbounded TEXT, but every accepted value is echoed to every connected SignalR client on
    // the next broadcast. Counted in UTF-16 code units, so an astral-plane character costs two.
    public const int MaxLength = 16384;

    // null covers both "member absent" and "member sent as JSON null". Blank is rejected too: deleting the note
    // is how a note is removed, so an empty body is a mistake rather than a way to clear one.
    public static bool TryAccept(string? value, out string body, out string? error)
    {
        body = "";

        if (value is null)
        {
            error = "No note body supplied; expected 'body'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "'body' must not be blank.";
            return false;
        }

        if (value.Length > MaxLength)
        {
            error = $"'body' exceeds the {MaxLength} character limit.";
            return false;
        }

        // Not trimmed: operators paste formatted text and leading indentation is theirs to keep.
        body = value;
        error = null;
        return true;
    }
}
