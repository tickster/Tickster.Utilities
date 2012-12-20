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
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.UI;

namespace Tickster.Utils
{
    /// <summary>
    /// Generates exception signatures useful for grouping exceptions together. 
    /// The signatures are also useful when you wan't to give users an identifier or 
    /// "error-code" to use when they contact customer service. Use the ToString method
    /// to produce small friendly signatures (example: 7815696e). Produces upper case
    /// codes by default (controlled by the UseUpperCaseSignature property).
    /// </summary>
    public sealed class ExceptionSignatureBuilder : IDisposable
    {
        private StringBuilder _signatureBuffer;

        private bool _disposed;

        /// <summary>
        /// Used by the RemoveTextWithinStopChars method to trim unwanted text within exception messages.
        /// </summary>
        private static Dictionary<char, char> stopChars = new Dictionary<char, char> 
        {
            { '(', ')' },
            { '{', '}' },
            { '[', ']' },
            { '"', '"' },
            { '\'', '\'' }
        };

        private static char[] stopCharLookupChars = new char[] { '(', '{', '[', '"', '\'' };

        /// <summary>
        /// Gets or sets a value indicating whether to do "intelligent" processing of 
        /// exception messages in order to allow for some dynamic data in the messages 
        /// between two exception while still being able to produce the same signature.
        /// Also attempts to use errocodes instead of exception messages for exception types
        /// that provide such (eg SocketException).
        /// Default is true.
        /// </summary>
        [DefaultValue(true)]
        public bool PreprocessExceptionMessages { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether signatures should be upper case. Default is true.
        /// </summary>
        [DefaultValue(true)]
        public bool UseUppercaseSignature { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to use the complete stack trace when
        /// generating the signature. If set to false the builder only looks at the
        /// point of origin for each exception. Defaults to true.
        /// </summary>
        /// <value>
        ///     <c>true</c> if builder includes complete stack trace; otherwise, <c>false</c>.
        /// </value>
        [DefaultValue(true)]
        public bool IncludeCompleteStackTrace { get; set; }

        /// <summary>
        /// Initializes a new instance of the ExceptionSignatureBuilder class.
        /// </summary>
        public ExceptionSignatureBuilder()
        {
            PreprocessExceptionMessages = true;
            UseUppercaseSignature = true;
            IncludeCompleteStackTrace = true;

            _signatureBuffer = new StringBuilder();
        }

        /// <summary>
        /// Add one exception to the builder and traverse all its inner exceptions
        /// </summary>
        /// <param name="exception">The exception to add</param>
        public void AddException(Exception exception)
        {
            AddException(exception, true);
        }

        /// <summary>
        /// Add one exception to the builder optionally traversing all of its inner exceptions
        /// </summary>
        /// <param name="exception">The exception to add</param>
        /// <param name="traverseInnerException">Whether or not to include all inner exceptions in the signature</param>
        public void AddException(Exception exception, bool traverseInnerException)
        {
            if (exception == null)
                throw new ArgumentNullException("exception");

            AssertNotDisposed();

            while (exception != null)
            {
                Type exceptionType = exception.GetType();

                _signatureBuffer.Append(exceptionType.Name);
                _signatureBuffer.Append(": ");

                AppendExceptionMessage(exception);

                _signatureBuffer.AppendLine();

                if (IncludeCompleteStackTrace)
                    AppendStackTrace(exception);
                else
                    AppendMethodInfo(exception.TargetSite);

                _signatureBuffer.AppendLine();

                if (!traverseInnerException)
                    break;

                exception = exception.InnerException;
            }
        }

        private void AppendStackTrace(Exception exception)
        {
            if (exception == null)
                throw new ArgumentNullException("exception");

            var st = new StackTrace(exception, false);

            StackFrame[] frames = st.GetFrames();

            if (frames != null && frames.Length > 0)
            {
                foreach (StackFrame frame in frames)
                {
                    if (frame != null)
                    {
                        MethodBase mb = frame.GetMethod();

                        if (mb != null)
                            AppendMethodInfo(mb);
                    }
                }
            }
        }

        private string AppendMethodInfo(MethodBase mb)
        {
            if (mb == null)
                throw new ArgumentNullException("mb");

            if (mb.DeclaringType != null)
                _signatureBuffer.Append(mb.DeclaringType.FullName);
            else
                _signatureBuffer.Append("<unknown type>");

            _signatureBuffer.Append('.');
            _signatureBuffer.Append(mb.Name);

            if (mb.IsGenericMethod)
            {
                _signatureBuffer.Append('<');

                Type[] genericArgumentTypes = mb.GetGenericArguments();
                string[] genericArgumentNames = new string[genericArgumentTypes.Length];

                for (int i = 0; i < genericArgumentTypes.Length; i++)
                    genericArgumentNames[i] = genericArgumentTypes[i].Name;

                _signatureBuffer.Append(string.Join(",", genericArgumentNames));

                _signatureBuffer.Append('>');
            }

            _signatureBuffer.Append('(');

            _signatureBuffer.Append(string.Join(",", GetParameterStrings(mb.GetParameters())));

            _signatureBuffer.Append(')');

            return _signatureBuffer.ToString();
        }

        private string[] GetParameterStrings(ParameterInfo[] parameterInfo)
        {
            if (parameterInfo == null)
                throw new ArgumentNullException("parameterInfo");

            var ps = new string[parameterInfo.Length];

            ParameterInfo pi;

            for (int i = 0; i < parameterInfo.Length; i++)
            {
                pi = parameterInfo[i];

                if (pi.ParameterType != null)
                    ps[i] = pi.ParameterType.Name + " " + pi.Name;
                else
                    ps[i] = "<unknown type> " + pi.Name;
            }

            return ps;
        }

        private void AssertNotDisposed()
        {
            if (_disposed)
                throw new InvalidOperationException("Cannot use builder after it's been disposed");
        }

        /// <summary>
        /// Removes all signature data from the builder
        /// </summary>
        public void Clear()
        {
            _signatureBuffer = new StringBuilder();
        }

        private void AppendExceptionMessage(Exception exception)
        {
            if (exception == null)
                throw new ArgumentNullException("exception");

            if (PreprocessExceptionMessages)
            {
                var se = exception as SocketException;

                if (se != null)
                {
                    AppendErrorCode(se.ErrorCode);
                    return;
                }

                var w32e = exception as Win32Exception;

                if (w32e != null)
                {
                    AppendErrorCode(w32e.ErrorCode);
                    return;
                }

                // ViewState exceptions tack on all debug data in their messages instead of 
                // properly attaching them to the Data property. Thus we need to do some 
                // preprocessing and remove everything after the first line break (if any).
                if (exception is ViewStateException)
                {
                    int p = exception.Message.IndexOf("\r\n");

                    if (p != -1)
                    {
                        // TODO: when/if we support removal of key-value pairs from 
                        // multi line exception messages we don't need this special 
                        // case plus we could get rid of the System.Web dependency.
                        _signatureBuffer.Append(exception.Message.Substring(0, p));
                        return;
                    }
                }
            }

            if (exception.Message == null)
                return;

            if (PreprocessExceptionMessages)
                AppendPreprocessedMessage(_signatureBuffer, exception.Message);
            else
                _signatureBuffer.Append(exception.Message);
        }

        private void AppendErrorCode(int errorCode)
        {
            _signatureBuffer.Append("ErrorCode: ");
            _signatureBuffer.Append(errorCode.ToString(CultureInfo.InvariantCulture));
        }

        internal static void AppendPreprocessedMessage(StringBuilder sb, string message)
        {
            if (message == null)
                throw new ArgumentNullException("message");

            if (message.Length == 0)
                return;

            int length = message.IndexOf(':');

            if (length == -1)
                sb.Append(RemoveTextWithinStopChars(message));
            else
                sb.Append(RemoveTextWithinStopChars(message.Substring(0, length)));
        }

        internal static string RemoveTextWithinStopChars(string value)
        {
            if (value == null)
                throw new ArgumentNullException("value");

            if (value.Length == 0)
                return value;

            if (value.IndexOfAny(stopCharLookupChars) == -1)
                return value;

            char[] chars = value.ToCharArray();

            // Read and write positions
            int rp = 0;
            int wp = 0;

            while (rp < chars.Length)
            {
                char c = chars[rp];
                char seekChar;

                if (stopChars.TryGetValue(c, out seekChar))
                {
                    if (c == seekChar)
                    {
                        // No balancing required; eg single and double quotes
                        do
                        {
                            rp++;
                        }
                        while (rp < chars.Length && chars[rp] != seekChar);
                    }
                    else
                    {
                        // Balanced stopchars eg parenthesis
                        int balanceCounter = 0;

                        while (rp < chars.Length)
                        {
                            if (chars[rp] == c)
                                balanceCounter++;
                            else if (chars[rp] == seekChar)
                                balanceCounter--;

                            if (balanceCounter == 0)
                                break;

                            rp++;
                        }
                    }
                }
                else
                {
                    if (wp != rp)
                        chars[wp] = c;

                    wp++;
                }

                rp++;
            }

            return new string(chars, 0, wp);
        }

        /// <summary>
        /// Returns the signature hash (128bit md5)
        /// </summary>
        public byte[] GetSignatureBytes()
        {
            AssertNotDisposed();

            byte[] buf = Encoding.ASCII.GetBytes(_signatureBuffer.ToString());
            var hashProvider = MD5.Create();

            return hashProvider.ComputeHash(buf);
        }

        /// <summary>
        /// Returns a 32 characters long hex digest of the signature bytes
        /// </summary>
        public string ToSignatureHashDigest()
        {
            var digest = HexTranslator.ToHex(GetSignatureBytes());

            if (UseUppercaseSignature)
                return digest.ToUpper(CultureInfo.InvariantCulture);

            return digest;
        }

        /// <summary>
        /// Computes a 8-character long exception signature taken from the beginning
        /// of the exception signature hash digest (ToSignatureHashDigest)
        /// </summary>
        public string ToSignatureString()
        {
            return ToSignatureHashDigest().Substring(0, 8);
        }

        /// <summary>
        /// Computes a 8-character long exception signature taken from the beginning
        /// of the exception signature hash digest (ToSignatureHashDigest)
        /// </summary>
        public override string ToString()
        {
            return ToSignatureString();
        }

        public void Dispose()
        {
            if (_signatureBuffer != null)
                _signatureBuffer = null;

            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}