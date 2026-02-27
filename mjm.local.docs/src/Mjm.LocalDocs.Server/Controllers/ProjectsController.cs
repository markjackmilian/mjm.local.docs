using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Models;
using Mjm.LocalDocs.Core.Services;
using Mjm.LocalDocs.Server.Dtos;

namespace Mjm.LocalDocs.Server.Controllers;

[ApiController]
[Route("api/projects")]
[Authorize]
public sealed class ProjectsController : ControllerBase
{
    private readonly IProjectRepository _projectRepository;
    private readonly DocumentService _documentService;

    public ProjectsController(
        IProjectRepository projectRepository,
        DocumentService documentService)
    {
        _projectRepository = projectRepository;
        _documentService = documentService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var projects = await _projectRepository.GetAllAsync(ct);
        var result = new List<ProjectWithDocCountResponse>();

        foreach (var project in projects)
        {
            var docs = await _documentService.GetDocumentsByProjectAsync(project.Id, includeSuperseded: false, ct);
            result.Add(new ProjectWithDocCountResponse(MapProject(project), docs.Count));
        }

        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var project = await _projectRepository.GetByIdAsync(id, ct);
        if (project is null)
            return NotFound();

        return Ok(MapProject(project));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProjectRequest request, CancellationToken ct)
    {
        if (await _projectRepository.ExistsByNameAsync(request.Name, ct))
            return Conflict(new { message = $"A project named '{request.Name}' already exists." });

        var project = new Project
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = request.Name,
            Description = request.Description,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var created = await _projectRepository.CreateAsync(project, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, MapProject(created));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateProjectRequest request, CancellationToken ct)
    {
        var existing = await _projectRepository.GetByIdAsync(id, ct);
        if (existing is null)
            return NotFound();

        // Check name uniqueness if name changed
        if (!string.Equals(existing.Name, request.Name, StringComparison.OrdinalIgnoreCase))
        {
            if (await _projectRepository.ExistsByNameAsync(request.Name, ct))
                return Conflict(new { message = $"A project named '{request.Name}' already exists." });
        }

        var updated = new Project
        {
            Id = existing.Id,
            Name = request.Name,
            Description = request.Description,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var result = await _projectRepository.UpdateAsync(updated, ct);
        return Ok(MapProject(result));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var deleted = await _projectRepository.DeleteAsync(id, ct);
        if (!deleted)
            return NotFound();

        return NoContent();
    }

    [HttpGet("exists-by-name")]
    public async Task<IActionResult> ExistsByName([FromQuery] string name, CancellationToken ct)
    {
        var exists = await _projectRepository.ExistsByNameAsync(name, ct);
        return Ok(new { exists });
    }

    private static ProjectResponse MapProject(Project p) =>
        new(p.Id, p.Name, p.Description, p.CreatedAt, p.UpdatedAt);
}
