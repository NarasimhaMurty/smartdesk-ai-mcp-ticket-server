using System.Collections.Concurrent;
using SmartDesk.McpServer.Ticket.Models;

namespace SmartDesk.McpServer.Ticket.Services;

public class InMemoryTicketStore : ITicketStore
{
    private readonly ConcurrentDictionary<string, Models.Ticket> _tickets = new();
    private readonly ILogger<InMemoryTicketStore> _logger;

    public InMemoryTicketStore(ILogger<InMemoryTicketStore> logger)
    {
        _logger = logger;
        SeedSampleData();
    }

    public Task<Models.Ticket> CreateAsync(Models.Ticket ticket, CancellationToken ct = default)
    {
        if (_tickets.ContainsKey(ticket.Id))
            throw new InvalidOperationException($"Ticket with ID {ticket.Id} already exists.");

        _tickets[ticket.Id] = ticket;
        _logger.LogInformation("Ticket created: {TicketId} - {Title}", ticket.Id, ticket.Title);
        return Task.FromResult(ticket);
    }

    public Task<Models.Ticket?> GetAsync(string id, CancellationToken ct = default)
    {
        _tickets.TryGetValue(id, out var ticket);
        return Task.FromResult(ticket);
    }

    public Task<Models.Ticket> UpdateAsync(Models.Ticket ticket, CancellationToken ct = default)
    {
        if (!_tickets.ContainsKey(ticket.Id))
            throw new InvalidOperationException($"Ticket {ticket.Id} not found. Cannot update.");

        ticket = ticket with { UpdatedAt = DateTime.UtcNow };
        _tickets[ticket.Id] = ticket;
        return Task.FromResult(ticket);
    }

    public Task<IReadOnlyList<Models.Ticket>> ListAsync(TicketStatus? status = null, CancellationToken ct = default)
    {
        var query = _tickets.Values.AsEnumerable();

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        IReadOnlyList<Models.Ticket> result = query
            .OrderByDescending(t => t.CreatedAt)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<bool> ExistsAsync(string id, CancellationToken ct = default)
        => Task.FromResult(_tickets.ContainsKey(id));

    private void SeedSampleData()
    {
        var samples = new[]
        {
            new Models.Ticket
            {
                Id = "TKT-001",
                Title = "Cannot access company VPN",
                Description = "Getting authentication error when connecting to VPN",
                Category = "Network",
                Priority = TicketPriority.High,
                Status = TicketStatus.Open,
                ReportedBy = "john.smith@company.com"
            },
            new Models.Ticket
            {
                Id = "TKT-002",
                Title = "Outlook not syncing emails",
                Description = "Emails stuck in outbox for 2 hours",
                Category = "Email",
                Priority = TicketPriority.Medium,
                Status = TicketStatus.InProgress,
                ReportedBy = "jane.doe@company.com",
                AssignedTo = "TriageAgent"
            }
        };

        foreach (var ticket in samples)
            _tickets[ticket.Id] = ticket;
    }
}
