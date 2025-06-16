using Ipfs.CoreApi;
using Makaretu.Dns;
using PeerTalk;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ipfs.Engine.CoreApi
{
    internal class DnsApi(IpfsEngine ipfs) : IDnsApi
    {
        public async Task<string> ResolveAsync(string name, bool recursive = false, CancellationToken cancel = default)
        {
            // Find the TXT dnslink in either <name> or _dnslink.<name>.
            string link = null;
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                try
                {
                    Task<string>[] attempts =
                    [
                        FindAsync(name, cts.Token),
                        FindAsync("_dnslink." + name, cts.Token)
                    ];
                    link = await TaskHelper.WhenAnyResultAsync(attempts, cancel).ConfigureAwait(false);
                    await cts.CancelAsync();
                }
                catch (Exception e)
                {
                    throw new NotSupportedException($"Cannot resolve '{name}'.", e);
                }
            }

            return !recursive || link.StartsWith("/ipfs/")
                ? link
                : link.StartsWith("/ipns/")
                ? await ipfs.Name.ResolveAsync(link, recursive, false, cancel).ConfigureAwait(false)
                : throw new NotSupportedException($"Cannot resolve '{link}'.");
        }

        private async Task<string> FindAsync(string name, CancellationToken cancel)
        {
            Message response = await ipfs.Options.Dns.QueryAsync(name, DnsType.TXT, cancel).ConfigureAwait(false);
            string link = response.Answers
                .OfType<TXTRecord>()
                .SelectMany(txt => txt.Strings)
                .Where(s => s.StartsWith("dnslink="))
                .Select(s => s[8..])
                .FirstOrDefault();

            return link ?? throw new Exception($"'{name}' is missing a TXT record with a dnslink.");
        }
    }
}