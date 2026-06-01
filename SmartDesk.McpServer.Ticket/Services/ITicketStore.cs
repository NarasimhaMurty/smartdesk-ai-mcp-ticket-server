using SmartDesk.McpServer.Ticket.Models;

namespace SmartDesk.McpServer.Ticket.Services;

public interface ITicketStore
{
    Task<Models.Ticket> CreateAsync(Models.Ticket ticket, CancellationToken ct = default);
    Task<Models.Ticket?> GetAsync(string id, CancellationToken ct = default);
    Task<Models.Ticket> UpdateAsync(Models.Ticket ticket, CancellationToken ct = default);
    Task<IReadOnlyList<Models.Ticket>> ListAsync(TicketStatus? status = null, CancellationToken ct = default);
    Task<bool> ExistsAsync(string id, CancellationToken ct = default);
}
