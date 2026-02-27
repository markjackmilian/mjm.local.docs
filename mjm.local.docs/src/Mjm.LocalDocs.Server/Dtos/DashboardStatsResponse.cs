namespace Mjm.LocalDocs.Server.Dtos;

public sealed record DashboardStatsResponse(
    int ProjectCount,
    int DocumentCount,
    long TotalSizeBytes,
    IReadOnlyList<ProjectWithDocCountResponse> RecentProjects);
