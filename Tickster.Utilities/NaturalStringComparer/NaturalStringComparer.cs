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
using System.Collections.Generic;
using System.Globalization;

namespace Tickster.Collections
{
    /// <summary>
    /// High performance, fully managed comparer for peforming so-called natural sort (ie abc1 is sorted before "abc10").
    /// White space is not significant for sorting (ie "abc 1" is equal to "abc1"). For highest performance initialize the
    /// comparer with StringComparison.Ordinal or StringComparison.OrdinalIgnoreCase.
    /// </summary>
    public sealed class NaturalStringComparer : Comparer<string>
    {
        /// <summary>
        /// Represents the different kinds of known tokens
        /// </summary>
        private enum TokenType : byte
        {
            Undefined,
            String,
            Numeric,
            End
        }

        /// <summary>
        ///  The type of comparison to use when comparing non-numeric substrings
        /// </summary>
        private StringComparison comparison;

        /// <summary>
        /// Initializes a new instance of the <see cref="NaturalStringComparer"/> class.
        /// </summary>
        public NaturalStringComparer()
            : this(StringComparison.CurrentCulture)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NaturalStringComparer"/> class.
        /// </summary>
        /// <param name="comparison">The type of comparison to use when comparing non-numeric substrings.</param>
        public NaturalStringComparer(StringComparison comparison)
        {
            this.comparison = comparison;
        }

        public override int Compare(string x, string y)
        {
            if ((object)x == (object)y)
                return 0;

            if (x == null)
                return -1;

            if (y == null)
                return 1;

            char[] xBuf = new char[x.Length];
            char[] yBuf = new char[y.Length];

            int xSourceOffset = 0,
                ySourceOffset = 0;

            TokenType xTokenType = TokenType.Undefined,
                      yTokenType = TokenType.Undefined;

            int xTokenLength = 0,
                yTokenLength = 0;

            do
            {
                ReadNextToken(x, ref xSourceOffset, xBuf, ref xTokenLength, ref xTokenType);
                ReadNextToken(y, ref ySourceOffset, yBuf, ref yTokenLength, ref yTokenType);

                if (xTokenType == TokenType.End || yTokenType == TokenType.End)
                {
                    if (xTokenType == TokenType.End && yTokenType == TokenType.End)
                        return 0;
                    else if (xTokenType == TokenType.End)
                        return -1;
                    else
                        return 1;
                }

                if (xTokenType == TokenType.Numeric && yTokenType == TokenType.Numeric)
                {
                    // Since we've stripped leading zeroes in ParseNextToken we can compare
                    // numeric tokens by lengths first (the shorter a token is the less numerical 
                    // value it represents
                    if (xTokenLength > yTokenLength)
                        return 1;
                    else if (xTokenLength < yTokenLength)
                        return -1;

                    for (int i = 0; i < xTokenLength; i++)
                    {
                        int r = xBuf[i].CompareTo(yBuf[i]);

                        if (r != 0)
                            return r;
                    }
                }
                else
                {
                    int r = 0;

                    // In the case of the simpler ordinal comparison types we'll do the comparison ourselves 
                    // char by char in order to avoid allocating a new string for the buffer contents. This greatly
                    // improved performances
                    if (this.comparison == StringComparison.Ordinal)
                    {
                        r = CompareOrdinal(xBuf, xTokenLength, yBuf, yTokenLength, this.comparison == StringComparison.OrdinalIgnoreCase);
                    }
                    else if (this.comparison == StringComparison.OrdinalIgnoreCase)
                    {
                        r = CompareOrdinal(xBuf, xTokenLength, yBuf, yTokenLength, this.comparison == StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        // Note that when using Substring with offset 0 and a length the same as the string in question we'll get 
                        // the same string instance so it's no use trying to optimize that any further.
                        r = string.Compare(new string(xBuf, 0, xTokenLength), new string(yBuf, 0, yTokenLength));
                    }

                    if (r != 0)
                        return r;
                }
            }
            while (xTokenType != TokenType.End && yTokenType != TokenType.End);

            return 0;
        }

        private static int CompareOrdinal(char[] xBuf, int xTokenLength, char[] yBuf, int yTokenLength, bool ignoreCase)
        {
            int minLength = xTokenLength < yTokenLength ? xTokenLength : yTokenLength;

            int r = 0;

            for (int i = 0; i < minLength; i++)
            {
                r = xBuf[i].CompareTo(yBuf[i]);

                if (r != 0)
                    return r;
            }

            if (xTokenLength < yTokenLength)
                return -1;
            else if (xTokenLength > yTokenLength)
                return 1;

            return 0;
        }

        private static int CompareOrdinalIgnoreCase(char[] xBuf, int xTokenLength, char[] yBuf, int yTokenLength)
        {
            TextInfo ti = CultureInfo.InvariantCulture.TextInfo;
            int minLength = xTokenLength < yTokenLength ? xTokenLength : yTokenLength;

            int r = 0;

            for (int i = 0; i < minLength; i++)
            {
                r = ti.ToLower(xBuf[i]).CompareTo(yBuf[i]);

                if (r != 0)
                    return r;
            }

            if (xTokenLength < yTokenLength)
                return -1;
            else if (xTokenLength > yTokenLength)
                return 1;

            return 0;
        }

        private static void ReadNextToken(string source, ref int sourceIndex, char[] destination, ref int destinationLength, ref TokenType tokenType)
        {
            destinationLength = 0;

            if (sourceIndex >= source.Length)
            {
                tokenType = TokenType.End;
                return;
            }

            char c = source[sourceIndex];

            if (c >= '0' && c <= '9')
            {
                tokenType = TokenType.Numeric;

                while (c >= '0' && c <= '9')
                {
                    if (destinationLength == 1 && destination[0] == '0')
                        destinationLength--;

                    destination[destinationLength] = c;
                    destinationLength++;

                    sourceIndex++;

                    if (sourceIndex >= source.Length)
                        break;

                    c = source[sourceIndex];
                }
            }
            else
            {
                tokenType = TokenType.String;

                while (c < '0' || c > '9')
                {
                    if (c != ' ')
                    {
                        destination[destinationLength] = c;
                        destinationLength++;
                    }

                    sourceIndex++;

                    if (sourceIndex >= source.Length)
                        break;

                    c = source[sourceIndex];
                }

                // The buffer is filled with one or more spaces only, this can happen for strings such as "foo 1  bar"
                // we'll just skip ahead and read the next token.
                if (destinationLength == 0)
                    ReadNextToken(source, ref sourceIndex, destination, ref destinationLength, ref tokenType);
            }
        }
    }
}