using System.Runtime.CompilerServices;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;

namespace Passenger.Lsp;

/// <summary>The connection's header-delimited messages, the server's words that its colours moved counted as each is read
/// off it: the client handles what the server sends one message at a time, in order, so a word handled after a request
/// went out may have arrived before it (<see cref="LanguageServer.ColourRefreshes"/>).</summary>
internal sealed class CountedMessages(Stream writer, Stream reader, IJsonRpcMessageFormatter formatter)
    : HeaderDelimitedMessageHandler(writer, reader, formatter)
{
    private int colourRefreshes;

    public int ColourRefreshes => Volatile.Read(ref colourRefreshes);

    // Pooled: every message the server sends passes here, most finishing later than they began.
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    protected override async ValueTask<JsonRpcMessage?> ReadCoreAsync(CancellationToken cancellationToken)
    {
        JsonRpcMessage? message = await base.ReadCoreAsync(cancellationToken).ConfigureAwait(false);
        if (message is JsonRpcRequest { Method: "workspace/semanticTokens/refresh" })
        {
            Interlocked.Increment(ref colourRefreshes);
        }
        return message;
    }
}
