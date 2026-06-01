using ModelContextProtocol.Server;
using System.ComponentModel;
using SmartDesk.McpServer.Ticket.Models;
using SmartDesk.McpServer.Ticket.Services;

namespace SmartDesk.McpServer.Ticket.Tools;

[McpServerToolType]
public class TicketTools
{
    private readonly ITicketStore _store;
    private readonly ILogger<TicketTools> _logger;

    public TicketTools(ITicketStore store, ILogger<TicketTools> logger)
    {
        _store = store;
        _logger = logger;
    }

    [McpServerTool(Name = "create_ticket")]
    [Description("""
        Creates a new IT support ticket in the helpdesk system.
        Use this when a user reports a new IT issue.
        Call this FIRST before any resolution attempts.
        Returns the ticket ID which you must use for all subsequent operations on this ticket.
    """)]
    public async Task<CreateTicketResult> CreateTicketAsync(

        [Description("Short, clear title of the IT issue. Example: 'Cannot connect to VPN', 'Outlook not opening'")]
        string title,

        [Description("Detailed description of the issue including error messages, when it started, what was tried")]
        string description,

        [Description("Issue category. One of: Network, Email, Hardware, Software, Access, Security, Other")]
        string category,

        [Description("Reporter's email address")]
        string reportedBy,

        [Description("Priority: Low, Medium, High, Critical. Default is Medium. Use Critical only for business-stopping issues.")]
        TicketPriority priority = TicketPriority.Medium,

        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportedBy);

        var validCategories = new[] { "Network", "Email", "Hardware", "Software", "Access", "Security", "Other" };

        if (!validCategories.Contains(category, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Invalid category '{category}'. Must be one of: {string.Join(", ", validCategories)}");

        var ticketId = $"TKT-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..4].ToUpperInvariant()}";

        var ticket = new Models.Ticket
        {
            Id = ticketId,
            Title = title.Trim(),
            Description = description.Trim(),
            Category = category,
            Priority = priority,
            Status = TicketStatus.Open,
            ReportedBy = reportedBy.ToLowerInvariant().Trim()
        };

        var created = await _store.CreateAsync(ticket, cancellationToken);

        return new CreateTicketResult
        {
            TicketId = created.Id,
            Title = created.Title,
            Status = created.Status.ToString(),
            Priority = created.Priority.ToString(),
            Message = $"Ticket {created.Id} created successfully. Category: {created.Category}, Priority: {created.Priority}"
        };
    }

    [McpServerTool(Name = "get_ticket")]
    [Description("""
        Retrieves full details of an existing ticket by its ID.
        Use this to check current status, read notes, or get context before taking action on a ticket.
        Returns null-like error if ticket ID does not exist.
    """)]
    public async Task<GetTicketResult> GetTicketAsync(

        [Description("Ticket ID to retrieve. Format: TKT-YYYYMMDDHHMMSS-XXXX. Example: TKT-001 or TKT-20240101120000-A3B2")]
        string ticketId,

        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketId);

        var ticket = await _store.GetAsync(ticketId.ToUpperInvariant().Trim(), cancellationToken);

        if (ticket is null)
            throw new InvalidOperationException($"Ticket '{ticketId}' not found. Verify the ticket ID is correct.");

        return new GetTicketResult
        {
            TicketId = ticket.Id,
            Title = ticket.Title,
            Description = ticket.Description,
            Category = ticket.Category,
            Status = ticket.Status.ToString(),
            Priority = ticket.Priority.ToString(),
            ReportedBy = ticket.ReportedBy,
            AssignedTo = ticket.AssignedTo ?? "Unassigned",
            Notes = ticket.Notes.Select(n => $"[{n.AddedAt:HH:mm}] {n.Author}: {n.Content}").ToList(),
            CreatedAt = ticket.CreatedAt,
            UpdatedAt = ticket.UpdatedAt
        };
    }

    [McpServerTool(Name = "update_ticket_status")]
    [Description("""
        Updates the status and optionally the assignee of a ticket.
        Use this when ticket progresses through resolution stages.
        Status flow: Open → InProgress → PendingUser → Resolved → Closed.
        Always add a note explaining why the status changed.
    """)]
    public async Task<UpdateTicketResult> UpdateTicketStatusAsync(

        [Description("ID of the ticket to update")]
        string ticketId,

        [Description("New status. Valid values: Open, InProgress, PendingUser, Resolved, Closed")]
        string status,

        [Description("Note explaining reason for status change. Example: 'Started investigation of network settings'")]
        string note,

        [Description("Agent or person handling this ticket. Example: 'TriageAgent', 'john.doe@company.com'")]
        string? assignTo = null,

        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketId);
        ArgumentException.ThrowIfNullOrWhiteSpace(note);

        if (!Enum.TryParse<TicketStatus>(status, ignoreCase: true, out var newStatus))
            throw new ArgumentException($"Invalid status '{status}'. Valid values: {string.Join(", ", Enum.GetNames<TicketStatus>())}");

        var ticket = await _store.GetAsync(ticketId.ToUpperInvariant(), cancellationToken)
            ?? throw new InvalidOperationException($"Ticket '{ticketId}' not found.");

        var updatedTicket = ticket with
        {
            Status = newStatus,
            AssignedTo = assignTo ?? ticket.AssignedTo,
            ResolvedAt = newStatus == TicketStatus.Resolved ? DateTime.UtcNow : ticket.ResolvedAt
        };

        updatedTicket.Notes.Add(new TicketNote
        {
            Author = assignTo ?? "System",
            Content = $"[Status: {status}] {note}"
        });

        await _store.UpdateAsync(updatedTicket, cancellationToken);

        return new UpdateTicketResult
        {
            TicketId = updatedTicket.Id,
            NewStatus = updatedTicket.Status.ToString(),
            AssignedTo = updatedTicket.AssignedTo ?? "Unassigned",
            Message = $"Ticket {ticketId} updated to {status}. Note added."
        };
    }

    [McpServerTool(Name = "add_ticket_note")]
    [Description("""
        Adds a note/comment to an existing ticket without changing status.
        Use this to document investigation steps, attempted solutions, or communication with the user.
        Notes are permanent and visible to all agents handling the ticket.
    """)]
    public async Task<AddNoteResult> AddTicketNoteAsync(

        [Description("ID of the ticket to add note to")]
        string ticketId,

        [Description("Author of this note. Use agent name or email. Example: 'TriageAgent', 'KnowledgeAgent'")]
        string author,

        [Description("Note content. Be specific. Example: 'Checked network adapter settings. Found outdated driver version 12.1.0'")]
        string content,

        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketId);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var ticket = await _store.GetAsync(ticketId.ToUpperInvariant(), cancellationToken)
            ?? throw new InvalidOperationException($"Ticket '{ticketId}' not found.");

        ticket.Notes.Add(new TicketNote { Author = author, Content = content });

        await _store.UpdateAsync(ticket, cancellationToken);

        return new AddNoteResult
        {
            TicketId = ticketId,
            NoteCount = ticket.Notes.Count,
            Message = $"Note added to ticket {ticketId} by {author}."
        };
    }

    [McpServerTool(Name = "list_tickets")]
    [Description("""
        Lists tickets, optionally filtered by status.
        Use this to find open tickets, check workload, or look for similar issues to the current one.
        Returns summary of each ticket (not full details).
        Use get_ticket for full details of a specific ticket.
    """)]
    public async Task<ListTicketsResult> ListTicketsAsync(

        [Description("Filter by status. Leave empty for all tickets. Options: Open, InProgress, PendingUser, Resolved, Closed")]
        string? status = null,

        CancellationToken cancellationToken = default)
    {
        TicketStatus? statusFilter = null;

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<TicketStatus>(status, ignoreCase: true, out var parsed))
                throw new ArgumentException($"Invalid status filter '{status}'. Valid values: {string.Join(", ", Enum.GetNames<TicketStatus>())}");

            statusFilter = parsed;
        }

        var tickets = await _store.ListAsync(statusFilter, cancellationToken);

        return new ListTicketsResult
        {
            TotalCount = tickets.Count,
            Tickets = tickets.Select(t => new TicketSummary
            {
                TicketId = t.Id,
                Title = t.Title,
                Category = t.Category,
                Status = t.Status.ToString(),
                Priority = t.Priority.ToString(),
                ReportedBy = t.ReportedBy,
                CreatedAt = t.CreatedAt
            }).ToList()
        };
    }
}
