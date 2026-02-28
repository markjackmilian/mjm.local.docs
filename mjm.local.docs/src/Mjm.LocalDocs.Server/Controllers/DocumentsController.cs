using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Models;
using Mjm.LocalDocs.Core.Services;
using Mjm.LocalDocs.Infrastructure.Documents;
using Mjm.LocalDocs.Server.Dtos;

namespace Mjm.LocalDocs.Server.Controllers;

[ApiController]
[Authorize]
public sealed class DocumentsController : ControllerBase
{
    private readonly DocumentService _documentService;
    private readonly IProjectRepository _projectRepository;
    private readonly CompositeDocumentReader _documentReader;

    public DocumentsController(
        DocumentService documentService,
        IProjectRepository projectRepository,
        IDocumentReader documentReader)
    {
        _documentService = documentService;
        _projectRepository = projectRepository;
        _documentReader = (CompositeDocumentReader)documentReader;
    }

    [HttpGet("api/projects/{projectId}/documents")]
    public async Task<IActionResult> GetByProject(
        string projectId,
        [FromQuery] bool includeSuperseded = false,
        CancellationToken ct = default)
    {
        if (!await _projectRepository.ExistsAsync(projectId, ct))
            return NotFound(new { message = "Project not found." });

        var documents = await _documentService.GetDocumentsByProjectAsync(projectId, includeSuperseded, ct);
        return Ok(documents.Select(MapDocument).ToList());
    }

    [HttpPost("api/projects/{projectId}/documents")]
    [RequestSizeLimit(52_428_800)] // 50 MB
    public async Task<IActionResult> Upload(
        string projectId,
        IFormFile file,
        CancellationToken ct)
    {
        if (!await _projectRepository.ExistsAsync(projectId, ct))
            return NotFound(new { message = "Project not found." });

        if (file.Length == 0)
            return BadRequest(new { message = "File is empty." });

        var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!_documentReader.CanRead(fileExtension))
            return BadRequest(new { message = $"Unsupported file format '{fileExtension}'." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var fileBytes = ms.ToArray();

        var extractionResult = await _documentReader.ExtractTextAsync(fileBytes, fileExtension, ct);
        if (!extractionResult.Success)
            return BadRequest(new { message = $"Failed to extract text: {extractionResult.ErrorMessage}" });

        var contentHash = Convert.ToBase64String(SHA256.HashData(fileBytes));

        var document = new Document
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = projectId,
            FileName = file.FileName,
            FileExtension = fileExtension,
            FileContent = fileBytes,
            FileSizeBytes = fileBytes.Length,
            ExtractedText = extractionResult.Text,
            ContentHash = contentHash,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var saved = await _documentService.AddDocumentAsync(document, ct);
        return CreatedAtAction(nameof(GetById), new { id = saved.Id }, MapDocument(saved));
    }

    [HttpPost("api/projects/{projectId}/documents/knowhow")]
    public async Task<IActionResult> CreateKnowHow(
        string projectId,
        [FromBody] CreateKnowHowRequest request,
        CancellationToken ct)
    {
        if (!await _projectRepository.ExistsAsync(projectId, ct))
            return NotFound(new { message = "Project not found." });

        var fileName = SanitizeFileName(request.Title) + ".md";
        var fileBytes = Encoding.UTF8.GetBytes(request.Content);
        var contentHash = Convert.ToBase64String(SHA256.HashData(fileBytes));

        var document = new Document
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = projectId,
            FileName = fileName,
            FileExtension = ".md",
            FileContent = fileBytes,
            FileSizeBytes = fileBytes.Length,
            ExtractedText = request.Content,
            ContentHash = contentHash,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var saved = await _documentService.AddDocumentAsync(document, ct);
        return CreatedAtAction(nameof(GetById), new { id = saved.Id }, MapDocument(saved));
    }

    [HttpGet("api/documents/{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var document = await _documentService.GetDocumentAsync(id, ct);
        if (document is null)
            return NotFound();

        return Ok(MapDocument(document));
    }

    [HttpGet("api/documents/{id}/content")]
    public async Task<IActionResult> GetContent(string id, CancellationToken ct)
    {
        var document = await _documentService.GetDocumentAsync(id, ct);
        if (document is null)
            return NotFound();

        return Ok(new { extractedText = document.ExtractedText });
    }

    [HttpGet("api/documents/{id}/file")]
    public async Task<IActionResult> DownloadFile(string id, CancellationToken ct)
    {
        var document = await _documentService.GetDocumentAsync(id, ct);
        if (document is null)
            return NotFound();

        var fileContent = await _documentService.GetDocumentFileAsync(id, ct);
        if (fileContent is null)
            return NotFound(new { message = "File content not available." });

        var contentType = GetContentType(document.FileExtension);
        return File(fileContent, contentType, document.FileName);
    }

    [HttpPut("api/documents/{id}")]
    [RequestSizeLimit(52_428_800)] // 50 MB
    public async Task<IActionResult> UpdateVersion(
        string id,
        IFormFile file,
        CancellationToken ct)
    {
        var existing = await _documentService.GetDocumentAsync(id, ct);
        if (existing is null)
            return NotFound();

        if (existing.IsSuperseded)
            return BadRequest(new { message = "Cannot update a superseded document." });

        var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!_documentReader.CanRead(fileExtension))
            return BadRequest(new { message = $"Unsupported file format '{fileExtension}'." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var fileBytes = ms.ToArray();

        var extractionResult = await _documentReader.ExtractTextAsync(fileBytes, fileExtension, ct);
        if (!extractionResult.Success)
            return BadRequest(new { message = $"Failed to extract text: {extractionResult.ErrorMessage}" });

        var contentHash = Convert.ToBase64String(SHA256.HashData(fileBytes));

        var newVersion = new Document
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = existing.ProjectId,
            FileName = file.FileName,
            FileExtension = fileExtension,
            FileContent = fileBytes,
            FileSizeBytes = fileBytes.Length,
            ExtractedText = extractionResult.Text,
            ContentHash = contentHash,
            VersionNumber = existing.VersionNumber + 1,
            ParentDocumentId = existing.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var saved = await _documentService.UpdateDocumentAsync(id, newVersion, ct);
        return Ok(MapDocument(saved));
    }

    [HttpPut("api/documents/{id}/knowhow")]
    public async Task<IActionResult> UpdateKnowHow(
        string id,
        [FromBody] UpdateKnowHowRequest request,
        CancellationToken ct)
    {
        var existing = await _documentService.GetDocumentAsync(id, ct);
        if (existing is null)
            return NotFound();

        if (existing.IsSuperseded)
            return BadRequest(new { message = "Cannot update a superseded document." });

        var fileName = SanitizeFileName(request.Title) + ".md";
        var fileBytes = Encoding.UTF8.GetBytes(request.Content);
        var contentHash = Convert.ToBase64String(SHA256.HashData(fileBytes));

        var newVersion = new Document
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = existing.ProjectId,
            FileName = fileName,
            FileExtension = ".md",
            FileContent = fileBytes,
            FileSizeBytes = fileBytes.Length,
            ExtractedText = request.Content,
            ContentHash = contentHash,
            VersionNumber = existing.VersionNumber + 1,
            ParentDocumentId = existing.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var saved = await _documentService.UpdateDocumentAsync(id, newVersion, ct);
        return Ok(MapDocument(saved));
    }

    [HttpDelete("api/documents/{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var deleted = await _documentService.DeleteDocumentAsync(id, ct);
        if (!deleted)
            return NotFound();

        return NoContent();
    }

    [HttpGet("api/documents/{id}/versions")]
    public async Task<IActionResult> GetVersions(string id, CancellationToken ct)
    {
        var document = await _documentService.GetDocumentAsync(id, ct);
        if (document is null)
            return NotFound();

        var versions = await _documentService.GetDocumentVersionsAsync(id, ct);
        return Ok(versions.Select(MapDocument).ToList());
    }

    private static DocumentResponse MapDocument(Document d) =>
        new(d.Id, d.ProjectId, d.FileName, d.FileExtension, d.FileSizeBytes,
            d.VersionNumber, d.ParentDocumentId, d.IsSuperseded, d.ContentHash,
            d.CreatedAt, d.UpdatedAt);

    private static string SanitizeFileName(string title)
    {
        var sanitized = title.Trim().Replace(' ', '-');
        sanitized = Regex.Replace(sanitized, @"[^\w\-.]", "");
        sanitized = Regex.Replace(sanitized, @"-{2,}", "-");
        return string.IsNullOrEmpty(sanitized) ? "document" : sanitized;
    }

    private static string GetContentType(string fileExtension) =>
        fileExtension.ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => "application/msword",
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            ".html" or ".htm" => "text/html",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".csv" => "text/csv",
            _ => "application/octet-stream"
        };
}
