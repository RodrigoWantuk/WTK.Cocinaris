using Cocinaris.Modules.Planning.Application;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Options;

namespace Cocinaris.Api.Planning;

/// <summary>Web push settings (ADR-0013), from <c>Planning__WebPush__*</c>. Secret: never commit them.</summary>
public sealed class WebPushOptions
{
    /// <summary>Gets or sets the VAPID public key, base64url.</summary>
    public string? PublicKey { get; set; }

    /// <summary>Gets or sets the VAPID private key, base64url.</summary>
    public string? PrivateKey { get; set; }

    /// <summary>Gets or sets the contact the push services see: <c>mailto:</c> or <c>https:</c>.</summary>
    public string? Subject { get; set; }
}

/// <summary>
/// Sends through <c>Lib.Net.Http.WebPush</c>. Invalid or missing settings leave the feature off with one
/// warning that names no key. Logs carry exception types only, never endpoints or keys.
/// </summary>
public sealed class WebPushSender : IWebPushSender
{
    /// <summary>Name of the HTTP client, which does not follow redirects.</summary>
    public const string HttpClientName = "cocinaris-webpush";

    private static readonly int TimeToLiveSeconds = (int)TimeSpan.FromHours(12).TotalSeconds;
    private readonly IHttpClientFactory _clients;
    private readonly ILogger<WebPushSender> _logger;
    private readonly VapidAuthentication? _authentication;

    /// <summary>Reads the settings once.</summary>
    /// <param name="options">Settings.</param>
    /// <param name="clients">HTTP clients.</param>
    /// <param name="logger">Log.</param>
    public WebPushSender(IOptions<WebPushOptions> options, IHttpClientFactory clients, ILogger<WebPushSender> logger)
    {
        _clients = clients;
        _logger = logger;
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.PublicKey) || string.IsNullOrWhiteSpace(settings.PrivateKey) || string.IsNullOrWhiteSpace(settings.Subject))
        {
            return;
        }

        try
        {
            _authentication = new VapidAuthentication(settings.PublicKey, settings.PrivateKey) { Subject = settings.Subject };
            PublicKey = settings.PublicKey;
        }
        catch (ArgumentException)
        {
            logger.LogWarning("Web push settings are not valid; notifications stay off");
        }
    }

    /// <inheritdoc />
    public string? PublicKey { get; }

    /// <inheritdoc />
    public async Task<PushSendOutcome> SendAsync(PushSubscriptionEntry subscription, string payload, CancellationToken cancellationToken)
    {
        // Checked again here: the rules may have tightened since this row was saved.
        if (_authentication is null || !PushSubscriptionRules.IsAllowedEndpoint(subscription.Endpoint))
        {
            return PushSendOutcome.Failed;
        }

        try
        {
            var target = new PushSubscription { Endpoint = subscription.Endpoint };
            target.SetKey(PushEncryptionKeyName.P256DH, subscription.P256dh);
            target.SetKey(PushEncryptionKeyName.Auth, subscription.Auth);
            var client = new PushServiceClient(_clients.CreateClient(HttpClientName));
            await client.RequestPushMessageDeliveryAsync(target, new PushMessage(payload) { TimeToLive = TimeToLiveSeconds }, _authentication, cancellationToken);
            return PushSendOutcome.Delivered;
        }
        catch (PushServiceClientException exception) when (exception.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
        {
            return PushSendOutcome.Gone;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // One bad subscription must not stop the others (exception messages may hold the address).
            _logger.LogWarning("Web push send failed: {ExceptionType}", exception.GetType().Name);
            return PushSendOutcome.Failed;
        }
    }
}
