namespace AutoTrainer.Api.ApiTypes;

public class ApiHeartBeat
{
    public string Identifier { get; set; } = "";

    public string Version { get; set; } = "";

    public double Timestamp { get; set; }
}
