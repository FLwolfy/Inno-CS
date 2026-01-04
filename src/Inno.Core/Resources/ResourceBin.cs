namespace Inno.Core.Resources;

public readonly struct ResourceBin(string sourceName, byte[] sourceBytes)
{
    public readonly string sourceName = sourceName;
    public readonly byte[] sourceBytes = sourceBytes;
}