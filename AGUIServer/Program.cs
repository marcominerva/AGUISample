using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using AgentBasicService.SessionStores;
using AgentBasicService.Settings;
using AgentBasicService.Tracing;
using AGUIServer.ContextProviders;
using AGUIServer.Services;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Responses;
using TinyHelpers.AspNetCore.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

// Add services to the container.
builder.Services.AddHttpContextAccessor();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddHttpClient<WeatherService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration.GetValue<string>("OpenWeatherMap:BaseUrl")!);
});

var openAISettings = builder.Services.ConfigureAndGet<AzureOpenAISettings>(builder.Configuration, "AzureOpenAI")!;
builder.Services.AddChatClient(_ =>
{
    // Endpoint must end with /openai/v1 for Azure OpenAI
    var openAIClient = new OpenAIClient(new ApiKeyCredential(openAISettings.ApiKey), new()
    {
        Endpoint = new(openAISettings.Endpoint),
        Transport = new HttpClientPipelineTransport(new(new TraceHttpClientHandler()))
    });

    return openAIClient.GetResponsesClient().AsIChatClientWithStoredOutputDisabled(openAISettings.Deployment);
});

builder.Services.AddAIAgent("Default", (services, key) =>
{
    var httpContextAccessor = services.GetRequiredService<IHttpContextAccessor>();
    var chatClient = services.GetRequiredService<IChatClient>();

    var chatHistoryProvider = new InMemoryChatHistoryProvider(new()
    {
        ChatReducer = new MessageCountingChatReducer(20), //new SummarizingChatReducer(chatClient, 1, 10)
        ReducerTriggerEvent = InMemoryChatHistoryProviderOptions.ChatReducerTriggerEvent.AfterMessageAdded,
        StorageInputRequestMessageFilter = messages =>
        {
            return messages.Where(m => m.GetAgentRequestMessageSourceType() != AgentRequestMessageSourceType.ChatHistory
                && m.GetAgentRequestMessageSourceType() != AgentRequestMessageSourceType.AIContextProvider);
        }
    });

    return chatClient.AsAIAgent(new()
    {
        Id = key.ToLowerInvariant(),
        Name = key,
        ChatOptions = new()
        {
            Instructions = "You are a helpful assistant that provides concise and accurate information.",
            Tools = [new HostedWebSearchTool(),
            AIFunctionFactory.Create(services.GetRequiredService<WeatherService>().GetCurrentWeatherAsync,"get_current_weather"),
            AIFunctionFactory.Create(services.GetRequiredService<WeatherService>().GetWeatherForecastAsync,"get_weather_forecast")
            ]
        },
        AIContextProviders = [new UserContextProvider()],
        ChatHistoryProvider = chatHistoryProvider
    },
    loggerFactory: services.GetRequiredService<ILoggerFactory>(),
    services: services);
}, ServiceLifetime.Scoped)
.WithSessionStore((services, key) =>
{
    var agentSessionStore = new InMemorySessionStore();
    return agentSessionStore;
}, withIsolation: false);

builder.Services.AddAGUIServer();

builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseHttpsRedirection();

app.MapOpenApi();
app.MapSwaggerUI(setupAction: options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", app.Environment.ApplicationName);
});

app.MapPost("/api/chat", async (ChatRequest request, [FromKeyedServices("Default")] AIAgent agent, [FromKeyedServices("Default")] AgentSessionStore store, CancellationToken cancellationToken) =>
{
    var conversationId = request.ConversationId ?? Guid.NewGuid().ToString("N");
    var session = await store.GetOrCreateSessionAsync(agent, new(conversationId), cancellationToken);

    var response = await agent.RunAsync(request.Message, session, cancellationToken: cancellationToken);

    await store.SaveSessionAsync(agent, new(conversationId), session, cancellationToken);

    return TypedResults.Ok(new ChatResponse(conversationId, response.Text, response.Usage?.TotalTokenCount));
});

app.MapPost("/api/chat/streaming", async (ChatRequest request, [FromKeyedServices("Default")] AIAgent agent, [FromKeyedServices("Default")] AgentSessionStore store, CancellationToken cancellationToken) =>
{
    async IAsyncEnumerable<SseItem<ChatResponse>> StreamAsync([EnumeratorCancellation] CancellationToken innerCancellationToken)
    {
        var conversationId = request.ConversationId ?? Guid.NewGuid().ToString("N");
        var session = await store.GetOrCreateSessionAsync(agent, new(conversationId), innerCancellationToken);

        var updates = new List<AgentResponseUpdate>();

        yield return new(new(conversationId, null), "start");

        await foreach (var update in agent.RunStreamingAsync(request.Message, session, cancellationToken: innerCancellationToken))
        {
            updates.Add(update);
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return new(new(null, update.Text), "delta");
            }
        }

        await store.SaveSessionAsync(agent, new(conversationId), session, innerCancellationToken);
        var response = updates.ToAgentResponse();

        yield return new(new(null, null, response.Usage?.TotalTokenCount), "metadata");
    }

    return TypedResults.ServerSentEvents(StreamAsync(cancellationToken));
});

app.MapAGUIServer("/agent", context =>
{
    return context.RequestServices.GetRequiredKeyedService<AIAgent>("Default");
}).ExcludeFromDescription();

app.Run();