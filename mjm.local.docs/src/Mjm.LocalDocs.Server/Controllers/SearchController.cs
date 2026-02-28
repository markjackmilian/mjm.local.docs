using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mjm.LocalDocs.Core.Services;
using Mjm.LocalDocs.Server.Dtos;

namespace Mjm.LocalDocs.Server.Controllers;

[ApiController]
[Route("api/search")]
[Authorize]
public sealed class SearchController : ControllerBase
{
    private readonly DocumentService _documentService;

    public SearchController(DocumentService documentService)
    {
        _documentService = documentService;
    }

    [HttpPost]
    public async Task<IActionResult> Search([FromBody] SearchRequest request, CancellationToken ct)
    {
        var results = await _documentService.SearchAsync(
            request.Query,
            request.ProjectId,
            request.Limit,
            ct);

        var response = results.Select(r => new SearchResultResponse(
            r.Chunk.Id,
            r.Chunk.Content,
            r.Chunk.DocumentId,
            r.Chunk.FileName,
            r.Score)).ToList();

        return Ok(response);
    }
}
