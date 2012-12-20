/*
 * Copyright (c) 2012 Markus Olsson, Tickster AB
 * var mail = "developers@tickster.com";
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this 
 * software and associated documentation files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use, copy, modify, merge, publish, 
 * distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING 
 * BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, 
 * DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, 
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System;
using System.Globalization;

namespace Tickster.Utils
{
    public static class HexTranslator
    {
        private static char[] hexAlphabet = new char[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f' };

        /// <summary>
        /// "Hexifies" a byte array
        /// </summary>
        /// <returns>A lower-case hex encoded string representation of the byte array</returns>
        public static string ToHex(byte[] buffer)
        {
            return ToHex(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// "Hexifies" a byte array
        /// </summary>
        /// <returns>A lower-case hex encoded string representation of the byte array</returns>
        public static string ToHex(byte[] buffer, int offset, int length)
        {
            if (buffer == null)
                throw new ArgumentNullException("buffer");

            if (offset < 0)
                throw new ArgumentOutOfRangeException("offset", "Offset cannot be less than 0");

            if (offset > buffer.Length)
                throw new ArgumentOutOfRangeException("offset", "Offset cannot be greater than buffer length");

            if (offset + length > buffer.Length)
                throw new ArgumentException("The offset and length values provided exceed buffer length");

            char[] charbuf = new char[checked(length * 2)];

            int c = -1;

            for (int i = offset; i < length + offset; i++)
            {
                charbuf[c += 1] = hexAlphabet[buffer[i] >> 4];
                charbuf[c += 1] = hexAlphabet[buffer[i] & 0x0F];
            }

            return new string(charbuf);
        }

        /// <summary>
        /// "Hexifies" an integer
        /// </summary>
        /// <returns>A lower-case hex encoded string representation of the integer</returns>
        public static string ToHex(int value)
        {
            return value.ToString("x", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Converts a sequence of hexadecimal pairs into a byte array, ie F0 will yield [ 255 ]
        /// and FF00 will yield [ 255, 0 ]. This is the inverse function of ToHex(byte[] buffer)
        /// </summary>
        public static byte[] ToBytes(string hex)
        {
            if (hex == null)
                throw new ArgumentNullException("hex");

            if (hex.Length == 0 || hex.Length % 2 != 0)
                throw new FormatException("Length of hex string must be greater than 0 and a multiple of 2");

            byte[] buf = new byte[hex.Length / 2];

            int bufOffset;
            int hexOffset;

            for (bufOffset = 0; bufOffset < buf.Length; bufOffset++)
            {
                hexOffset = bufOffset * 2;

                char c1 = hex[hexOffset];
                char c2 = hex[hexOffset + 1];

                if (c1 >= 'A' || c1 <= 'F')
                    c1 = char.ToLowerInvariant(c1);

                if (c2 >= 'A' || c2 <= 'F')
                    c2 = char.ToLowerInvariant(c2);

                int i1 = Array.IndexOf(hexAlphabet, c1);
                int i2 = Array.IndexOf(hexAlphabet, c2);

                if (i1 < 0 || i2 < 0)
                    throw new FormatException("Forbidden characters encountered");

                buf[bufOffset] = (byte)((i1 << 4) + i2);
            }

            return buf;
        }
    }
}