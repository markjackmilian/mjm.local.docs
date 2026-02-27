namespace Mjm.LocalDocs.Server.Dtos;

using System.ComponentModel.DataAnnotations;

public sealed record CreateProjectRequest(
    [property: Required, MaxLength(100)] string Name,
    [property: MaxLength(500)] string? Description = null);
