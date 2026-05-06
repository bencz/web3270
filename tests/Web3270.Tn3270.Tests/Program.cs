using Web3270.Tn3270;
using Web3270.Tn3270.DataStream;

namespace Web3270.Tn3270.Tests;

internal static class Program
{
    private static int Main()
    {
        var failures = new List<string>();
        Run(failures, "ReadModified uses the current cursor address", ReadModifiedUsesCurrentCursorAddress);
        Run(failures, "ReadModified emits all non-null bytes from an MDT field",
            ReadModifiedEmitsAllNonNullBytesFromModifiedField);
        Run(failures, "ReadModified skips protected MDT fields", ReadModifiedSkipsProtectedModifiedFields);
        Run(failures, "ReadModifiedAll does not emit unmodified fields", ReadModifiedAllDoesNotEmitUnmodifiedFields);

        if (failures.Count == 0)
        {
            Console.WriteLine("All tests passed.");
            return 0;
        }

        foreach (var failure in failures) 
            Console.Error.WriteLine(failure);
        return 1;
    }

    private static void Run(List<string> failures, string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            failures.Add($"FAIL {name}: {ex.Message}");
        }
    }

    private static void ReadModifiedUsesCurrentCursorAddress()
    {
        var session = new Tn3270Session(new Tn3270SessionOptions
        {
            Rows = 24,
            Columns = 80
        });

        var buffer = session.Buffer;
        const int fieldAttributeAddress = 1770;
        const int inputStartAddress = fieldAttributeAddress + 1;

        buffer[fieldAttributeAddress].IsFieldAttribute = true;
        buffer[fieldAttributeAddress].Attribute = new FieldAttr(0xC0);
        buffer[fieldAttributeAddress].Character = 0x40;
        buffer.CursorAddress = inputStartAddress;
        buffer.KeyboardLocked = false;

        foreach (var ch in "CUL8TR") session.TypeCharacter(ch);

        var record = new Tn3270OutboundBuilder(buffer).BuildReadModified(AidKey.Enter);
        var expectedCursor = BufferAddress.Encode12(inputStartAddress + 6);
        var expectedSba = BufferAddress.Encode12(inputStartAddress);

        AssertEqual(inputStartAddress + 6, buffer.CursorAddress, "buffer cursor");
        AssertByte(AidKey.Enter, record[0], "AID");
        AssertByte(expectedCursor.hi, record[1], "cursor hi");
        AssertByte(expectedCursor.lo, record[2], "cursor lo");
        AssertByte(Tn3270Order.SBA, record[3], "SBA");
        AssertByte(expectedSba.hi, record[4], "SBA hi");
        AssertByte(expectedSba.lo, record[5], "SBA lo");
        AssertBytes([0xC3, 0xE4, 0xD3, 0xF8, 0xE3, 0xD9], record[6..], "typed EBCDIC bytes");
    }

    private static void ReadModifiedEmitsAllNonNullBytesFromModifiedField()
    {
        var session = new Tn3270Session(new Tn3270SessionOptions
        {
            Rows = 24,
            Columns = 80
        });

        var buffer = session.Buffer;
        const int fieldAttributeAddress = 1770;
        const int inputStartAddress = fieldAttributeAddress + 1;
        const int nextFieldAttributeAddress = inputStartAddress + 9;

        buffer[fieldAttributeAddress].IsFieldAttribute = true;
        buffer[fieldAttributeAddress].Attribute = new FieldAttr(0xC0);
        buffer[fieldAttributeAddress].Character = 0x40;
        buffer[nextFieldAttributeAddress].IsFieldAttribute = true;
        buffer[nextFieldAttributeAddress].Attribute = new FieldAttr(0xF0);
        buffer[nextFieldAttributeAddress].Character = 0x40;

        for (var address = inputStartAddress; address < nextFieldAttributeAddress; address++)
        {
            buffer[address].Character = 0x40;
            buffer[address].Modified = false;
        }

        buffer.CursorAddress = inputStartAddress;
        buffer.KeyboardLocked = false;

        foreach (var ch in "HERC01") session.TypeCharacter(ch);

        var record = new Tn3270OutboundBuilder(buffer).BuildReadModified(AidKey.Enter);
        var expectedCursor = BufferAddress.Encode12(inputStartAddress + 6);
        var expectedSba = BufferAddress.Encode12(inputStartAddress);

        AssertByte(AidKey.Enter, record[0], "AID");
        AssertByte(expectedCursor.hi, record[1], "cursor hi");
        AssertByte(expectedCursor.lo, record[2], "cursor lo");
        AssertByte(Tn3270Order.SBA, record[3], "SBA");
        AssertByte(expectedSba.hi, record[4], "SBA hi");
        AssertByte(expectedSba.lo, record[5], "SBA lo");
        AssertBytes(
            [0xC8, 0xC5, 0xD9, 0xC3, 0xF0, 0xF1, 0x40, 0x40, 0x40],
            record[6..],
            "full MDT field bytes");
    }

    private static void ReadModifiedSkipsProtectedModifiedFields()
    {
        var session = new Tn3270Session(new Tn3270SessionOptions
        {
            Rows = 24,
            Columns = 80
        });

        var buffer = session.Buffer;
        const int protectedAttributeAddress = 100;
        const int protectedStartAddress = protectedAttributeAddress + 1;

        buffer[protectedAttributeAddress].IsFieldAttribute = true;
        buffer[protectedAttributeAddress].Attribute = new FieldAttr(0xE1);
        buffer[protectedAttributeAddress].Character = 0x40;
        buffer[protectedStartAddress].Character = 0xC8;
        buffer[protectedStartAddress].Modified = true;
        buffer.CursorAddress = protectedStartAddress;

        var record = new Tn3270OutboundBuilder(buffer).BuildReadModified(AidKey.Enter);

        AssertEqual(3, record.Length, "record length");
        AssertByte(AidKey.Enter, record[0], "AID");
    }

    private static void ReadModifiedAllDoesNotEmitUnmodifiedFields()
    {
        var session = new Tn3270Session(new Tn3270SessionOptions
        {
            Rows = 24,
            Columns = 80
        });

        var buffer = session.Buffer;
        const int fieldAttributeAddress = 1770;
        const int inputStartAddress = fieldAttributeAddress + 1;
        const int nextFieldAttributeAddress = inputStartAddress + 6;

        buffer[fieldAttributeAddress].IsFieldAttribute = true;
        buffer[fieldAttributeAddress].Attribute = new FieldAttr(0xC0);
        buffer[fieldAttributeAddress].Character = 0x40;
        buffer[nextFieldAttributeAddress].IsFieldAttribute = true;
        buffer[nextFieldAttributeAddress].Attribute = new FieldAttr(0xF0);
        buffer[nextFieldAttributeAddress].Character = 0x40;

        for (var address = inputStartAddress; address < nextFieldAttributeAddress; address++)
        {
            buffer[address].Character = 0x40;
            buffer[address].Modified = false;
        }

        buffer.CursorAddress = inputStartAddress;

        var record = new Tn3270OutboundBuilder(buffer).BuildReadModified(AidKey.Enter, true);

        AssertEqual(3, record.Length, "record length");
        AssertByte(AidKey.Enter, record[0], "AID");
    }

    private static void AssertEqual(int expected, int actual, string label)
    {
        if (actual != expected)
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }

    private static void AssertByte(byte expected, byte actual, string label)
    {
        if (actual != expected)
            throw new InvalidOperationException($"{label}: expected 0x{expected:X2}, got 0x{actual:X2}");
    }

    private static void AssertBytes(byte[] expected, byte[] actual, string label)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException(
                $"{label}: expected {Hex(expected)}, got {Hex(actual)}");
    }

    private static string Hex(IEnumerable<byte> bytes) 
        => string.Join(" ", bytes.Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
}