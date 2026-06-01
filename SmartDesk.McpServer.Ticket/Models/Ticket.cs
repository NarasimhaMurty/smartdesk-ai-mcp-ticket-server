namespace SmartDesk.McpServer.Ticket.Models;

// WHY record with mutable properties here?
// → Tickets CHANGE state over their lifetime
//   (Open → InProgress → Resolved → Closed)
// → init-only records cannot be updated
// → We use { get; set; } for mutable state
// → BUT we still use record for value equality

public record Ticket
{
    public string Id { get; init; } = string.Empty;
    // WHY init for Id?
    // → Ticket ID never changes after creation
    // → Immutable identity

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    // WHY set for these?
    // → Agents may update title/description after triage

    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;

    public string ReportedBy { get; init; } = string.Empty;
    // WHY init for ReportedBy?
    // → Who reported it never changes

    public string? AssignedTo { get; set; }
    // WHY nullable?
    // → New ticket = not yet assigned to anyone

    public List<TicketNote> Notes { get; init; } = [];
    // WHY init for Notes list?
    // → The LIST itself doesn't change (same object)
    // → But we ADD items to it
    // → init = you cannot replace the list
    //   but you can call .Add() on it

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    // WHY nullable DateTime?
    // → Ticket may not be resolved yet
    // → null = not resolved, DateTime = resolved at this time
}

public record TicketNote
{
    public string Author { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public DateTime AddedAt { get; init; } = DateTime.UtcNow;
}

// WHY enums instead of strings?
// → string "opne" (typo) compiles fine but is wrong
// → TicketStatus.Opne does NOT compile → caught immediately
// → Enums = compile-time safety
// → AI gets exact valid values from JSON schema
public enum TicketStatus
{
    Open,
    InProgress,
    PendingUser,
    Resolved,
    Closed
}

public enum TicketPriority
{
    Low,
    Medium,
    High,
    Critical
}
