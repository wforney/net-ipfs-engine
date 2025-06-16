using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;

namespace Ipfs.Cli.Commands;

[Command(Name = "bitswap", Description = "Manage swapped blocks")]
[Subcommand(typeof(BitswapWantListCommand))]
[Subcommand(typeof(BitswapUnwantCommand))]
[Subcommand(typeof(BitswapLedgerCommand))]
[Subcommand(typeof(BitswapStatCommand))]
internal class BitswapCommand : CommandBase
{
    public Program Parent { get; set; }

    protected override Task<int> OnExecute(CommandLineApplication app)
    {
        app.ShowHelp();
        return Task.FromResult(0);
    }
}

[Command(Name = "ledger", Description = "Show the current ledger for a peer")]
internal class BitswapLedgerCommand : CommandBase
{
    [Argument(0, "peerid", "The PeerID (B58) of the ledger to inspect")]
    [Required]
    public string PeerId { get; set; }

    private BitswapCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        Peer peer = new() { Id = PeerId };
        CoreApi.BitswapLedger ledger = await Program.CoreApi.Bitswap.LedgerAsync(peer);
        return Program.Output(app, ledger, null);
    }
}

[Command(Name = "unwant", Description = "Remove a block from the wantlist")]
internal class BitswapUnwantCommand : CommandBase
{
    [Argument(0, "cid", "The content ID of the block")]
    [Required]
    public string Cid { get; set; }

    private BitswapCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        await Program.CoreApi.Bitswap.UnwantAsync(Cid);
        return 0;
    }
}

[Command(Name = "wantlist", Description = "Show blocks currently on the wantlist")]
internal class BitswapWantListCommand : CommandBase
{
    [Option("-p|--peer", Description = "Peer to show wantlist for. Default: self.")]
    public string PeerId { get; set; }

    private BitswapCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        MultiHash peer = PeerId == null
            ? null
            : new MultiHash(PeerId);
        IEnumerable<Cid> cids = await Program.CoreApi.Bitswap.WantsAsync(peer);
        return Program.Output(app, cids, (data, writer) =>
        {
            foreach (Cid cid in data)
            {
                writer.WriteLine(cid.Encode());
            }
        });
    }
}

[Command(Name = "stat", Description = "Show bitswap information")]
internal class BitswapStatCommand : CommandBase
{
    private BitswapCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;

        CoreApi.BitswapData stats = await Program.CoreApi.Stats.BitswapAsync();
        return Program.Output(app, stats, null);
    }
}