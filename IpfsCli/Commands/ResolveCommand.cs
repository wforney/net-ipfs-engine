using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;

namespace Ipfs.Cli.Commands;

[Command(Name = "resolve", Description = "Resolve any type of name")]
internal class ResolveCommand : CommandBase
{
    [Argument(0, "name", "The IPFS/IPNS/... name")]
    [Required]
    public string Name { get; set; }

    [Option("-r|--recursive", Description = "Resolve until the result is an IPFS name")]
    public bool Recursive { get; set; }

    private Program Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        string result = await Parent.CoreApi.Generic.ResolveAsync(Name, Recursive);
        app.Out.Write(result);
        return 0;
    }
}