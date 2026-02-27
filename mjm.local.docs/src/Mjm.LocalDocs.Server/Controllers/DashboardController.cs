using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Services;
using Mjm.LocalDocs.Server.Dtos;

namespace Mjm.LocalDocs.Server.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public sealed class DashboardController : ControllerBase
{
    private readonly IProjectRepository _projectRepository;
    private readonly DocumentService _documentService;

    public DashboardController(
        IProjectRepository projectRepository,
        DocumentService documentService)
    {
        _projectRepository = projectRepository;
        _documentService = documentService;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var projects = await _projectRepository.GetAllAsync(ct);
        var totalDocCount = 0;
        long totalSize = 0;
        var projectsWithDocs = new List<ProjectWithDocCountResponse>();

        foreach (var project in projects)
        {
            var docs = await _documentService.GetDocumentsByProjectAsync(
                project.Id, includeSuperseded: false, ct);
            var docCount = docs.Count;
            var projectSize = docs.Sum(d => d.FileSizeBytes);

            totalDocCount += docCount;
            totalSize += projectSize;

            projectsWithDocs.Add(new ProjectWithDocCountResponse(
                new ProjectResponse(project.Id, project.Name, project.Description,
                    project.CreatedAt, project.UpdatedAt),
                docCount));
        }

        var recentProjects = projectsWithDocs
            .OrderByDescending(p => p.Project.UpdatedAt ?? p.Project.CreatedAt)
            .Take(6)
            .ToList();

        return Ok(new DashboardStatsResponse(
            projects.Count,
            totalDocCount,
            totalSize,
            recentProjects));
    }
}
