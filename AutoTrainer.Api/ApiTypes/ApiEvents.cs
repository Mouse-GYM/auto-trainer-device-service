namespace AutoTrainer.Api.ApiTypes;

public class ApiEvent
{
    public ApiEventKind Kind { get; set; }
    public double When { get; set; }
    public ulong Index { get; set; }
    public object? Context { get; set; }
    public int Repeat { get; set; }
}
