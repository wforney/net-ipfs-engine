using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json.Linq;
using System.ComponentModel.DataAnnotations;

namespace Ipfs.Cli.Commands;

[Command(Name = "config", Description = "Manage the configuration")]
[Subcommand(typeof(ConfigShowCommand))]
[Subcommand(typeof(ConfigReplaceCommand))]
internal class ConfigCommand : CommandBase
{
    [Argument(0, "name", "The name of the configuration setting")]
    public string Name { get; set; }

    public Program Parent { get; set; }

    [Argument(1, "value", "The value of the configuration setting")]
    public string Value { get; set; }

    [Option("--json", Description = "Treat the <value> as JSON")]
    public bool ValueIsJson { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        if (Name == null)
        {
            app.ShowHelp();
            return 0;
        }

        if (Value == null)
        {
            JToken json = await Parent.CoreApi.Config.GetAsync(Name);
            app.Out.Write(json.ToString());
            return 0;
        }

        if (ValueIsJson)
        {
            JToken value = JToken.Parse(Value);
            await Parent.CoreApi.Config.SetAsync(Name, value);
        }
        else
        {
            await Parent.CoreApi.Config.SetAsync(Name, Value);
        }
        return 0;
    }
}

[Command(Name = "show", Description = "Show the config file contents")]
internal class ConfigShowCommand : CommandBase
{
    private ConfigCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        JObject json = await Program.CoreApi.Config.GetAsync();
        app.Out.Write(json.ToString());
        return 0;
    }
}

[Command(Name = "replace", Description = "Replace the config file")]
internal class ConfigReplaceCommand : CommandBase
{
    [Argument(0, "path", "The path to the config file")]
    [Required]
    public string FilePath { get; set; }

    private ConfigCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        JObject json = JObject.Parse(File.ReadAllText(FilePath));
        await Program.CoreApi.Config.ReplaceAsync(json);
        return 0;
    }
}