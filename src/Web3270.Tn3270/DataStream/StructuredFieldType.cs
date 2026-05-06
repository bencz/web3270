namespace Web3270.Tn3270.DataStream;

/// <summary>
/// Structured Field IDs used inside Write Structured Field (0xF3) and the
/// inbound Read Structured Field response (AID 0x88).
/// </summary>
public static class StructuredFieldType
{
    public const byte ResetPartition = 0x00;
    public const byte ReadPartition = 0x01;
    public const byte EraseReset = 0x03;
    public const byte LoadProgrammedSymbols = 0x06;
    public const byte SetReplyMode = 0x09;
    public const byte SetWindowOrigin = 0x0B;
    public const byte CreatePartition = 0x0C;
    public const byte DestroyPartition = 0x0D;
    public const byte ActivatePartition = 0x0E;
    public const byte Outbound3270DS = 0x40;
    public const byte ScsData = 0x41;
    public const byte SelectFormatGroup = 0x4A;
    public const byte PresentAbsoluteFormat = 0x4B;
    public const byte PresentRelativeFormat = 0x4C;

    // Inbound (from terminal)
    public const byte Inbound3270DS = 0x80;
    public const byte QueryReply = 0x81;

    public const byte IndFile = 0xD0; // IND$FILE — file transfer
}

/// <summary>Query Reply field type bytes (RFC + IBM 3270 architecture).</summary>
public static class QueryReplyType
{
    public const byte Summary = 0x80;
    public const byte UsableArea = 0x81;
    public const byte AlphanumericPartitions = 0x84;
    public const byte CharacterSets = 0x85;
    public const byte Color = 0x86;
    public const byte Highlight = 0x87;
    public const byte ReplyModes = 0x88;
    public const byte OemAuxiliaryDevice = 0x8F;
    public const byte DistributedDataMgmt = 0x95;
    public const byte StoragePools = 0x96;
    public const byte AuxiliaryDevice = 0x99;
    public const byte RpqNames = 0xA1;
    public const byte ImplicitPartition = 0xA6;
    public const byte Transparency = 0xA8;
    public const byte Segment = 0xB0;
    public const byte Procedure = 0xB1;
    public const byte LineType = 0xB2;
    public const byte Port = 0xB3;
    public const byte GraphicColor = 0xB4;
    public const byte GraphicSymbolSets = 0xB6;
}