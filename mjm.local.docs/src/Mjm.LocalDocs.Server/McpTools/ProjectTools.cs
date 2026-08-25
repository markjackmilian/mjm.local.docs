using System.ComponentModel;
using ModelContextProtocol.Server;
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Models;
using Mjm.LocalDocs.Core.Services;

namespace Mjm.LocalDocs.Server.McpTools;

/// <summary>
/// MCP Tools for managing projects.
/// </summary>
[McpServerToolType]
public sealed class ProjectTools
{
    private readonly IProjectRepository _projectRepository;
    private readonly DocumentService _documentService;
    private readonly ProjectService _projectService;

    public ProjectTools(IProjectRepository projectRepository, DocumentService documentService, ProjectService projectService)
    {
        _projectRepository = projectRepository;
        _documentService = documentService;
        _projectService = projectService;
    }

    [McpServerTool(Name = "create_project")]
    [Description("Create a new project to organize documents.")]
    public async Task<string> CreateProjectAsync(
        [Description("Unique name for the project")] string name,
        [Description("Optional description of the project")] string? description = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Error: Project name is required.";

        // Check if project with same name exists
        var existingProject = await _projectRepository.GetByNameAsync(name, cancellationToken);
        if (existingProject != null)
        {
            return $"Error: A project with name '{name}' already exists (ID: {existingProject.Id}).";
        }

        var project = new Project
        {
            Id = Guid.NewGuid().ToString(),
            Name = name.Trim(),
            Description = description?.Trim()
        };

        try
        {
            var savedProject = await _projectRepository.CreateAsync(project, cancellationToken);
            return $"Project '{savedProject.Name}' created successfully (ID: {savedProject.Id}).";
        }
        catch (Exception ex)
        {
            return $"Error creating project: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_project")]
    [Description("Get details about a specific project.")]
    public async Task<string> GetProjectAsync(
        [Description("The project ID or name")] string projectIdOrName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectIdOrName))
            return "Error: Project ID or name is required.";

        // Try to find by ID first, then by name
        var project = await _projectRepository.GetByIdAsync(projectIdOrName, cancellationToken)
                      ?? await _projectRepository.GetByNameAsync(projectIdOrName, cancellationToken);

        if (project == null)
        {
            return $"Error: Project '{projectIdOrName}' not found.";
        }

        var documents = await _documentService.GetDocumentsByProjectAsync(project.Id, cancellationToken: cancellationToken);

        var response = $"## Project: {project.Name}\n\n";
        response += $"**ID**: {project.Id}\n";
        if (!string.IsNullOrEmpty(project.Description))
        {
            response += $"**Description**: {project.Description}\n";
        }
        response += $"**Created**: {project.CreatedAt:yyyy-MM-dd HH:mm:ss}\n";
        if (project.UpdatedAt.HasValue)
        {
            response += $"**Updated**: {project.UpdatedAt:yyyy-MM-dd HH:mm:ss}\n";
        }
        response += $"**Documents**: {documents.Count}\n\n";

        if (documents.Count > 0)
        {
            response += "### Documents:\n";
            foreach (var doc in documents)
            {
                response += $"- {doc.FileName} (ID: {doc.Id}, {doc.FileSizeBytes} bytes)\n";
            }
        }

        return response;
    }

    [McpServerTool(Name = "delete_project")]
    [Description("Delete a project and ALL its documents. This action cannot be undone!")]
    public async Task<string> DeleteProjectAsync(
        [Description("The project ID to delete")] string projectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return "Error: Project ID is required.";

        var project = await _projectRepository.GetByIdAsync(projectId, cancellationToken);
        if (project == null)
        {
            return $"Error: Project with ID '{projectId}' not found.";
        }

        try
        {
            var deleted = await _projectService.DeleteProjectAsync(projectId, cancellationToken);
            if (deleted)
            {
                return $"Project '{project.Name}' (ID: {projectId}) and all its documents have been deleted.";
            }
            return $"Error: Failed to delete project '{projectId}'.";
        }
        catch (Exception ex)
        {
            return $"Error deleting project: {ex.Message}";
        }
    }
}
