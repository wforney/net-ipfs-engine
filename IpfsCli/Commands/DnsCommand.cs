using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;

namespace Ipfs.Cli.Commands;

[Command(Name = "dns", Description = "Resolve DNS link")]
internal class DnsCommand : CommandBase
{
    [Argument(0, "domain-name", "The DNS domain name")]
    [Required]
    public string Name { get; set; }

    [Option("-r|--recursive", Description = "Resolve until the result is not a DNS link")]
    public bool Recursive { get; set; }

    private Program Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        string result = await Parent.CoreApi.Dns.ResolveAsync(Name, Recursive);
        app.Out.Write(result);
        return 0;
    }
}