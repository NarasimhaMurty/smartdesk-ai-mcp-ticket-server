# SmartDesk AI   MCP Ticket Server: Complete Implementation Guide

## Table of Contents
1. [What Is This Project?](#1-what-is-this-project)
2. [Architecture Overview](#2-architecture-overview)
3. [How MCP Works](#3-how-mcp-works)
4. [Project Structure](#4-project-structure)
5. [Prerequisites](#5-prerequisites)
6. [Step-by-Step Implementation](#6-step-by-step-implementation)
7. [How to Run & Test](#7-how-to-run--test)
8. [MCP Tool Reference](#8-mcp-tool-reference)
9. [Design Decisions Explained](#9-design-decisions-explained)

---

## 1. What Is This Project?

**SmartDesk AI** is an **IT Helpdesk MCP Server** built with **.NET 10** and the **Model Context Protocol (MCP)**.

### What Problem Does It Solve?
Traditional AI chat assistants are *passive*   they answer questions but cannot *take actions*.  
This project makes AI agents **active participants** in IT helpdesk workflows by exposing real tools that agents can call:

| Tool | What the AI Can Do |
|---|---|
| `create_ticket` | Log a new IT support ticket |
| `get_ticket` | Read full ticket details |
| `update_ticket_status` | Move ticket through the workflow |
| `add_ticket_note` | Document investigation steps |
| `list_tickets` | View open/in-progress workload |

### Real-World Use Case
```
User ? AI Agent ? MCP Server (this project) ? Ticket System
         ?                                         ?
         ????????????? Response ????????????????????
```
An AI agent (Claude, GPT-4, Copilot etc.) can now say:  
*"I'll create a ticket for your VPN issue"*   and **actually do it**.

---

## 2. Architecture Overview

```
SmartDesk.AI/
??? SmartDesk.McpServer.Ticket/          ? Single ASP.NET Core project
    ??? Program.cs                        ? App entry point & DI setup
    ??? Models/
    ?   ??? Ticket.cs                     ? Core domain model
    ?   ??? TicketResult.cs               ? Tool return types (DTOs)
    ??? Services/
    ?   ??? ITicketStore.cs               ? Abstraction (interface)
    ?   ??? InMemoryTicketStore.cs        ? In-memory implementation
    ??? Tools/
        ??? TicketTools.cs               ? 5 MCP tools exposed to AI
```

### Dependency Flow
```
AI Agent
   ?
   ? HTTP POST /mcp
ASP.NET Core (Program.cs)
   ?
   ? MCP SDK routes to
TicketTools (constructor-injected)
   ?
   ? calls via interface
ITicketStore
   ?
   ? implemented by
InMemoryTicketStore (Singleton)
```

### Key Design Principles
- **Interface + Implementation**: `ITicketStore` ? swap in-memory for DB/ServiceNow with zero tool code change
- **Singleton Store**: In-memory state must survive between agent calls
- **Stateless HTTP Transport**: Each MCP tool call is independent   scales horizontally
- **Record types**: Immutable-by-default DTOs with value equality
- **Thread-safe**: `ConcurrentDictionary` handles simultaneous agent calls

---

## 3. How MCP Works

**Model Context Protocol (MCP)** is an open standard (by Anthropic) that lets AI models call external tools over HTTP.

### Request Flow
```
1. AI sends:  POST /mcp
              { "method": "tools/call",
                "params": { "name": "create_ticket",
                            "arguments": { "title": "VPN broken", ... }}}

2. MCP SDK routes to ? CreateTicketAsync() in TicketTools.cs

3. Tool executes ? calls ITicketStore ? returns result

4. MCP SDK serializes result ? JSON response to AI

5. AI reads result and decides next action
```

### Why Stateless Mode?
```csharp
options.Stateless = true;
```
- **Stateless** = No session tracking per connection
- Each tool call is self-contained
- AI agents manage their own conversation state
- Easier to scale (any server instance handles any request)

---

## 4. Project Structure

```
SmartDesk.AI/
??? SmartDesk.McpServer.Ticket/
    ??? SmartDesk.McpServer.Ticket.csproj
    ??? Program.cs
    ??? Models/
    ?   ??? Ticket.cs
    ?   ??? TicketResult.cs
    ??? Services/
    ?   ??? ITicketStore.cs
    ?   ??? InMemoryTicketStore.cs
    ??? Tools/
        ??? TicketTools.cs
```

---

## 5. Prerequisites

| Requirement | Version | Check Command |
|---|---|---|
| .NET SDK | 10.0+ | `dotnet --version` |
| OS | Windows / macOS / Linux |   |
| Git (optional) | Any | `git --version` |

Install .NET 10: https://dotnet.microsoft.com/download/dotnet/10.0

---

## 6. Step-by-Step Implementation

Run every command **in order** in your terminal (PowerShell or bash).

---

### Step 1   Create Directory & Project

```powershell
mkdir SmartDesk.AI
cd SmartDesk.AI

dotnet new sln -n SmartDesk.AI

dotnet new web -n SmartDesk.McpServer.Ticket --framework net10.0

dotnet sln add SmartDesk.McpServer.Ticket/SmartDesk.McpServer.Ticket.csproj
```

---

### Step 2   Add NuGet Package

```powershell
cd SmartDesk.McpServer.Ticket
dotnet add package ModelContextProtocol.AspNetCore --version 1.3.0
cd ..
```

---

### Step 3   Edit the `.csproj` File

Replace the content of `SmartDesk.McpServer.Ticket/SmartDesk.McpServer.Ticket.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="ModelContextProtocol.AspNetCore" Version="1.3.0" />
  </ItemGroup>

</Project>
```

---

### Step 4   Create Folder Structure

```powershell
mkdir SmartDesk.McpServer.Ticket/Models
mkdir SmartDesk.McpServer.Ticket/Services
mkdir SmartDesk.McpServer.Ticket/Tools
```

---

### Step 5   Create `Models/Ticket.cs`

Create file `SmartDesk.McpServer.Ticket/Models/Ticket.cs`:

```csharp
namespace SmartDesk.McpServer.Ticket.Models;

// WHY record with mutable properties here?
// ? Tickets CHANGE state over their lifetime
//   (Open ? InProgress ? Resolved ? Closed)
// ? init-only records cannot be updated
// ? We use { get; set; } for mutable state
// ? BUT we still use record for value equality

public record Ticket
{
    public string Id { get; init; } = string.Empty;
    // WHY init for Id?
    // ? Ticket ID never changes after creation
    // ? Immutable identity

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    // WHY set for these?
    // ? Agents may update title/description after triage

    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;

    public string ReportedBy { get; init; } = string.Empty;
    // WHY init for ReportedBy?
    // ? Who reported it never changes

    public string? AssignedTo { get; set; }
    // WHY nullable?
    // ? New ticket = not yet assigned to anyone

    public List<TicketNote> Notes { get; init; } = [];
    // WHY init for Notes list?
    // ? The LIST itself doesn't change (same object)
    // ? But we ADD items to it
    // ? init = you cannot replace the list
    //   but you can call .Add() on it

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    // WHY nullable DateTime?
    // ? Ticket may not be resolved yet
    // ? null = not resolved, DateTime = resolved at this time
}

public record TicketNote
{
    public string Author { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public DateTime AddedAt { get; init; } = DateTime.UtcNow;
}

// WHY enums instead of strings?
// ? string "opne" (typo) compiles fine but is wrong
// ? TicketStatus.Opne does NOT compile ? caught immediately
// ? Enums = compile-time safety
// ? AI gets exact valid values from JSON schema
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
```

---

### Step 6   Create `Models/TicketResult.cs`

Create file `SmartDesk.McpServer.Ticket/Models/TicketResult.cs`:

```csharp
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
```

---

### Step 7   Create `Services/ITicketStore.cs`

Create file `SmartDesk.McpServer.Ticket/Services/ITicketStore.cs`:

```csharp
using SmartDesk.McpServer.Ticket.Models;

namespace SmartDesk.McpServer.Ticket.Services;

// WHY interface?
// ? Today: InMemoryTicketStore
// ? Tomorrow: ServiceNowTicketStore, CosmosDbTicketStore
// ? Agents never know which implementation is running
// ? SOLID: Dependency Inversion
public interface ITicketStore
{
    Task<Models.Ticket> CreateAsync(Models.Ticket ticket, CancellationToken ct = default);
    Task<Models.Ticket?> GetAsync(string id, CancellationToken ct = default);
    Task<Models.Ticket> UpdateAsync(Models.Ticket ticket, CancellationToken ct = default);
    Task<IReadOnlyList<Models.Ticket>> ListAsync(TicketStatus? status = null, CancellationToken ct = default);
    Task<bool> ExistsAsync(string id, CancellationToken ct = default);
}
```

---

### Step 8   Create `Services/InMemoryTicketStore.cs`

Create file `SmartDesk.McpServer.Ticket/Services/InMemoryTicketStore.cs`:

```csharp
using SmartDesk.McpServer.Ticket.Models;

namespace SmartDesk.McpServer.Ticket.Services;

// WHY Singleton lifetime?
// ? In-memory store = data lives in this object
// ? Singleton = same instance for entire app
// ? If Scoped/Transient = new instance per request
//   = data disappears between calls!
// ? RULE: Stateful in-memory services = always Singleton

public class InMemoryTicketStore : ITicketStore
{
    // WHY ConcurrentDictionary and not Dictionary?
    // ? Multiple agents may call tools simultaneously
    // ? Regular Dictionary is NOT thread-safe
    // ? Two agents writing at same time = data corruption
    // ? ConcurrentDictionary = thread-safe by design
    // ? In production (ServiceNow/DB) this is handled by the DB
    //   but in-memory we must handle it ourselves
    private readonly System.Collections.Concurrent
        .ConcurrentDictionary<string, Models.Ticket> _tickets = new();

    private readonly ILogger<InMemoryTicketStore> _logger;

    public InMemoryTicketStore(ILogger<InMemoryTicketStore> logger)
    {
        _logger = logger;
        SeedSampleData();
        // WHY seed data?
        // ? Demo and testing without manual setup
        // ? Shows agents working with existing tickets
        // ? Agents can list, search, update existing tickets
    }

    public Task<Models.Ticket> CreateAsync(
        Models.Ticket ticket,
        CancellationToken ct = default)
    {
        // WHY check for duplicate Id?
        // ? Should never happen (we generate GUIDs)
        // ? But defensive programming = always check
        if (_tickets.ContainsKey(ticket.Id))
            throw new InvalidOperationException(
                $"Ticket with ID {ticket.Id} already exists.");

        _tickets[ticket.Id] = ticket;

        _logger.LogInformation(
            "Ticket created: {TicketId} - {Title}",
            ticket.Id, ticket.Title);

        // WHY Task.FromResult?
        // ? Interface requires Task<T> (async contract)
        // ? In-memory is sync but we honour the async interface
        // ? Real implementation (DB/API) will be truly async
        return Task.FromResult(ticket);
    }

    public Task<Models.Ticket?> GetAsync(
        string id,
        CancellationToken ct = default)
    {
        _tickets.TryGetValue(id, out var ticket);

        // WHY return null instead of throwing?
        // ? Caller decides how to handle "not found"
        // ? Tool can give specific "ticket not found" message
        // ? Throwing here = less flexible
        return Task.FromResult(ticket);
    }

    public Task<Models.Ticket> UpdateAsync(
        Models.Ticket ticket,
        CancellationToken ct = default)
    {
        if (!_tickets.ContainsKey(ticket.Id))
            throw new InvalidOperationException(
                $"Ticket {ticket.Id} not found. Cannot update.");

        ticket = ticket with { UpdatedAt = DateTime.UtcNow };
        // WHY "with" expression?
        // ? record with { Property = value }
        //   creates a NEW record with that property changed
        // ? Preserves immutability principle
        // ? Original ticket unchanged, new ticket has updated time

        _tickets[ticket.Id] = ticket;
        return Task.FromResult(ticket);
    }

    public Task<IReadOnlyList<Models.Ticket>> ListAsync(
        TicketStatus? status = null,
        CancellationToken ct = default)
    {
        var query = _tickets.Values.AsEnumerable();

        // WHY optional status filter?
        // ? Agent might ask: "List all Open tickets"
        // ? Or: "List all tickets" (no filter)
        // ? One method handles both cases
        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        // WHY OrderByDescending on CreatedAt?
        // ? Most recent tickets first
        // ? Agent sees newest issues first
        // ? More useful for IT helpdesk context
        IReadOnlyList<Models.Ticket> result = query
            .OrderByDescending(t => t.CreatedAt)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<bool> ExistsAsync(
        string id,
        CancellationToken ct = default)
        => Task.FromResult(_tickets.ContainsKey(id));

    private void SeedSampleData()
    {
        // WHY seed data in production-like format?
        // ? Agents practice on realistic data
        // ? Demos look professional
        // ? Testing without manual ticket creation
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
```

---

### Step 9   Create `Tools/TicketTools.cs`

Create file `SmartDesk.McpServer.Ticket/Tools/TicketTools.cs`:

```csharp
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

    // ?? TOOL 1: create_ticket ?????????????????????????????????????

    [McpServerTool(Name = "create_ticket")]
    [Description("""
        Creates a new IT support ticket in the helpdesk system.
        Use this when a user reports a new IT issue.
        Call this FIRST before any resolution attempts.
        Returns the ticket ID which you must use for all
        subsequent operations on this ticket.
    """)]
    public async Task<CreateTicketResult> CreateTicketAsync(

        [Description("Short, clear title of the IT issue. " +
                     "Example: 'Cannot connect to VPN', 'Outlook not opening'")]
        string title,

        [Description("Detailed description of the issue including " +
                     "error messages, when it started, what was tried")]
        string description,

        [Description("Issue category. One of: Network, Email, Hardware, " +
                     "Software, Access, Security, Other")]
        string category,

        [Description("Reporter's email address")]
        string reportedBy,

        [Description("Priority: Low, Medium, High, Critical. " +
                     "Default is Medium. Use Critical only for " +
                     "business-stopping issues.")]
        TicketPriority priority = TicketPriority.Medium,

        CancellationToken cancellationToken = default)
    {
        // Validation
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportedBy);

        var validCategories = new[]
        {
            "Network", "Email", "Hardware",
            "Software", "Access", "Security", "Other"
        };

        if (!validCategories.Contains(category,
            StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Invalid category '{category}'. " +
                $"Must be one of: {string.Join(", ", validCategories)}");
        }

        // WHY generate ID here and not in the store?
        // ? Tool owns the ID generation logic
        // ? Format: TKT-{timestamp}-{random}
        //   Timestamp = easy sorting
        //   Random suffix = avoids collisions
        var ticketId = $"TKT-{DateTime.UtcNow:yyyyMMddHHmmss}-" +
                       $"{Guid.NewGuid().ToString()[..4].ToUpperInvariant()}";

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
            Message = $"Ticket {created.Id} created successfully. " +
                      $"Category: {created.Category}, Priority: {created.Priority}"
        };
    }

    // ?? TOOL 2: get_ticket ????????????????????????????????????????

    [McpServerTool(Name = "get_ticket")]
    [Description("""
        Retrieves full details of an existing ticket by its ID.
        Use this to check current status, read notes, or get
        context before taking action on a ticket.
        Returns null-like error if ticket ID does not exist.
    """)]
    public async Task<GetTicketResult> GetTicketAsync(

        [Description("Ticket ID to retrieve. " +
                     "Format: TKT-YYYYMMDDHHMMSS-XXXX. " +
                     "Example: TKT-001 or TKT-20240101120000-A3B2")]
        string ticketId,

        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketId);

        var ticket = await _store.GetAsync(
            ticketId.ToUpperInvariant().Trim(), cancellationToken);

        if (ticket is null)
            throw new InvalidOperationException(
                $"Ticket '{ticketId}' not found. " +
                "Verify the ticket ID is correct.");

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
            Notes = ticket.Notes
                .Select(n => $"[{n.AddedAt:HH:mm}] {n.Author}: {n.Content}")
                .ToList(),
            CreatedAt = ticket.CreatedAt,
            UpdatedAt = ticket.UpdatedAt
        };
    }

    // ?? TOOL 3: update_ticket_status ?????????????????????????????

    [McpServerTool(Name = "update_ticket_status")]
    [Description("""
        Updates the status and optionally the assignee of a ticket.
        Use this when ticket progresses through resolution stages.
        Status flow: Open ? InProgress ? PendingUser ? Resolved ? Closed.
        Always add a note explaining why the status changed.
    """)]
    public async Task<UpdateTicketResult> UpdateTicketStatusAsync(

        [Description("ID of the ticket to update")]
        string ticketId,

        [Description("New status. Valid values: " +
                     "Open, InProgress, PendingUser, Resolved, Closed")]
        string status,

        [Description("Note explaining reason for status change. " +
                     "Example: 'Started investigation of network settings'")]
        string note,

        [Description("Agent or person handling this ticket. " +
                     "Example: 'TriageAgent', 'john.doe@company.com'")]
        string? assignTo = null,

        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketId);
        ArgumentException.ThrowIfNullOrWhiteSpace(note);

        if (!Enum.TryParse<TicketStatus>(status, ignoreCase: true,
            out var newStatus))
        {
            throw new ArgumentException(
                $"Invalid status '{status}'. " +
                $"Valid values: {string.Join(", ", Enum.GetNames<TicketStatus>())}");
        }

        var ticket = await _store.GetAsync(
            ticketId.ToUpperInvariant(), cancellationToken)
            ?? throw new InvalidOperationException(
                $"Ticket '{ticketId}' not found.");

        // WHY "with" expression for updating?
        // ? Records encourage immutability
        // ? Create new record with changes
        // ? Old record unchanged
        // ? Clean state management
        var updatedTicket = ticket with
        {
            Status = newStatus,
            AssignedTo = assignTo ?? ticket.AssignedTo,
            ResolvedAt = newStatus == TicketStatus.Resolved
                ? DateTime.UtcNow
                : ticket.ResolvedAt
            // WHY set ResolvedAt automatically?
            // ? When status = Resolved, capture exact time
            // ? Agents don't need to remember to set this
            // ? Business rule enforced at data layer
        };

        // Add the note to history
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

    // ?? TOOL 4: add_ticket_note ???????????????????????????????????

    [McpServerTool(Name = "add_ticket_note")]
    [Description("""
        Adds a note/comment to an existing ticket without changing status.
        Use this to document investigation steps, attempted solutions,
        or communication with the user.
        Notes are permanent and visible to all agents handling the ticket.
    """)]
    public async Task<AddNoteResult> AddTicketNoteAsync(

        [Description("ID of the ticket to add note to")]
        string ticketId,

        [Description("Author of this note. " +
                     "Use agent name or email. " +
                     "Example: 'TriageAgent', 'KnowledgeAgent'")]
        string author,

        [Description("Note content. Be specific. " +
                     "Example: 'Checked network adapter settings. " +
                     "Found outdated driver version 12.1.0'")]
        string content,

        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketId);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var ticket = await _store.GetAsync(
            ticketId.ToUpperInvariant(), cancellationToken)
            ?? throw new InvalidOperationException(
                $"Ticket '{ticketId}' not found.");

        ticket.Notes.Add(new TicketNote
        {
            Author = author,
            Content = content
        });

        await _store.UpdateAsync(ticket, cancellationToken);

        return new AddNoteResult
        {
            TicketId = ticketId,
            NoteCount = ticket.Notes.Count,
            Message = $"Note added to ticket {ticketId} by {author}."
        };
    }

    // ?? TOOL 5: list_tickets ??????????????????????????????????????

    [McpServerTool(Name = "list_tickets")]
    [Description("""
        Lists tickets, optionally filtered by status.
        Use this to find open tickets, check workload,
        or look for similar issues to the current one.
        Returns summary of each ticket (not full details).
        Use get_ticket for full details of a specific ticket.
    """)]
    public async Task<ListTicketsResult> ListTicketsAsync(

        [Description("Filter by status. Leave empty for all tickets. " +
                     "Options: Open, InProgress, PendingUser, Resolved, Closed")]
        string? status = null,

        CancellationToken cancellationToken = default)
    {
        TicketStatus? statusFilter = null;

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<TicketStatus>(status, ignoreCase: true,
                out var parsed))
            {
                throw new ArgumentException(
                    $"Invalid status filter '{status}'. " +
                    $"Valid values: {string.Join(", ", Enum.GetNames<TicketStatus>())}");
            }
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
```

---

### Step 10   Create `Program.cs`

Replace the content of `SmartDesk.McpServer.Ticket/Program.cs`:

```csharp
using ModelContextProtocol.AspNetCore;
using SmartDesk.McpServer.Ticket.Services;
using SmartDesk.McpServer.Ticket.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        // WHY Stateless = true?
        // ? No session tracking needed
        // ? Each tool call is independent
        // ? Perfect for AI agents (they manage their own state)
        // ? Simpler, easier to test, easier to scale
        // ? In production: stateless = horizontally scalable
        //   (any server instance handles any request)
        options.Stateless = true;
    })
    .WithTools<TicketTools>();

// WHY Singleton?
// ? In-memory store holds data in RAM
// ? Singleton = same object for app lifetime
// ? Scoped/Transient = new object per request = data loss!
builder.Services.AddSingleton<ITicketStore, InMemoryTicketStore>();

builder.Services.AddLogging(l =>
{
    l.AddConsole();
    l.SetMinimumLevel(LogLevel.Information);
});

var app = builder.Build();

// MCP endpoint   AI agents connect here
app.MapMcp("/mcp");

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    server = "TicketMcpServer",
    version = "1.0.0",
    timestamp = DateTime.UtcNow
}));

// Debug endpoint   see all tickets in memory
app.MapGet("/debug/tickets", async (ITicketStore store) =>
{
    var tickets = await store.ListAsync();
    return Results.Ok(tickets);
});

app.Run();
```

---

### Step 11   Build & Verify

```powershell
cd SmartDesk.AI
dotnet build
```

Expected output:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

### Step 12   Run the Server

```powershell
cd SmartDesk.McpServer.Ticket
dotnet run
```

Expected console output:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
info: Microsoft.Hosting.Lifetime[0]
      Application started.
```

---

## 7. How to Run & Test

### Test Health Endpoint
```powershell
curl http://localhost:5000/health
```
Expected:
```json
{"status":"healthy","server":"TicketMcpServer","version":"1.0.0","timestamp":"..."}
```

### Test Debug Endpoint (see seeded tickets)
```powershell
curl http://localhost:5000/debug/tickets
```
Expected: JSON array with TKT-001 and TKT-002.

### Test MCP Tools via curl

**List all tickets:**
```powershell
curl -X POST http://localhost:5000/mcp `
  -H "Content-Type: application/json" `
  -d '{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": {
      "name": "list_tickets",
      "arguments": {}
    }
  }'
```

**Create a ticket:**
```powershell
curl -X POST http://localhost:5000/mcp `
  -H "Content-Type: application/json" `
  -d '{
    "jsonrpc": "2.0",
    "id": 2,
    "method": "tools/call",
    "params": {
      "name": "create_ticket",
      "arguments": {
        "title": "Laptop screen flickering",
        "description": "Screen flickers every 5 minutes since morning",
        "category": "Hardware",
        "reportedBy": "bob@company.com",
        "priority": "High"
      }
    }
  }'
```

**Get a specific ticket:**
```powershell
curl -X POST http://localhost:5000/mcp `
  -H "Content-Type: application/json" `
  -d '{
    "jsonrpc": "2.0",
    "id": 3,
    "method": "tools/call",
    "params": {
      "name": "get_ticket",
      "arguments": { "ticketId": "TKT-001" }
    }
  }'
```

**Update ticket status:**
```powershell
curl -X POST http://localhost:5000/mcp `
  -H "Content-Type: application/json" `
  -d '{
    "jsonrpc": "2.0",
    "id": 4,
    "method": "tools/call",
    "params": {
      "name": "update_ticket_status",
      "arguments": {
        "ticketId": "TKT-001",
        "status": "InProgress",
        "note": "Investigating VPN authentication logs",
        "assignTo": "TriageAgent"
      }
    }
  }'
```

**Add a note:**
```powershell
curl -X POST http://localhost:5000/mcp `
  -H "Content-Type: application/json" `
  -d '{
    "jsonrpc": "2.0",
    "id": 5,
    "method": "tools/call",
    "params": {
      "name": "add_ticket_note",
      "arguments": {
        "ticketId": "TKT-001",
        "author": "TriageAgent",
        "content": "User confirmed the issue started after Windows Update KB5031455"
      }
    }
  }'
```

### Discover Available Tools
```powershell
curl -X POST http://localhost:5000/mcp `
  -H "Content-Type: application/json" `
  -d '{
    "jsonrpc": "2.0",
    "id": 6,
    "method": "tools/list",
    "params": {}
  }'
```

---

## 8. MCP Tool Reference

### `create_ticket`
| Parameter | Type | Required | Description |
|---|---|---|---|
| `title` | string | ? | Short issue title |
| `description` | string | ? | Detailed description |
| `category` | string | ? | Network / Email / Hardware / Software / Access / Security / Other |
| `reportedBy` | string | ? | Reporter email |
| `priority` | enum | ? | Low / Medium / High / Critical (default: Medium) |

**Returns:** `CreateTicketResult`   includes new `TicketId`

---

### `get_ticket`
| Parameter | Type | Required | Description |
|---|---|---|---|
| `ticketId` | string | ? | Ticket ID (e.g. TKT-001) |

**Returns:** `GetTicketResult`   full details including all notes

---

### `update_ticket_status`
| Parameter | Type | Required | Description |
|---|---|---|---|
| `ticketId` | string | ? | Ticket ID |
| `status` | string | ? | Open / InProgress / PendingUser / Resolved / Closed |
| `note` | string | ? | Reason for status change |
| `assignTo` | string | ? | Agent/person to assign |

**Returns:** `UpdateTicketResult`

---

### `add_ticket_note`
| Parameter | Type | Required | Description |
|---|---|---|---|
| `ticketId` | string | ? | Ticket ID |
| `author` | string | ? | Note author |
| `content` | string | ? | Note text |

**Returns:** `AddNoteResult`   includes total note count

---

### `list_tickets`
| Parameter | Type | Required | Description |
|---|---|---|---|
| `status` | string | ? | Filter by status (empty = all tickets) |

**Returns:** `ListTicketsResult`   list of `TicketSummary` objects

---

## 9. Design Decisions Explained

### Why `record` types?
- Value equality by default   two tickets with same data are "equal"
- `with` expressions create modified copies cleanly
- `init`-only properties enforce immutability where needed

### Why `ConcurrentDictionary`?
- MCP server may receive concurrent tool calls from multiple AI agents
- Regular `Dictionary<K,V>` is **not thread-safe**   concurrent writes corrupt data
- `ConcurrentDictionary` is thread-safe by design at zero extra cost

### Why Singleton for `InMemoryTicketStore`?
```
Singleton  ? 1 instance, data persists ?
Scoped     ? 1 per HTTP request, data gone after request ?
Transient  ? 1 per injection, data gone after injection ?
```

### Why an `ITicketStore` interface?
Swap the backing store with **zero changes** to `TicketTools`:
```csharp
// Today (demo):
builder.Services.AddSingleton<ITicketStore, InMemoryTicketStore>();

// Tomorrow (production):
builder.Services.AddScoped<ITicketStore, ServiceNowTicketStore>();
// or:
builder.Services.AddScoped<ITicketStore, CosmosDbTicketStore>();
```

### Why Stateless HTTP transport?
- Each MCP tool call is a complete, independent HTTP request
- Server does not track client sessions
- Horizontally scalable   spin up 10 instances behind a load balancer and it just works
- AI agents are responsible for their own conversation context

### Ticket Lifecycle
```
Open ? InProgress ? PendingUser ? Resolved ? Closed
```
- `Open`: Newly created, awaiting action
- `InProgress`: Agent is actively working on it
- `PendingUser`: Waiting for user response/confirmation
- `Resolved`: Solution applied, awaiting closure
- `Closed`: Ticket fully closed

### ID Format: `TKT-{yyyyMMddHHmmss}-{XXXX}`
- Timestamp prefix enables chronological sorting
- 4-character random GUID suffix prevents collisions
- Human-readable (not raw GUIDs)
- Seeded demo tickets use short IDs: `TKT-001`, `TKT-002`

---

## Complete File Checklist

After following this guide you should have:

```
SmartDesk.AI/
??? SmartDesk.AI.sln
??? SmartDesk.McpServer.Ticket/
    ??? SmartDesk.McpServer.Ticket.csproj   ? net10.0 + MCP package
    ??? Program.cs                           ? DI + MCP setup + endpoints
    ??? Models/
    ?   ??? Ticket.cs                        ? Domain model + enums
    ?   ??? TicketResult.cs                  ? Tool return DTOs
    ??? Services/
    ?   ??? ITicketStore.cs                  ? Store interface
    ?   ??? InMemoryTicketStore.cs           ? Thread-safe in-memory impl
    ??? Tools/
        ??? TicketTools.cs                   ? 5 MCP tools
```

Run `dotnet build`   if output shows `Build succeeded. 0 Error(s)`, you are done. ?


