using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Ipfs.Cli.Commands;

[Command(Name = "pubsub", Description = "Publish/subscribe to messages on a given topic")]
[Subcommand(typeof(PubsubListCommand))]
[Subcommand(typeof(PubsubPeersCommand))]
[Subcommand(typeof(PubsubPublishCommand))]
[Subcommand(typeof(PubsubSubscribeCommand))]
internal class PubsubCommand : CommandBase
{
    public Program Parent { get; set; }

    protected override Task<int> OnExecute(CommandLineApplication app)
    {
        app.ShowHelp();
        return Task.FromResult(0);
    }
}

[Command(Name = "ls", Description = "List subscribed topics by name")]
internal class PubsubListCommand : CommandBase
{
    private PubsubCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        IEnumerable<string> topics = await Program.CoreApi.PubSub.SubscribedTopicsAsync();
        return Program.Output(app, topics, (data, writer) =>
        {
            foreach (string topic in topics)
            {
                writer.WriteLine(topic);
            }
        });
    }
}

[Command(Name = "peers", Description = "List peers that are pubsubbing with")]
internal class PubsubPeersCommand : CommandBase
{
    [Argument(0, "topic", "The topic of interest")]
    public string Topic { get; set; }

    private PubsubCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        IEnumerable<Peer> peers = await Program.CoreApi.PubSub.PeersAsync(Topic);
        return Program.Output(app, peers, null);
    }
}

[Command(Name = "pub", Description = "Publish a message on a topic")]
internal class PubsubPublishCommand : CommandBase
{
    [Argument(1, "message", "The data to publish")]
    [Required]
    public string Message { get; set; }

    [Argument(0, "topic", "The topic of interest")]
    [Required]
    public string Topic { get; set; }

    private PubsubCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        await Program.CoreApi.PubSub.PublishAsync(Topic, Message);
        return 0;
    }
}

[Command(Name = "sub", Description = "Subscribe to messages on a topic")]
internal class PubsubSubscribeCommand : CommandBase
{
    [Argument(0, "topic", "The topic of interest")]
    [Required]
    public string Topic { get; set; }

    private PubsubCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        CancellationTokenSource cts = new();
        await Program.CoreApi.PubSub.SubscribeAsync(Topic, (m) =>
        {
            _ = Program.Output(app, m, (data, writer) =>
            {
                writer.WriteLine(Encoding.UTF8.GetString(data.DataBytes));
            });
        }, cts.Token);

        // Never return, just print messages received.
        await Task.Delay(-1);

        // Keep compiler happy.
        return 0;
    }
}