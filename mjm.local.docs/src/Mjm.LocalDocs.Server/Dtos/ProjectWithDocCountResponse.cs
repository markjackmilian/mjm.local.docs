namespace Mjm.LocalDocs.Server.Dtos;

public sealed record ProjectWithDocCountResponse(
    ProjectResponse Project,
    int DocumentCount);
