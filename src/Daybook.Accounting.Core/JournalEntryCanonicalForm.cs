using System.Globalization;
using System.Text;

namespace Daybook.Accounting.Core;

/// <summary>
/// A deterministic byte representation of a <see cref="JournalEntry"/>'s
/// frozen-at-post content, for the HMAC hash chain (spec §15.3).
/// </summary>
/// <remarks>
/// Covers every field CLAUDE.md buckets as "frozen at post" — deliberately
/// excludes anything tracked in <c>LineTagLedger</c>/<c>ReconciliationLedger</c>
/// (mutable after post, never accounting truth — including them would make
/// the chain break every time a line gets tagged or reconciled) and
/// <see cref="JournalEntry.EntryHash"/> itself (can't hash something that
/// includes its own hash). Built with <see cref="BinaryWriter"/> over a
/// fixed field order — deterministic and unambiguous, unlike JSON
/// canonicalization (key ordering, number formatting).
/// </remarks>
public static class JournalEntryCanonicalForm
{
    public static byte[] Serialize(JournalEntry entry)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8);

        writer.Write(entry.Id.ToByteArray());
        writer.Write(entry.EntryDate.ToString("O", CultureInfo.InvariantCulture));
        writer.Write(entry.Description);

        writer.Write(entry.Lines.Count);
        foreach (var line in entry.Lines)
        {
            writer.Write(line.AccountId.ToByteArray());
            writer.Write((int)line.Side);
            writer.Write(line.Amount.Amount);
            writer.Write(line.Amount.Currency.Code);
            WriteNullableString(writer, line.Memo);
        }

        writer.Write((int)entry.Status);
        WriteNullableInt(writer, entry.SequenceNumber);
        WriteNullableDateTimeOffset(writer, entry.PostedAtUtc);
        WriteNullableGuid(writer, entry.PostedByUserId);
        WriteNullableGuid(writer, entry.ReversesEntryId);
        writer.Write(entry.SchemaVersion);

        writer.Write(entry.References.Count);
        foreach (var reference in entry.References)
        {
            writer.Write((int)reference.Type);
            writer.Write(reference.Value);
        }

        return stream.ToArray();
    }

    private static void WriteNullableString(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null);
        if (value is not null)
        {
            writer.Write(value);
        }
    }

    private static void WriteNullableInt(BinaryWriter writer, int? value)
    {
        writer.Write(value.HasValue);
        if (value.HasValue)
        {
            writer.Write(value.Value);
        }
    }

    private static void WriteNullableGuid(BinaryWriter writer, Guid? value)
    {
        writer.Write(value.HasValue);
        if (value.HasValue)
        {
            writer.Write(value.Value.ToByteArray());
        }
    }

    private static void WriteNullableDateTimeOffset(BinaryWriter writer, DateTimeOffset? value)
    {
        writer.Write(value.HasValue);
        if (value.HasValue)
        {
            writer.Write(value.Value.ToUnixTimeMilliseconds());
        }
    }
}