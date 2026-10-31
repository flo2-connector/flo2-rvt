// IFC GlobalIds: the 22-character, base-64 form IFC writes a 128-bit GUID in.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.
//
// FromGuid is the same conversion as ConvertToIFCGuid in Autodesk's open-source IFC
// exporter for Revit (github.com/Autodesk/revit-ifc, branch Release_24.x.x, commit
// 887c3d3e654f5a61b0ee54c5504ffafbbe52b827, Source/Revit.IFC.Export/Utility/GUIDUtil.cs),
// written here from the IFC specification's own description (the GUID's 128 bits, most
// significant first, as one 2-bit digit and twenty-one 6-bit digits of the alphabet below),
// so that the GlobalId flo2-rvt computes for an element is the one Revit's exporter writes.
// IsValid is the exporter's IsValidIFCGUID rule: 22 characters of the alphabet, the first
// one of 0 to 3.

using System;
using System.Text;

namespace Flo2Rvt.Addin.Edit.Logic
{
    public static class IfcGuid
    {
        public const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";
        public const int Length = 22;

        /// <summary>True when the text is a GlobalId Revit's exporter would accept (and so could have written).</summary>
        public static bool IsValid(string text)
        {
            if (text == null || text.Length != Length) return false;
            if (text[0] < '0' || text[0] > '3') return false;
            foreach (char c in text)
            {
                if (Alphabet.IndexOf(c) < 0) return false;
            }
            return true;
        }

        /// <summary>The GUID's bytes most significant first, as its canonical text prints them.</summary>
        private static byte[] BigEndian(Guid guid)
        {
            byte[] le = guid.ToByteArray();
            // Guid.ToByteArray stores the first three fields little-endian.
            return new[]
            {
                le[3], le[2], le[1], le[0],
                le[5], le[4],
                le[7], le[6],
                le[8], le[9], le[10], le[11], le[12], le[13], le[14], le[15],
            };
        }

        public static string FromGuid(Guid guid)
        {
            byte[] b = BigEndian(guid);
            var sb = new StringBuilder(Length);
            // The first byte (0..255) is two digits, the first of them 0..3; then five groups of
            // three bytes, four digits each: 2 + 20 = 22.
            AppendDigits(sb, b[0], 2);
            for (int i = 1; i < 16; i += 3)
            {
                ulong n = ((ulong)b[i] << 16) | ((ulong)b[i + 1] << 8) | b[i + 2];
                AppendDigits(sb, n, 4);
            }
            return sb.ToString();
        }

        private static void AppendDigits(StringBuilder sb, ulong value, int digits)
        {
            var buf = new char[digits];
            for (int d = digits - 1; d >= 0; d--)
            {
                buf[d] = Alphabet[(int)(value % 64)];
                value /= 64;
            }
            sb.Append(buf);
        }

        /// <summary>The GUID a valid GlobalId stands for. Throws FormatException otherwise.</summary>
        public static Guid ToGuid(string text)
        {
            if (!IsValid(text)) throw new FormatException("not an IFC GlobalId");
            var b = new byte[16];
            b[0] = (byte)Digits(text, 0, 2);
            int at = 2;
            for (int i = 1; i < 16; i += 3)
            {
                ulong n = Digits(text, at, 4);
                b[i] = (byte)(n >> 16);
                b[i + 1] = (byte)(n >> 8);
                b[i + 2] = (byte)n;
                at += 4;
            }
            var le = new[]
            {
                b[3], b[2], b[1], b[0],
                b[5], b[4],
                b[7], b[6],
                b[8], b[9], b[10], b[11], b[12], b[13], b[14], b[15],
            };
            return new Guid(le);
        }

        private static ulong Digits(string text, int start, int count)
        {
            ulong n = 0;
            for (int i = 0; i < count; i++) n = n * 64 + (ulong)Alphabet.IndexOf(text[start + i]);
            return n;
        }
    }
}
