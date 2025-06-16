using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;

namespace Ipfs.Cli.Commands;

[Command(Name = "block", Description = "Manage raw blocks")]
[Subcommand(typeof(BlockStatCommand))]
[Subcommand(typeof(BlockRemoveCommand))]
[Subcommand(typeof(BlockGetCommand))]
[Subcommand(typeof(BlockPutCommand))]
internal class BlockCommand : CommandBase
{
    public Program Parent { get; set; }

    protected override Task<int> OnExecute(CommandLineApplication app)
    {
        app.ShowHelp();
        return Task.FromResult(0);
    }
}

[Command(Name = "get", Description = "Get the IPFS block")]
internal class BlockGetCommand : CommandBase
{
    [Argument(0, "cid", "The content ID of the block")]
    [Required]
    public string Cid { get; set; }

    private BlockCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        IDataBlock block = await Program.CoreApi.Block.GetAsync(Cid);
        await block.DataStream.CopyToAsync(Console.OpenStandardOutput());

        return 0;
    }
}

[Command(Name = "rm", Description = "Remove the IPFS block")]
internal class BlockRemoveCommand : CommandBase
{
    [Argument(0, "cid", "The content ID of the block")]
    [Required]
    public string Cid { get; set; }

    [Option("-f|-force", Description = "Ignore nonexistent blocks")]
    public bool Force { get; set; }

    private BlockCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        Cid cid = await Program.CoreApi.Block.RemoveAsync(Cid, Force);

        return Program.Output(app, cid, (data, writer) =>
        {
            writer.WriteLine($"Removed {data.Encode()}");
        });
    }
}

[Command(Name = "stat", Description = "Information on on the IPFS block")]
internal class BlockStatCommand : CommandBase
{
    [Argument(0, "cid", "The content ID of the block")]
    [Required]
    public string Cid { get; set; }

    private BlockCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        IDataBlock block = await Program.CoreApi.Block.StatAsync(Cid);

        return Program.Output(app, block, (data, writer) =>
        {
            writer.WriteLine($"{data.Id.Encode()} {data.Size}");
        });
    }
}

[Command(Name = "put", Description = "Put the IPFS block")]
internal class BlockPutCommand : CommandBase
{
    [Argument(0, "path", "The file containing the data")]
    [Required]
    public string BlockPath { get; set; }

    [Option("--hash", Description = "The hashing algorithm")]
    public string MultiHashType { get; set; } = MultiHash.DefaultAlgorithmName;

    [Option("--pin", Description = "Pin the block")]
    public bool Pin { get; set; }

    private BlockCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        byte[] blockData = File.ReadAllBytes(BlockPath);
        Cid cid = await Program.CoreApi.Block.PutAsync
        (
            data: blockData,
            pin: Pin,
            multiHash: MultiHashType
        );

        return Program.Output(app, cid, (data, writer) =>
        {
            writer.WriteLine($"Added {data.Encode()}");
        });
    }
}