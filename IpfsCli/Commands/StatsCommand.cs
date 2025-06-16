using McMaster.Extensions.CommandLineUtils;

namespace Ipfs.Cli.Commands;

[Command(Name = "bw", Description = "IPFS bandwidth information")]
internal class StatsBandwidthCommand : CommandBase
{
    private StatsCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;

        CoreApi.BandwidthData stats = await Program.CoreApi.Stats.BandwidthAsync();
        return Program.Output(app, stats, null);
    }
}

[Command(Name = "stats", Description = "Query IPFS statistics")]
[Subcommand(typeof(StatsBandwidthCommand))]
[Subcommand(typeof(StatsRepoCommand))]
[Subcommand(typeof(StatsBitswapCommand))]
internal class StatsCommand : CommandBase
{
    public Program Parent { get; set; }

    protected override Task<int> OnExecute(CommandLineApplication app)
    {
        app.ShowHelp();
        return Task.FromResult(0);
    }
}

[Command(Name = "repo", Description = "Repository information")]
internal class StatsRepoCommand : CommandBase
{
    private StatsCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;

        CoreApi.RepositoryData stats = await Program.CoreApi.Stats.RepositoryAsync();
        return Program.Output(app, stats, null);
    }
}

[Command(Name = "bitswap", Description = "Bitswap information")]
internal class StatsBitswapCommand : CommandBase
{
    private StatsCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;

        CoreApi.BitswapData stats = await Program.CoreApi.Stats.BitswapAsync();
        return Program.Output(app, stats, null);
    }
}