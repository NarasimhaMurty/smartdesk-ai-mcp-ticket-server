namespace SmartDesk.McpServer.Ticket.Models;

public record CreateTicketResult
{
    public string TicketId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public record GetTicketResult
{
    public string TicketId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
    public string ReportedBy { get; init; } = string.Empty;
    public string AssignedTo { get; init; } = string.Empty;
    public List<string> Notes { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record UpdateTicketResult
{
    public string TicketId { get; init; } = string.Empty;
    public string NewStatus { get; init; } = string.Empty;
    public string AssignedTo { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public record AddNoteResult
{
    public string TicketId { get; init; } = string.Empty;
    public int NoteCount { get; init; }
    public string Message { get; init; } = string.Empty;
}

public record ListTicketsResult
{
    public int TotalCount { get; init; }
    public List<TicketSummary> Tickets { get; init; } = [];
}

public record TicketSummary
{
    public string TicketId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
    public string ReportedBy { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}
