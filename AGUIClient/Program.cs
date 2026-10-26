using AGUI.Abstractions;
using AGUI.Client;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

using var httpClient = new HttpClient()
{
    BaseAddress = new Uri("https://localhost:7017/")
};

var chatClient = new AGUIChatClient(new(httpClient, "/agent"));
var agent = chatClient.AsAIAgent(name: "agui-client");

var session = await agent.CreateSessionAsync();

var threadId = Guid.NewGuid().ToString("N");
var options = new ChatClientAgentRunOptions(new()
{
    RawRepresentationFactory = _ => new RunAgentInput { ThreadId = threadId }
});

while (true)
{
    Console.Write("Question: ");
    var question = Console.ReadLine()!;

    //var response = await agent.RunAsync(question, options: options);
    //Console.WriteLine(response);

    var response = agent.RunStreamingAsync(question, options: options);
    await foreach (var message in response)
    {
        Console.Write(message);
    }

    session.TryGetInMemoryChatHistory(out var chatHistory);

    Console.WriteLine();
    Console.WriteLine();
}