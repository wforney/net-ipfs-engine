using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;

namespace Ipfs.Cli.Commands;

[Command(Name = "swarm", Description = "Manage connections to the p2p network")]
[Subcommand(typeof(SwarmConnectCommand))]
[Subcommand(typeof(SwarmDisconnectCommand))]
[Subcommand(typeof(SwarmPeersCommand))]
[Subcommand(typeof(SwarmAddrsCommand))]
internal class SwarmCommand : CommandBase
{
    public Program Parent { get; set; }

    protected override Task<int> OnExecute(CommandLineApplication app)
    {
        app.ShowHelp();
        return Task.FromResult(0);
    }
}

[Command(Name = "connect", Description = "Connect to a peer")]
internal class SwarmConnectCommand : CommandBase
{
    [Argument(0, "addr", "A multiaddress to the peer")]
    [Required]
    public string Address { get; set; }

    public SwarmCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        await Program.CoreApi.Swarm.ConnectAsync(Address);
        return 0;
    }
}

[Command(Name = "disconnect", Description = "Disconnect from a peer")]
internal class SwarmDisconnectCommand : CommandBase
{
    [Argument(0, "addr", "A multiaddress to the peer")]
    [Required]
    public string Address { get; set; }

    public SwarmCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        await Program.CoreApi.Swarm.DisconnectAsync(Address);
        return 0;
    }
}

[Command(Name = "peers", Description = "List of connected peers")]
internal class SwarmPeersCommand : CommandBase
{
    public SwarmCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        IEnumerable<Peer> peers = await Program.CoreApi.Swarm.PeersAsync();
        _ = Program.Output(app, peers, (data, writer) =>
        {
            foreach (Peer peer in data)
            {
                writer.WriteLine(peer.ConnectedAddress);
            }
        });
        return 0;
    }
}

[Command(Name = "addrs", Description = "List addresses of known peers")]
internal class SwarmAddrsCommand : CommandBase
{
    public SwarmCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        IEnumerable<Peer> peers = await Program.CoreApi.Swarm.AddressesAsync();
        _ = Program.Output(app, peers, (data, writer) =>
        {
            foreach (MultiAddress address in data.SelectMany(p => p.Addresses))
            {
                writer.WriteLine(address);
            }
        });
        return 0;
    }
}