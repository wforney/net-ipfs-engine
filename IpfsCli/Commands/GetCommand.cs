using McMaster.Extensions.CommandLineUtils;
using Nito.AsyncEx;
using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Threading.Tasks.Dataflow;

namespace Ipfs.Cli.Commands;

[Command(Name = "get", Description = "Download IPFS data")]
internal class GetCommand : CommandBase
{
    private readonly AsyncLock ZipLock = new();

    private ActionBlock<IpfsFile> fetch;

    private int processed = 0;

    // when requested equals processed then the task is done.
    private int requested = 1;

    // ZipArchive is NOT thread safe
    private ZipArchive zip;

    [Option("-c|--compress", Description = "Create a ZIP compressed file")]
    public bool Compress { get; set; }

    [Argument(0, "ipfs-path", "The path to the IPFS data")]
    [Required]
    public string IpfsPath { get; set; }

    [Option("-o|--output", Description = "The output path for the data")]
    public string OutputBasePath { get; set; }

    private Program Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        OutputBasePath ??= Path.Combine(".", IpfsPath);

        if (Compress)
        {
            string zipPath = Path.GetFullPath(OutputBasePath);
            if (!Path.HasExtension(zipPath))
            {
                zipPath = Path.ChangeExtension(zipPath, ".zip");
            }

            app.Out.WriteLine($"Saving to {zipPath}");
            zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        }

        try
        {
            ExecutionDataflowBlockOptions options = new()
            {
                MaxDegreeOfParallelism = 10
            };
            fetch = new ActionBlock<IpfsFile>(FetchFileOrDirectory, options);
            IpfsFile first = new()
            {
                Path = zip == null ? "" : IpfsPath,
                Node = await Parent.CoreApi.FileSystem.ListFileAsync(IpfsPath)
            };
            _ = fetch.Post(first);
            await fetch.Completion;
        }
        finally
        {
            zip?.Dispose();
        }
        return 0;
    }

    private async Task FetchFileOrDirectory(IpfsFile file)
    {
        if (file.Node.IsDirectory)
        {
            foreach (IFileSystemLink link in file.Node.Links)
            {
                IpfsFile next = new()
                {
                    Path = Path.Combine(file.Path, link.Name),
                    Node = await Parent.CoreApi.FileSystem.ListFileAsync(link.Id)
                };
                ++requested;
                _ = fetch.Post(next);
            }
        }
        else
        {
            if (zip != null)
            {
                await SaveToZip(file);
            }
            else
            {
                await SaveToDisk(file);
            }
        }

        if (++processed == requested)
        {
            fetch.Complete();
        }
    }

    private async Task SaveToDisk(IpfsFile file)
    {
        string outputPath = Path.GetFullPath(Path.Combine(OutputBasePath, file.Path));
        _ = Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        using Stream instream = await Parent.CoreApi.FileSystem.ReadFileAsync(file.Node.Id);
        using FileStream outstream = File.Create(outputPath);
        await instream.CopyToAsync(outstream);
    }

    private async Task SaveToZip(IpfsFile file)
    {
        using (Stream instream = await Parent.CoreApi.FileSystem.ReadFileAsync(file.Node.Id))
        using (await ZipLock.LockAsync())
        using (Stream entryStream = zip.CreateEntry(file.Path).Open())
        {
            await instream.CopyToAsync(entryStream);
        }
    }

    private class IpfsFile
    {
        public IFileSystemNode Node;
        public string Path;
    }
}