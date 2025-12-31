namespace Pxl8.DataGateway.Configuration;

/// <summary>
/// Configuration options for Data Plane
/// </summary>
public class DataPlaneOptions
{
    public const string SectionName = "DataPlane";

    /// <summary>
    /// Data Plane unique identifier (e.g., "ru-central1-a")
    /// </summary>
    public required string DataPlaneId { get; set; }

    /// <summary>
    /// Control API base URL (e.g., "https://control-api.pxl8.io")
    /// </summary>
    public required string ControlApiUrl { get; set; }

    /// <summary>
    /// Policy snapshot sync interval
    /// </summary>
    /// <remarks>Default: 00:01:00 (1 minute)</remarks>
    public TimeSpan PolicySyncInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Usage report flush interval
    /// </summary>
    /// <remarks>Default: 00:00:10 (10 seconds)</remarks>
    public TimeSpan UsageFlushInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Budget refill check interval
    /// </summary>
    /// <remarks>Default: 00:00:05 (5 seconds)</remarks>
    public TimeSpan BudgetRefillCheckInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Budget refill threshold (0.0 - 1.0)
    /// </summary>
    /// <remarks>Default: 0.2 (20% of granted budget)</remarks>
    public double BudgetRefillThreshold { get; set; } = 0.2;

    /// <summary>
    /// Initial budget request amounts
    /// </summary>
    public long InitialBandwidthRequest { get; set; } = 10_737_418_240; // 10 GB

    public int InitialTransformsRequest { get; set; } = 100_000;
}
