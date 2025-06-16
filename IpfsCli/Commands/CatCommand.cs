using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;

namespace Ipfs.Cli.Commands;

[Command(Name = "cat", Description = "Show IPFS file data")]
internal class CatCommand : CommandBase
{
    [Argument(0, "ref", "The IPFS path to the data")]
    [Required]
    public string IpfsPath { get; set; }

    [Option("-l|--length", Description = "Maximum number of bytes to read")]
    public long Length { get; set; }

    [Option("-o|--offset", Description = "Byte offset to begin reading from")]
    public long Offset { get; set; }

    private Program Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        using Stream stream = await Parent.CoreApi.FileSystem.ReadFileAsync(IpfsPath, Offset, Length);
        Stream stdout = Console.OpenStandardOutput();
        await stream.CopyToAsync(stdout);
        return 0;
    }
}