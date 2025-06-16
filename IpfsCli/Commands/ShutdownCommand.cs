using McMaster.Extensions.CommandLineUtils;

namespace Ipfs.Cli.Commands;

[Command(Name = "shutdown", Description = "Stop the IPFS daemon")]
internal class ShutdownCommand : CommandBase
{
    private Program Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        await Parent.CoreApi.Generic.ShutdownAsync();
        return 0;
    }
}