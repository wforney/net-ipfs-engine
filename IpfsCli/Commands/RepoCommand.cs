using Ipfs.Engine;
using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;

namespace Ipfs.Cli.Commands;

[Command(Name = "repo", Description = "Manage the IPFS repository")]
[Subcommand(typeof(RepoGCCommand))]
[Subcommand(typeof(RepoMigrateCommand))]
[Subcommand(typeof(RepoStatCommand))]
[Subcommand(typeof(RepoVerifyCommand))]
[Subcommand(typeof(RepoVersionCommand))]
internal class RepoCommand : CommandBase
{
    public Program Parent { get; set; }

    protected override Task<int> OnExecute(CommandLineApplication app)
    {
        app.ShowHelp();
        return Task.FromResult(0);
    }
}

[Command(Name = "gc", Description = "Perform a garbage collection sweep on the repo")]
internal class RepoGCCommand : CommandBase
{
    private RepoCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;

        await Program.CoreApi.BlockRepository.RemoveGarbageAsync();
        return 0;
    }
}

[Command(Name = "stat", Description = "Repository information")]
internal class RepoStatCommand : CommandBase
{
    private RepoCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;

        CoreApi.RepositoryData stats = await Program.CoreApi.BlockRepository.StatisticsAsync();
        return Program.Output(app, stats, null);
    }
}

[Command(Name = "verify", Description = "Verify all blocks in repo are not corrupted")]
internal class RepoVerifyCommand : CommandBase
{
    private RepoCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;

        await Program.CoreApi.BlockRepository.VerifyAsync();
        return 0;
    }
}

[Command(Name = "version", Description = "Repository version")]
internal class RepoVersionCommand : CommandBase
{
    private RepoCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;

        string stats = await Program.CoreApi.BlockRepository.VersionAsync();
        return Program.Output(app, stats, null);
    }
}

[Command(Name = "migrate", Description = "Migrate to the version")]
internal class RepoMigrateCommand : CommandBase
{
    [Argument(0, "version", "The version number of the repository")]
    [Required]
    public int Version { get; set; }

    private RepoCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        // TODO: Add option --pass
        string passphrase = "this is not a secure pass phrase";
        IpfsEngine ipfs = new(passphrase.ToCharArray());

        await ipfs.MigrationManager.MirgrateToVersionAsync(Version);
        return 0;
    }
}