using McMaster.Extensions.CommandLineUtils;

namespace Ipfs.Cli.Commands;

[Command(Name = "add", Description = "Add the bootstrap peer")]
[Subcommand(typeof(BootstrapAddDefaultCommand))]
internal class BootstrapAddCommand : CommandBase
{
    [Argument(0, "addr", "A multiaddress to the peer")]
    public string Address { get; set; }

    public BootstrapCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        _ = await Program.CoreApi.Bootstrap.AddAsync(Address);
        return 0;
    }
}

[Command(Name = "default", Description = "Add the default bootstrap peers")]
internal class BootstrapAddDefaultCommand : CommandBase
{
    private BootstrapAddCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent.Parent;
        IEnumerable<MultiAddress> peers = await Program.CoreApi.Bootstrap.AddDefaultsAsync();
        return Program.Output(app, peers, (data, writer) =>
        {
            foreach (MultiAddress a in data)
            {
                writer.WriteLine(a);
            }
        });
    }
}

[Command(Name = "bootstrap", Description = "Manage bootstrap peers")]
[Subcommand(typeof(BootstrapListCommand))]
[Subcommand(typeof(BootstrapRemoveCommand))]
[Subcommand(typeof(BootstrapAddCommand))]
internal class BootstrapCommand : CommandBase
{
    public Program Parent { get; set; }

    protected override Task<int> OnExecute(CommandLineApplication app)
    {
        app.ShowHelp();
        return Task.FromResult(0);
    }
}

[Command(Name = "list", Description = "List the bootstrap peers")]
internal class BootstrapListCommand : CommandBase
{
    private BootstrapCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        IEnumerable<MultiAddress> peers = await Program.CoreApi.Bootstrap.ListAsync();
        return Program.Output(app, peers, (data, writer) =>
        {
            foreach (MultiAddress addresss in data)
            {
                writer.WriteLine(addresss);
            }
        });
    }
}

[Command(Name = "rm", Description = "Remove the bootstrap peer")]
[Subcommand(typeof(BootstrapRemoveAllCommand))]
internal class BootstrapRemoveCommand : CommandBase
{
    [Argument(0, "addr", "A multiaddress to the peer")]
    public string Address { get; set; }

    public BootstrapCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        _ = await Program.CoreApi.Bootstrap.RemoveAsync(Address);
        return 0;
    }
}

[Command(Name = "all", Description = "Remove all the bootstrap peers")]
internal class BootstrapRemoveAllCommand : CommandBase
{
    private BootstrapRemoveCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent.Parent;
        await Program.CoreApi.Bootstrap.RemoveAllAsync();
        return 0;
    }
}