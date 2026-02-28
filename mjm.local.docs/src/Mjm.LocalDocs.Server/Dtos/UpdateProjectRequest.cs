namespace Mjm.LocalDocs.Server.Dtos;

using System.ComponentModel.DataAnnotations;

public sealed record UpdateProjectRequest(
    [Required, MaxLength(100)] string Name,
    [MaxLength(500)] string? Description = null);
