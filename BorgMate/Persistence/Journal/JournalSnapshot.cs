using System.Collections.Generic;
using BorgMate.Domain.Journal;

namespace BorgMate.Persistence.Journal;

public sealed record JournalSnapshot(IReadOnlyList<JournalEntry> Entries, int UnreadCount);