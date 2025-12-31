using Microsoft.AspNetCore.Mvc;
using Pxl8.DataGateway.Services;

namespace Pxl8.DataGateway.Controllers;

/// <summary>
/// Images API - Hot Path (zero DB calls, < 100ms p99)
/// </summary>
/// <remarks>
/// This is a stub implementation demonstrating budget enforcement.
/// In production, this would integrate with actual image storage and transformation services.
/// </remarks>
[ApiController]
[Route("api/v1/images")]
public class ImagesController : ControllerBase
{
    private readonly IBudgetManager _budgetManager;
    private readonly ILogger<ImagesController> _logger;

    public ImagesController(IBudgetManager budgetManager, ILogger<ImagesController> logger)
    {
        _budgetManager = budgetManager;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/v1/images/{imageId}
    /// </summary>
    /// <remarks>
    /// Hot path: retrieve original image with bandwidth budget check
    /// </remarks>
    [HttpGet("{imageId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult GetImage(Guid imageId, [FromQuery] Guid tenantId, [FromQuery] Guid periodId)
    {
        // Demo: 100KB image size
        const long imageSizeBytes = 102400;

        // Hot path budget check (0 external calls)
        if (!_budgetManager.TrySpendBandwidth(tenantId, periodId, imageSizeBytes))
        {
            _logger.LogWarning(
                "Bandwidth quota exceeded: tenant={TenantId}, period={PeriodId}, image={ImageId}",
                tenantId, periodId, imageId);

            return StatusCode(429, new
            {
                error = "QUOTA_EXCEEDED",
                message = "Bandwidth quota exceeded. Lease may have expired or budget exhausted."
            });
        }

        _logger.LogDebug(
            "Image delivered: image={ImageId}, tenant={TenantId}, size={SizeBytes}bytes",
            imageId, tenantId, imageSizeBytes);

        // Stub response (in production: return actual image from S3/storage)
        return Ok(new
        {
            image_id = imageId,
            tenant_id = tenantId,
            size_bytes = imageSizeBytes,
            message = "Image delivered (stub - no actual image data)",
            budget_check = "passed"
        });
    }

    /// <summary>
    /// GET /api/v1/images/{imageId}/transform
    /// </summary>
    /// <remarks>
    /// Hot path: retrieve transformed image with bandwidth + transforms budget check
    /// </remarks>
    [HttpGet("{imageId}/transform")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult GetTransformedImage(
        Guid imageId,
        [FromQuery] Guid tenantId,
        [FromQuery] Guid periodId,
        [FromQuery] int? width,
        [FromQuery] int? height,
        [FromQuery] string? format)
    {
        // Demo: 150KB transformed image size
        const long transformedSizeBytes = 153600;

        // Hot path budget check - transforms quota
        if (!_budgetManager.TrySpendTransform(tenantId, periodId))
        {
            _logger.LogWarning(
                "Transforms quota exceeded: tenant={TenantId}, period={PeriodId}, image={ImageId}",
                tenantId, periodId, imageId);

            return StatusCode(429, new
            {
                error = "TRANSFORMS_QUOTA_EXCEEDED",
                message = "Transforms quota exceeded. Lease may have expired or budget exhausted."
            });
        }

        // Hot path budget check - bandwidth quota
        if (!_budgetManager.TrySpendBandwidth(tenantId, periodId, transformedSizeBytes))
        {
            _logger.LogWarning(
                "Bandwidth quota exceeded after transform: tenant={TenantId}, period={PeriodId}, image={ImageId}",
                tenantId, periodId, imageId);

            return StatusCode(429, new
            {
                error = "BANDWIDTH_QUOTA_EXCEEDED",
                message = "Bandwidth quota exceeded. Lease may have expired or budget exhausted."
            });
        }

        _logger.LogDebug(
            "Transformed image delivered: image={ImageId}, tenant={TenantId}, width={Width}, height={Height}, format={Format}, size={SizeBytes}bytes",
            imageId, tenantId, width, height, format, transformedSizeBytes);

        // Stub response (in production: transform and return actual image)
        return Ok(new
        {
            image_id = imageId,
            tenant_id = tenantId,
            transformation = new { width, height, format },
            size_bytes = transformedSizeBytes,
            message = "Transformed image delivered (stub - no actual transformation)",
            budget_checks = new
            {
                transforms = "passed",
                bandwidth = "passed"
            }
        });
    }

    /// <summary>
    /// GET /api/v1/images/budget-status
    /// </summary>
    /// <remarks>
    /// Debug endpoint: check current budget status for tenant
    /// </remarks>
    [HttpGet("budget-status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetBudgetStatus([FromQuery] Guid tenantId, [FromQuery] Guid periodId)
    {
        var budget = _budgetManager.GetBudget(tenantId, periodId);

        if (budget == null)
        {
            return NotFound(new
            {
                error = "BUDGET_NOT_FOUND",
                message = "No budget lease found for this tenant/period. Budget may need to be allocated first."
            });
        }

        lock (budget.Lock)
        {
            return Ok(new
            {
                tenant_id = tenantId,
                period_id = periodId,
                lease_id = budget.LeaseId,
                expires_at = budget.ExpiresAt,
                is_expired = DateTimeOffset.UtcNow > budget.ExpiresAt,
                remaining_bandwidth_bytes = budget.RemainingBandwidthBytes,
                remaining_transforms = budget.RemainingTransforms,
                granted_bandwidth_bytes = budget.GrantedBandwidthBytes,
                granted_transforms = budget.GrantedTransforms,
                refill_in_progress = budget.RefillInProgress,
                last_refill_attempt_at = budget.LastRefillAttemptAt,
                consumed_bandwidth_delta = budget.ConsumedBandwidthDelta,
                consumed_transforms_delta = budget.ConsumedTransformsDelta
            });
        }
    }
}
