namespace N24DataRelay.Core.Services;

/// <summary>Resolves effective outbound routes for the watcher. When Transfer.Routes is empty, returns one synthetic route from Transfer.Ssh/Smb + Service.WatchDirectory.</summary>
public static class TransferRouteHelper
{
    /// <summary>Returns (watchPath, route) pairs. Watcher watches each watchPath and sends files to the route's target.</summary>
    public static List<(string WatchPath, N24DataRelay.Core.Models.TransferRouteSettings Route)> GetEffectiveRoutes(
        N24DataRelay.Core.Models.N24DataRelayConfiguration config)
    {
        var service = config.Service;
        var transfer = config.Transfer;
        var routes = transfer.Routes;

        if (routes == null || routes.Count == 0)
        {
            var synthetic = new N24DataRelay.Core.Models.TransferRouteSettings
            {
                Name = config.Branding.ScadaSideName,
                SourcePath = service.WatchDirectory,
                TransferMethod = service.TransferMethod,
                Ssh = transfer.Ssh,
                Smb = transfer.Smb
            };
            return new List<(string, N24DataRelay.Core.Models.TransferRouteSettings)>
            {
                (service.WatchDirectory, synthetic)
            };
        }

        return routes
            .Select(r => (string.IsNullOrEmpty(r.SourcePath) ? service.WatchDirectory : r.SourcePath!, r))
            .ToList();
    }

    /// <summary>Finds the route that applies to the given file path (which watch path it lies under). Returns null if no match.</summary>
    public static N24DataRelay.Core.Models.TransferRouteSettings? GetRouteForPath(
        N24DataRelay.Core.Models.N24DataRelayConfiguration config,
        string filePath)
    {
        var effective = GetEffectiveRoutes(config);
        var normalized = filePath.Replace('\\', '/').TrimEnd('/');
        foreach (var (watchPath, route) in effective)
        {
            var watchNorm = watchPath.Replace('\\', '/').TrimEnd('/');
            if (normalized.StartsWith(watchNorm + "/", StringComparison.Ordinal) || normalized == watchNorm)
                return route;
        }
        return null;
    }
}
