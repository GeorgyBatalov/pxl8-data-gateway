using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Pxl8.DataGateway.Security;

/// <summary>
/// HTTP message handler that automatically signs outgoing requests to Control API with HMAC-SHA256
/// Adds X-Signature and X-Timestamp headers to all requests
/// </summary>
public class HmacSigningHandler : DelegatingHandler
{
    private readonly string _sharedSecret;
    private readonly ILogger<HmacSigningHandler> _logger;

    public HmacSigningHandler(IConfiguration configuration, ILogger<HmacSigningHandler> logger)
    {
        _sharedSecret = configuration["InterPlane:SharedSecret"]
            ?? throw new InvalidOperationException("InterPlane:SharedSecret configuration is missing");

        if (_sharedSecret.Length < 32)
        {
            throw new InvalidOperationException("InterPlane:SharedSecret must be at least 32 characters");
        }

        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Read request body (if present)
        string body = string.Empty;
        if (request.Content != null)
        {
            body = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        // Generate timestamp
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Generate HMAC signature
        var signature = GenerateSignature(
            request.Method.Method,
            request.RequestUri?.PathAndQuery ?? "/",
            body,
            timestamp);

        // Add HMAC headers
        request.Headers.Add("X-Signature", signature);
        request.Headers.Add("X-Timestamp", timestamp.ToString());

        _logger.LogDebug(
            "HMAC signature added. Method: {Method}, Path: {Path}, Timestamp: {Timestamp}",
            request.Method, request.RequestUri?.PathAndQuery, timestamp);

        // Send request
        return await base.SendAsync(request, cancellationToken);
    }

    private string GenerateSignature(string httpMethod, string path, string body, long timestamp)
    {
        // Message format: METHOD|PATH|BODY|TIMESTAMP
        var message = $"{httpMethod.ToUpperInvariant()}|{path}|{body}|{timestamp}";

        var keyBytes = Encoding.UTF8.GetBytes(_sharedSecret);
        var messageBytes = Encoding.UTF8.GetBytes(message);

        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(messageBytes);

        return Convert.ToBase64String(hashBytes);
    }
}
