using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;

namespace Ipfs.Cli.Commands;

[Command(Name = "ls", Description = "List links")]
internal class LsCommand : CommandBase
{
    [Argument(0, "ipfs-path", "The path to an IPFS object")]
    [Required]
    public string IpfsPath { get; set; }

    private Program Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        IFileSystemNode node = await Parent.CoreApi.FileSystem.ListFileAsync(IpfsPath);
        return Parent.Output(app, node, (data, writer) =>
        {
            foreach (IFileSystemLink link in data.Links)
            {
                writer.WriteLine($"{link.Id.Encode()} {link.Size} {link.Name}");
            }
        });
    }
}