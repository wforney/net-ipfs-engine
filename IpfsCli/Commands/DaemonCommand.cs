using McMaster.Extensions.CommandLineUtils;

namespace Ipfs.Cli.Commands;

[Command(Name = "daemon", Description = "Start a long running IPFS deamon")]
internal class DaemonCommand : CommandBase // TODO
{
    private Program Parent { get; set; }

    protected override Task<int> OnExecute(CommandLineApplication app)
    {
        Server.Program.Main([]);
        return Task.FromResult(0);
    }
}