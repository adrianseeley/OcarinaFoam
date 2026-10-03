using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Streaming tokenizer for this reader's restricted ASCII grammar: skip whitespace
    // and both comment forms, separate punctuation, and copy a token into a reusable
    // buffer. Quoted strings are simple; this is not a complete OpenFOAM preprocessor.
    public static bool Next(Tokens tokens, bool allowEnd = false)
    {
        StreamReader reader = tokens.Reader;
        int c;
        while (true)
        {
            c = reader.Read();
            if (c < 0)
            {
                if (allowEnd)
                {
                    return false;
                }
                throw new EndOfStreamException();
            }
            if (char.IsWhiteSpace((char)c))
            {
                continue;
            }
            if (c == '/' && reader.Peek() == '/')
            {
                do
                {
                    c = reader.Read();
                }
                while (c >= 0 && c != '\n');
                continue;
            }
            if (c == '/' && reader.Peek() == '*')
            {
                reader.Read();
                int previous = 0;
                while (true)
                {
                    c = reader.Read();
                    if (c < 0)
                    {
                        throw new EndOfStreamException();
                    }
                    if (previous == '*' && c == '/')
                    {
                        break;
                    }
                    previous = c;
                }
                continue;
            }
            break;
        }
        tokens.Length = 0;
        if (c == '"')
        {
            while (true)
            {
                c = reader.Read();
                if (c < 0)
                {
                    throw new EndOfStreamException();
                }
                if (c == '"')
                {
                    break;
                }
                Append(tokens, (char)c);
            }
            return true;
        }
        Append(tokens, (char)c);
        if (Delimiter(c))
        {
            return true;
        }
        while (true)
        {
            c = reader.Peek();
            if (c < 0 || char.IsWhiteSpace((char)c) || Delimiter(c) || c == '/')
            {
                break;
            }
            Append(tokens, (char)reader.Read());
        }
        return true;
    }
}
