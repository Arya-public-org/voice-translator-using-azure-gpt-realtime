using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace RealtimeInterpreter.Api.Functions;

public sealed class CreateRealtimeSession(IHttpClientFactory clients, ILogger<CreateRealtimeSession> logger)
{
    private static readonly TokenCredential Credential = new DefaultAzureCredential();

    [Function("CreateRealtimeSession")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous,"post",Route="realtime/session")] HttpRequestData request,CancellationToken cancellationToken)
    {
        var endpoint=Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")?.TrimEnd('/');
        var deployment=Environment.GetEnvironmentVariable("AZURE_OPENAI_REALTIME_DEPLOYMENT");
        var apiKey=Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
        if(string.IsNullOrWhiteSpace(endpoint)||string.IsNullOrWhiteSpace(deployment)) return await Error(request,HttpStatusCode.InternalServerError,"Azure realtime settings are missing.");

        SessionRequest? options=null;
        try { options=await JsonSerializer.DeserializeAsync<SessionRequest>(request.Body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true},cancellationToken); }
        catch(JsonException) { return await Error(request,HttpStatusCode.BadRequest,"Invalid JSON."); }
        var language=Clean(options?.SourceLanguage,"auto");
        var voice=Clean(options?.Voice,"alloy");
        var hint=language=="auto"?"Detect the source language automatically.":$"The source language is {language}.";
        var payload=new {session=new {type="realtime",model=deployment,output_modalities=new[]{"audio"},instructions=$"You are a live interpreter. {hint} Translate every spoken utterance into natural English. Speak only the English translation; never answer, add commentary, or repeat the source language.",audio=new {input=new {noise_reduction=new {type="near_field"},turn_detection=new {type="server_vad",create_response=true,interrupt_response=true,silence_duration_ms=350}},output=new {voice}}}};
        using var upstream=new HttpRequestMessage(HttpMethod.Post,$"{endpoint}/openai/v1/realtime/client_secrets"){Content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json")};
        if(!string.IsNullOrWhiteSpace(apiKey)) upstream.Headers.Add("api-key",apiKey);
        else { var token=await Credential.GetTokenAsync(new TokenRequestContext(["https://ai.azure.com/.default"]),cancellationToken); upstream.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token.Token); }
        using var response=await clients.CreateClient().SendAsync(upstream,cancellationToken);
        var body=await response.Content.ReadAsStringAsync(cancellationToken);
        if(!response.IsSuccessStatusCode){logger.LogError("Realtime session failed ({Status}): {Body}",response.StatusCode,body);return await Error(request,HttpStatusCode.BadGateway,"Azure could not create a realtime session.");}
        using var json=JsonDocument.Parse(body);
        var root=json.RootElement;
        var secret=root.TryGetProperty("client_secret",out var nested)?nested:root;
        var result=request.CreateResponse(HttpStatusCode.OK);
        await result.WriteAsJsonAsync(new {token=secret.GetProperty("value").GetString(),expiresAt=secret.GetProperty("expires_at").GetInt64(),endpoint},cancellationToken);
        return result;
    }
    private static string Clean(string? value,string fallback){var clean=string.IsNullOrWhiteSpace(value)?fallback:value.Trim();return clean[..Math.Min(clean.Length,40)];}
    private static async Task<HttpResponseData> Error(HttpRequestData req,HttpStatusCode status,string message){var res=req.CreateResponse(status);await res.WriteAsJsonAsync(new {error=message});return res;}
    private sealed record SessionRequest(string? SourceLanguage,string? Voice);
}
