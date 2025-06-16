using Ipfs.CoreApi;
using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;

namespace Ipfs.Cli.Commands;

[Command(Name = "add", Description = "Add a file to IPFS")]
internal class AddCommand : CommandBase
{
    private static readonly AddFileOptions DefaultOptions = new();

    [Option("--chunk-size", Description = "The maximum number of bytes in a block")]
    public int ChunkSize { get; set; } = DefaultOptions.ChunkSize;

    [Option("--encoding", Description = "CID encoding algorithm")]
    public string Encoding { get; set; } = DefaultOptions.Encoding;

    [Argument(0, "path", "The path to a file to be added to ipfs")]
    [Required]
    public string FilePath { get; set; }

    [Option("--hash", Description = "The hashing algorithm")]
    public string Hash { get; set; } = DefaultOptions.Hash;

    [Option("-n|--only-hash", Description = "Only chunk and hash - do not write to disk")]
    public bool OnlyHash { get; set; } = DefaultOptions.OnlyHash;

    [Option("--pin", Description = "Pin when adding")]
    public bool Pin { get; set; } = DefaultOptions.Pin;

    [Option("-p|--progress", Description = "")]
    public bool Progress { get; set; } = false;

    [Option("--protect", Description = "protect the data with the key")]
    public string ProtectionKey { get; set; }

    [Option("--raw-leaves", Description = "Raw data for leaf nodes")]
    public bool RawLeaves { get; set; } = DefaultOptions.RawLeaves;

    [Option("-r|--recursive", Description = "Add directory paths recursively")]
    public bool Recursive { get; set; }

    [Option("-t|--trickle", Description = "Use trickle dag format")]
    public bool Trickle { get; set; } = DefaultOptions.Trickle;

    [Option("-w|--wrap", Description = "Wrap file in a directory")]
    public bool Wrap { get; set; } = DefaultOptions.Wrap;

    private Program Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        AddFileOptions options = new()
        {
            ChunkSize = ChunkSize,
            Encoding = Encoding,
            Hash = Hash,
            OnlyHash = OnlyHash,
            Pin = Pin,
            RawLeaves = RawLeaves,
            Trickle = Trickle,
            Wrap = Wrap,
            ProtectionKey = ProtectionKey
        };
        if (Progress)
        {
            options.Progress = new Progress<TransferProgress>(t =>
            {
                Console.WriteLine($"{t.Name} {t.Bytes}");
            });
        }
        IFileSystemNode node = Directory.Exists(FilePath)
            ? await Parent.CoreApi.FileSystem.AddDirectoryAsync(FilePath, Recursive, options)
            : await Parent.CoreApi.FileSystem.AddFileAsync(FilePath, options);
        return Parent.Output(app, node, (data, writer) =>
        {
            writer.WriteLine($"{data.Id.Encode()} added");
        });
    }
}