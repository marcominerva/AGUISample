using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AGUIServer.ContextProviders;

public class UserContextProvider : MessageAIContextProvider
{
    protected override ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        // Get relevant information from a knowledge base or other source. Here we hardcode it for simplicity.
        return ValueTask.FromResult<IEnumerable<ChatMessage>>(
            [new(ChatRole.User, $"Current UTC date time: {DateTime.UtcNow}")]
        );
    }
}