namespace Daybook.Accounting.Core;

/// <summary>
/// A book's status (spec §4.1). Data only for now — nothing in Core reads
/// this to gate behavior (e.g. an archived book does not reject postings);
/// that's a deliberate v1 scope cut, left for a later milestone once
/// there's a real caller pairing a <see cref="Book"/> with the
/// <see cref="Journal"/> it governs.
/// </summary>
public enum BookStatus
{
    Open,
    Archived,
}