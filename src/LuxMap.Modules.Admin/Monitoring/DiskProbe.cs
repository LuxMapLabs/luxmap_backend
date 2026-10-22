namespace LuxMap.Modules.Admin.Monitoring;

public sealed record DiskSpace(long TotalBytes, long FreeBytes)
{
    public double FreeRatio => TotalBytes > 0 ? (double)FreeBytes / TotalBytes : 0;
}

/// <summary>How much of the disk is left (BE-35, D-3). A seam for the tests, which cannot fill a real disk.</summary>
public interface IDiskProbe
{
    DiskSpace Measure();
}

/// <summary>
/// <c>statfs</c> on <see cref="MonitoringOptions.DiskPath"/> through <see cref="DriveInfo"/>, which on Linux takes any path, not only
/// a mount point. Free = what a non-root process may still write, the space the API and the database will actually get.
/// </summary>
public sealed class DriveDiskProbe(MonitoringOptions options) : IDiskProbe
{
    public DiskSpace Measure()
    {
        var drive = new DriveInfo(options.DiskPath);
        return new(drive.TotalSize, drive.AvailableFreeSpace);
    }
}
