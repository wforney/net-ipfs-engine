using Ipfs.Engine.UnixFileSystem;
using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;

namespace Ipfs.Cli.Commands;

[Command(Name = "object", Description = "Manage IPFS objects")]
[Subcommand(typeof(ObjectLinksCommand))]
[Subcommand(typeof(ObjectGetCommand))]
[Subcommand(typeof(ObjectDumpCommand))]
[Subcommand(typeof(ObjectStatCommand))]
internal class ObjectCommand : CommandBase
{
    public Program Parent { get; set; }

    protected override Task<int> OnExecute(CommandLineApplication app)
    {
        app.ShowHelp();
        return Task.FromResult(0);
    }
}

[Command(Name = "dump", Description = "Dump the DAG node")]
internal class ObjectDumpCommand : CommandBase
{
    [Argument(0, "cid", "The content ID of the object")]
    [Required]
    public string Cid { get; set; }

    private ObjectCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        Node node = new();
        IDataBlock block = await Program.CoreApi.Block.GetAsync(Cid);
        node.Dag = new DagNode(block.DataStream);
        node.DataMessage = ProtoBuf.Serializer.Deserialize<DataMessage>(node.Dag.DataStream);

        return Program.Output(app, node, null);
    }

    private class Node
    {
        public DagNode Dag;
        public DataMessage DataMessage;
    }
}

[Command(Name = "get", Description = "Serialise the DAG node")]
internal class ObjectGetCommand : CommandBase
{
    [Argument(0, "cid", "The content ID of the object")]
    [Required]
    public string Cid { get; set; }

    private ObjectCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        DagNode node = await Program.CoreApi.Object.GetAsync(Cid);

        return Program.Output(app, node, null);
    }
}

[Command(Name = "links", Description = "Information on the links pointed to by the IPFS block")]
internal class ObjectLinksCommand : CommandBase
{
    [Argument(0, "cid", "The content ID of the object")]
    [Required]
    public string Cid { get; set; }

    private ObjectCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        IEnumerable<IMerkleLink> links = await Program.CoreApi.Object.LinksAsync(Cid);

        return Program.Output(app, links, (data, writer) =>
        {
            foreach (IMerkleLink link in data)
            {
                writer.WriteLine($"{link.Id.Encode()} {link.Size} {link.Name}");
            }
        });
    }
}

[Command(Name = "stat", Description = "Stats for the DAG node")]
internal class ObjectStatCommand : CommandBase
{
    [Argument(0, "cid", "The content ID of the object")]
    [Required]
    public string Cid { get; set; }

    private ObjectCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        CoreApi.ObjectStat stat = await Program.CoreApi.Object.StatAsync(Cid);

        return Program.Output(app, stat, null);
    }
}