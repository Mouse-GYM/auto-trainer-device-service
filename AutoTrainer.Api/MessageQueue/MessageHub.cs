namespace AutoTrainer.Api.MessageQueue;

public interface IMessageHub
{
    Task ReceiveMessage(string user, object message);

    Task SendCommand(string user, string message);
}

public class MessageHub : Hub<IMessageHub>
{
    public async Task SendMessage(string user, object message)
    {
        await Clients.All.ReceiveMessage(user, message);
    }

    public async Task SendCommand(string user, string message)
    {
        await Clients.All.SendCommand(user, message);
    }
}
