// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text;

namespace Crateris.Core.Net;

/// <summary>
/// Writes a binary message into a buffer that's kept and reused, so writing
/// doesn't allocate once the buffer has grown to the messages' size.
/// Integers are LEB128 variable-length (7 bits a byte); signed ones are
/// zigzag-encoded first, so small values either side of zero are one byte.
/// </summary>
public sealed class WireWriter
{
    private byte[] _buffer;

    public WireWriter(int capacity = 1024) => _buffer = new byte[capacity];

    /// <summary>Bytes written since <see cref="Clear"/>.</summary>
    public int Length { get; private set; }

    /// <summary>What has been written.</summary>
    public ReadOnlySpan<byte> Written => _buffer.AsSpan(0, Length);

    public void Clear() => Length = 0;

    public void Byte(byte value)
    {
        Ensure(1);
        _buffer[Length++] = value;
    }

    public void UInt(ulong value)
    {
        Ensure(10);
        while (value >= 0x80)
        {
            _buffer[Length++] = (byte)(value | 0x80);
            value >>= 7;
        }
        _buffer[Length++] = (byte)value;
    }

    public void Int(long value) => UInt((ulong)((value << 1) ^ (value >> 63)));

    /// <summary>A string as its UTF-8 length plus one, then its bytes; 0 for null.</summary>
    public void String(string? value)
    {
        if (value is null)
        {
            UInt(0);
            return;
        }
        int count = Encoding.UTF8.GetByteCount(value);
        UInt((ulong)count + 1);
        Ensure(count);
        Length += Encoding.UTF8.GetBytes(value, _buffer.AsSpan(Length));
    }

    private void Ensure(int more)
    {
        if (Length + more > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, Length + more));
    }
}

/// <summary>
/// Reads what <see cref="WireWriter"/> wrote. A message that ends early, or
/// holds a value too big for its field, is refused with
/// <see cref="InvalidDataException"/>: messages come from the network.
/// </summary>
public ref struct WireReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _at;

    public WireReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _at = 0;
    }

    /// <summary>Whether every byte has been read.</summary>
    public bool AtEnd => _at == _data.Length;

    public byte Byte()
    {
        if (_at >= _data.Length)
            throw new InvalidDataException("The message ends early.");
        return _data[_at++];
    }

    public ulong UInt()
    {
        ulong value = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            byte b = Byte();
            value |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80)
                return value;
        }
        throw new InvalidDataException("A number in the message is too long.");
    }

    public long Int()
    {
        ulong v = UInt();
        return (long)(v >> 1) ^ -(long)(v & 1);
    }

    public string? String()
    {
        ulong n = UInt();
        if (n == 0)
            return null;
        if (n - 1 > (ulong)(_data.Length - _at))
            throw new InvalidDataException("A text in the message runs past its end.");
        int count = (int)(n - 1);
        string s = Encoding.UTF8.GetString(_data.Slice(_at, count));
        _at += count;
        return s;
    }
}
