using McMaster.Extensions.CommandLineUtils;

namespace Ipfs.Cli.Commands;

[Command(Name = "version", Description = "Show version information")]
internal class VersionCommand : CommandBase
{
    public Program Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Dictionary<string, string> info = await Parent.CoreApi.Generic.VersionAsync();
        return Parent.Output(app, info, null);
    }
}