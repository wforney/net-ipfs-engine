using Common.Logging;
using Common.Logging.Simple;
using Ipfs.Cli.Commands;
using Ipfs.CoreApi;
using Ipfs.Engine;
using Ipfs.Http;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;
using System.Reflection;

namespace Ipfs.Cli;

[Command("csipfs")]
[VersionOptionFromMember("--version", MemberName = nameof(GetVersion))]
[Subcommand(typeof(InitCommand))]
[Subcommand(typeof(AddCommand))]
[Subcommand(typeof(CatCommand))]
[Subcommand(typeof(GetCommand))]
[Subcommand(typeof(LsCommand))]
[Subcommand(typeof(RefsCommand))]
[Subcommand(typeof(IdCommand))]
[Subcommand(typeof(ObjectCommand))]
[Subcommand(typeof(BlockCommand))]
[Subcommand(typeof(FilesCommand))]
[Subcommand(typeof(DaemonCommand))]
[Subcommand(typeof(ResolveCommand))]
[Subcommand(typeof(NameCommand))]
[Subcommand(typeof(KeyCommand))]
[Subcommand(typeof(DnsCommand))]
[Subcommand(typeof(PinCommand))]
[Subcommand(typeof(PubsubCommand))]
[Subcommand(typeof(BootstrapCommand))]
[Subcommand(typeof(SwarmCommand))]
[Subcommand(typeof(DhtCommand))]
[Subcommand(typeof(ConfigCommand))]
[Subcommand(typeof(VersionCommand))]
[Subcommand(typeof(ShutdownCommand))]
[Subcommand(typeof(UpdateCommand))]
[Subcommand(typeof(BitswapCommand))]
[Subcommand(typeof(StatsCommand))]
[Subcommand(typeof(RepoCommand))]
class Program : CommandBase
{
    static bool debugging;
    static bool tracing;

    public static int Main(string[] args)
    {
        var startTime = DateTime.Now;

        // Need to setup common.logging early.
        debugging = args.Any(s => s == "--debug");
        tracing = args.Any(s => s == "--trace");
        var properties = new Common.Logging.Configuration.NameValueCollection
        {
            ["level"] = tracing ? "TRACE" : (debugging ? "DEBUG" : "OFF"),
            ["showLogName"] = "true",
            ["showDateTime"] = "true",
            ["dateTimeFormat"] = "HH:mm:ss.fff"
        };
        LogManager.Adapter = new ConsoleOutLoggerFactoryAdapter(properties);

        try
        {
            CommandLineApplication.Execute<Program>(args);
        }
        catch (Exception e)
        {
            for (; e != null; e = e.InnerException)
            {
                Console.Error.WriteLine(e.Message);
                if (debugging || tracing)
                {
                    Console.WriteLine();
                    Console.WriteLine(e.StackTrace);
                }
            }
            return 1;
        }

        var took = DateTime.Now - startTime;
        //Console.Write($"Took {took.TotalSeconds} seconds.");

        return 0;
    }

    [Option("--api <url>",  Description = "Use a specific API instance")]
    public string ApiUrl { get; set;  } = IpfsClient.DefaultApiUri.ToString();

    [Option("-L|--local", Description = "Run the command locally, instead of using the daemon")]
    public bool UseLocalEngine { get; set; }

    [Option("--enc", Description = "The output type (json, xml, or text)")]
    public string OutputEncoding { get; set; } = "text";

    [Option("--debug", Description = "Show debugging info")]
    public bool Debug { get; set; }  // just for documentation, already parsed in Main

    [Option("--trace", Description = "Show tracing info")]
    public bool Trace { get; set; }  // just for documentation, already parsed in Main

    [Option("--time", Description = "Show how long the command took")]
    public bool ShowTime { get; set; }

    protected override Task<int> OnExecute(CommandLineApplication app)
    {
        app.ShowHelp();
        return Task.FromResult(0);
    }

    ICoreApi coreApi;
    public ICoreApi CoreApi
    {
        get
        {
            if (coreApi is null)
            {
                if (UseLocalEngine)
                {
                    // TODO: Add option --pass
                    string passphrase = "this is not a secure pass phrase";
                    var engine = new IpfsEngine(passphrase.ToCharArray());
                    engine.StartAsync().Wait();
                    coreApi = engine;
                }
                else
                {
                    coreApi = new IpfsClient(ApiUrl);
                }
            }

            return coreApi;
        }
    }

    public int Output<T>(CommandLineApplication app, T data, Action<T, TextWriter> text)
        where T: class
    {
        if (text is null)
        {
            OutputEncoding = "json";
        }

        switch (OutputEncoding.ToLowerInvariant())
        {
            case "text":
                text(data, app.Out);
                break;

            case "json":
                var x = new JsonSerializer
                {
                    Formatting = Formatting.Indented
                };
                x.Serialize(app.Out, data);
                break;

            default:
                app.Error.WriteLine($"Unknown output encoding '{OutputEncoding}'");
                return 1;
        }

        return 0;
    }

    private static string GetVersion() =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
}
