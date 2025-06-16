using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;

namespace Ipfs.Cli.Commands;

[Command(Name = "dht", Description = "Query the DHT for values or peers")]
[Subcommand(typeof(DhtFindPeerCommand))]
[Subcommand(typeof(DhtFindProvidersCommand))]
internal class DhtCommand : CommandBase
{
    public Program Parent { get; set; }

    protected override Task<int> OnExecute(CommandLineApplication app)
    {
        app.ShowHelp();
        return Task.FromResult(0);
    }
}

[Command(Name = "findpeer", Description = "Find the multiaddresses associated with the peer ID")]
internal class DhtFindPeerCommand : CommandBase
{
    [Argument(0, "peerid", "The IPFS peer ID")]
    [Required]
    public string PeerId { get; set; }

    private DhtCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;

        Peer peer = await Program.CoreApi.Dht.FindPeerAsync(new MultiHash(PeerId));
        return Program.Output(app, peer, (data, writer) =>
        {
            foreach (MultiAddress a in peer.Addresses)
            {
                writer.WriteLine(a.ToString());
            }
        });
    }
}

[Command(Name = "findprovs", Description = "Find peers that can provide a specific value, given a key")]
internal class DhtFindProvidersCommand : CommandBase
{
    [Argument(0, "key", "The multihash key or a CID")]
    [Required]
    public string Key { get; set; }

    [Option("-n|--num-providers", Description = "The number of providers to find")]
    public int Limit { get; set; } = 20;

    private DhtCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;

        IEnumerable<Peer> peers = await Program.CoreApi.Dht.FindProvidersAsync(Cid.Decode(Key), Limit);
        return Program.Output(app, peers, (data, writer) =>
        {
            foreach (Peer peer in peers)
            {
                writer.WriteLine(peer.Id.ToString());
            }
        });
    }
}