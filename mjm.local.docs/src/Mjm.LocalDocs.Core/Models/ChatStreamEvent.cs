namespace Mjm.LocalDocs.Core.Models;

/// <summary>Base type for all events streamed from the chat service.</summary>
public abstract record ChatStreamEvent;

/// <summary>A text token to append to the assistant response.</summary>
public sealed record TextTokenEvent(string Token) : ChatStreamEvent;

/// <summary>An agentic thinking step, e.g. a semantic search query being executed.</summary>
public sealed record ThinkingEvent(string Message, int SearchIndex) : ChatStreamEvent;
