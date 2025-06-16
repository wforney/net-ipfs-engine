using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;

namespace Ipfs.Cli.Commands;

[Command(Name = "key", Description = "Manage private keys")]
[Subcommand(typeof(KeyListCommand))]
[Subcommand(typeof(KeyRemoveCommand))]
[Subcommand(typeof(KeyCreateCommand))]
[Subcommand(typeof(KeyRenameCommand))]
[Subcommand(typeof(KeyExportCommand))]
[Subcommand(typeof(KeyImportCommand))]
internal class KeyCommand : CommandBase
{
    public Program Parent { get; set; }

    protected override Task<int> OnExecute(CommandLineApplication app)
    {
        app.ShowHelp();
        return Task.FromResult(0);
    }
}

[Command(Name = "gen", Description = "Create a key")]
internal class KeyCreateCommand : CommandBase
{
    [Option("-s|--size", Description = "The key size")]
    public int KeySize { get; set; }

    [Option("-t|--type", Description = "The type of the key [rsa, ed25519]")]
    public string KeyType { get; set; } = "rsa";

    [Argument(0, "name", "The name of the key")]
    [Required]
    public string Name { get; set; }

    private KeyCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        IKey key = await Program.CoreApi.Key.CreateAsync(Name, KeyType, KeySize);
        return Program.Output(app, key, (data, writer) =>
        {
            writer.WriteLine($"{data.Id} {data.Name}");
        });
    }
}

[Command(Name = "export", Description = "Export the key to a PKCS #8 PEM file")]
internal class KeyExportCommand : CommandBase
{
    [Argument(0, "name", "The name of the key")]
    [Required]
    public string Name { get; set; }

    [Option("-o|--output", Description = "The file name for the PEM file")]
    public string OutputBasePath { get; set; }

    private KeyCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        string pass = Prompt.GetPassword("Password for PEM file?");
        string pem = await Program.CoreApi.Key.ExportAsync(Name, pass.ToCharArray());
        if (OutputBasePath == null)
        {
            app.Out.Write(pem);
        }
        else
        {
            string path = OutputBasePath;
            if (!Path.HasExtension(path))
            {
                path = Path.ChangeExtension(path, ".pem");
            }

            using StreamWriter writer = File.CreateText(path);
            writer.Write(pem);
        }

        return 0;
    }
}

[Command(Name = "list", Description = "List the keys")]
internal class KeyListCommand : CommandBase
{
    private KeyCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        IEnumerable<IKey> keys = await Program.CoreApi.Key.ListAsync();
        return Program.Output(app, keys, (data, writer) =>
        {
            foreach (IKey key in data)
            {
                writer.WriteLine($"{key.Id} {key.Name}");
            }
        });
    }
}

[Command(Name = "rm", Description = "Remove the key")]
internal class KeyRemoveCommand : CommandBase
{
    [Argument(0, "name", "The name of the key")]
    [Required]
    public string Name { get; set; }

    private KeyCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        IKey key = await Program.CoreApi.Key.RemoveAsync(Name);
        if (key == null)
        {
            app.Error.WriteLine($"The key '{Name}' is not defined.");
            return 1;
        }

        return Program.Output(app, key, (data, writer) =>
        {
            writer.WriteLine($"Removed {data.Id} {data.Name}");
        });
    }
}

[Command(Name = "rename", Description = "Rename the key")]
internal class KeyRenameCommand : CommandBase
{
    [Argument(0, "name", "The name of the key")]
    [Required]
    public string Name { get; set; }

    [Argument(1, "new-name", "The new name of the key")]
    [Required]
    public string NewName { get; set; }

    private KeyCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        IKey key = await Program.CoreApi.Key.RenameAsync(Name, NewName);
        if (key == null)
        {
            app.Error.WriteLine($"The key '{Name}' is not defined.");
            return 1;
        }

        return Program.Output(app, key, (data, writer) =>
        {
            writer.WriteLine($"Renamed to {data.Name}");
        });
    }
}

[Command(Name = "import", Description = "Import the key from a PKCS #8 PEM file")]
internal class KeyImportCommand : CommandBase
{
    [Argument(0, "name", "The name of the key")]
    [Required]
    public string Name { get; set; }

    [Argument(1, "path", "The path to the PEM file")]
    [Required]
    public string PemPath { get; set; }

    private KeyCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        string pem = File.ReadAllText(PemPath);
        string pass = Prompt.GetPassword("Password for PEM file?");
        IKey key = await Program.CoreApi.Key.ImportAsync(Name, pem, pass.ToCharArray());
        return Program.Output(app, key, (data, writer) =>
        {
            writer.WriteLine($"{data.Id} {data.Name}");
        });
    }
}