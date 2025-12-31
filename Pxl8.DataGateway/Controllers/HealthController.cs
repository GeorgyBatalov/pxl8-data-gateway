using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Pxl8.DataGateway.Configuration;
using Pxl8.DataGateway.Services;

namespace Pxl8.DataGateway.Controllers;

/// <summary>
/// Health Check API - monitoring and diagnostics
/// </summary>
[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DataPlaneOptions _options;
    private readonly IPolicySnapshotCache _policyCache;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        IHttpClientFactory httpClientFactory,
        IOptions<DataPlaneOptions> options,
        IPolicySnapshotCache policyCache,
        ILogger<HealthController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _policyCache = policyCache;
        _logger = logger;
    }

    /// <summary>
    /// GET /health/live
    /// </summary>
    /// <remarks>
    /// Liveness probe - is the service running?
    /// </remarks>
    [HttpGet("live")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetLiveness()
    {
        return Ok(new
        {
            status = "healthy",
            service = "pxl8-data-gateway",
            dataplane_id = _options.DataPlaneId,
            timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// GET /health/ready
    /// </summary>
    /// <remarks>
    /// Readiness probe - is the service ready to serve traffic?
    /// Checks: Control API connectivity, policy snapshot loaded
    /// </remarks>
    [HttpGet("ready")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetReadiness(CancellationToken cancellationToken)
    {
        var checks = new Dictionary<string, object>();
        var isHealthy = true;

        // Check 1: Policy snapshot loaded
        var snapshot = _policyCache.GetCurrentSnapshot();
        if (snapshot == null)
        {
            checks["policy_snapshot"] = new { status = "unhealthy", message = "No policy snapshot loaded yet" };
            isHealthy = false;
        }
        else
        {
            checks["policy_snapshot"] = new
            {
                status = "healthy",
                snapshot_id = snapshot.SnapshotId,
                generated_at = snapshot.GeneratedAt,
                tenant_count = snapshot.Tenants.Count
            };
        }

        // Check 2: Control API connectivity
        try
        {
            var client = _httpClientFactory.CreateClient("ControlApi");
            var controlApiUrl = $"{_options.ControlApiUrl}/health/live";

            var response = await client.GetAsync(controlApiUrl, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                checks["control_api"] = new { status = "healthy", url = controlApiUrl };
            }
            else
            {
                checks["control_api"] = new
                {
                    status = "degraded",
                    url = controlApiUrl,
                    http_status = (int)response.StatusCode,
                    message = "Control API returned non-2xx status (Data Plane can continue autonomously)"
                };
                // Note: degraded != unhealthy - Data Plane can work without Control Plane for 10+ min
            }
        }
        catch (Exception ex)
        {
            checks["control_api"] = new
            {
                status = "degraded",
                url = _options.ControlApiUrl,
                error = ex.Message,
                message = "Control API unreachable (Data Plane can continue autonomously)"
            };
            // Note: Control Plane unreachable is OK - autonomous operation
        }

        if (!isHealthy)
        {
            return StatusCode(503, new
            {
                status = "unhealthy",
                service = "pxl8-data-gateway",
                dataplane_id = _options.DataPlaneId,
                checks,
                timestamp = DateTimeOffset.UtcNow
            });
        }

        return Ok(new
        {
            status = "healthy",
            service = "pxl8-data-gateway",
            dataplane_id = _options.DataPlaneId,
            checks,
            timestamp = DateTimeOffset.UtcNow
        });
    }
}
