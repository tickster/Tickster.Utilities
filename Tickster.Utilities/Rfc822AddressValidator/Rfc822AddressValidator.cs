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

/*
 *  Notes:
 *  
 *      All methods currently performs checks for nullity, this is highly redundant and not 
 *      necessary as all methods except for Validate(..) is private. The reason for this
 *      is twofold, I want to be sure that the lib is stable and it might be valuable to open 
 *      up some of the methods for external calls later on.
 *      
 *      Paul Warren has a great Mail::RFC822::Address based email validation tool at
 *      http://www.mythic-beasts.com/~pdw/cgi-bin/emailvalidate
 *      
 *      This class currently only support the addr-spec path of the address token and currently
 *      I have no plans of adding support for it since I've never seen anyone use it.
 *      
 *      One could certainly argue that this is overkill for validating email addresses in 
 *      production websites/applications and that might be but it's pretty darn fast and
 *      even if you don't trust it or if you feel it's too bloated for use in your app it
 *      could prove useful for debugging your current validation methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace Tickster.Validation
{
    /// <summary>
    ///     A high-performance email address validator that validates most email address formats specified 
    ///     in RFC 822. Outperforms several non-trivial (interpreted) regular expression based validation methods.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The Validation methods are thread safe in the sense that they do not share any
    ///         validation state but the configuration properties of the validator may affect
    ///         the validator in a way that causes invalid addresses to be considered valid (and 
    ///         vice-versa) if they are modified while an address is being validated.
    ///     </para>
    ///     <para>
    ///         Instantiation is cheap after first init (when the static constructor has completed)
    ///         so there's really no need share validators across threads but as long as you don't
    ///         touch the config properties after instantiation you should be safe.
    ///     </para>
    ///     <para>
    ///         This class doesn't currently support the full address token (mailbox/group). It only
    ///         supports the addr-spec (local-part "@" domain) and has no support for specifying routes
    ///     </para>
    /// </remarks>
    public sealed class Rfc822AddressValidator
    {
        private static HashSet<char> asciiChars;
        private static HashSet<char> quotedStringTextChars;
        private static HashSet<char> specialChars;

        /// <summary>
        /// Gets or sets a value indicating whether a tld is required (foo@bar.com).
        /// Has no effect (always assumed to be false) if Rfc822Strict is true.
        /// </summary>
        public bool RequireTopLevelDomain { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the tld should be checked verified against a list of known top level domains.
        /// Has no effect (always assumed to be false) if Rfc822Strict is true or RequireTld is false.
        /// </summary>
        public bool RequireKnownTopLevelDomain { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to allow domain literals (ie foo@[127.0.0.1]).
        /// Has no effect (always assumed to be true) if Rfc822Strict is true.
        /// </summary>
        public bool AllowDomainLiterals { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to enable strict Rfc822 checking. Defaults to false.
        /// Use of this mode is only recommended for debugging. Allows domain literals
        /// and does not require a top level domain. With this mode set to false
        /// the validator will lean towards a more "sane" validation (which ironically
        /// is more strict) that's recommended for production use.
        /// </summary>
        public bool Rfc822Strict { get; set; }

        private static HashSet<string> _validTopLevelDomains;

        /// <summary>
        /// Gets a case-insensitive hash set containing all valid top level domains (without leading dot).
        /// Useful if you use a special TLD on you local network or if a new one is added in the future.
        /// Please note that if you change this set you will be altering the behavior of all Rfc822AddressValidator
        /// instances (current and future) that has the RequireKnownTopLevelDomain set to true.
        /// </summary>
        public static HashSet<string> ValidTopLevelDomains
        {
            get { return _validTopLevelDomains; }
        }

        /// <summary>
        /// Initializes static members of the Rfc822AddressValidator class.
        /// </summary>
        /// <remarks>Static constructors in .net are guaranteed to be thread safe and only runs once</remarks>
        static Rfc822AddressValidator()
        {
            InitChars();
            InitValidTopLevelDomains();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Rfc822AddressValidator"/> class.
        /// </summary>
        public Rfc822AddressValidator()
        {
            RequireTopLevelDomain = true;
            RequireKnownTopLevelDomain = false;
            Rfc822Strict = false;
            AllowDomainLiterals = false;
        }

        private static void InitChars()
        {
            asciiChars = new HashSet<char>();
            quotedStringTextChars = new HashSet<char>();

            for (int i = 0; i <= 127; i++)
            {
                asciiChars.Add((char)i);
                quotedStringTextChars.Add((char)i);
            }

            quotedStringTextChars.Remove('"');
            quotedStringTextChars.Remove('\\');
            quotedStringTextChars.Remove((char)13); // CR

            specialChars = new HashSet<char> { '(', ')', '<', '>', '@', ',', ';', ':', '\\', '"', '.', '[', ']' };
        }

        private static void InitValidTopLevelDomains()
        {
            _validTopLevelDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ac", "ad", "ae", "af", "ag", "ai", "al", "am", "an", "ao", "aq", "ar", 
                "as", "at", "au", "aw", "ax", "az", "ba", "bb", "bd", "be", "bf", "bg", 
                "bh", "bi", "bj", "bm", "bn", "bo", "br", "bs", "bt", "bv", "bw", "by", 
                "bz", "ca", "cc", "cd", "zr", "cf", "cg", "ch", "ci", "ck", "cl", "cm", 
                "cn", "co", "cr", "cu", "cv", "cx", "cy", "cz", "de", "dj", "dk", "dm", 
                "do", "dz", "ec", "ee", "eg", "eh", "er", "es", "et", "eu", "fi", "fj", 
                "fk", "fm", "fo", "fr", "ga", "gb", "uk", "gd", "ge", "gf", "gg", "gh", 
                "gi", "gl", "gm", "gn", "gp", "gq", "gr", "gs", "gt", "gu", "gw", "gy", 
                "hk", "hm", "hn", "hr", "ht", "hu", "id", "ie", "il", "im", "in", "io", 
                "iq", "ir", "is", "it", "je", "jm", "jo", "jp", "ke", "kg", "kh", "ki", 
                "km", "kn", "kp", "kr", "kw", "ky", "kz", "la", "lb", "lc", "li", "lk", 
                "lr", "ls", "lt", "lu", "lv", "ly", "ma", "mc", "md", "me", "mg", "mh", 
                "mk", "ml", "mm", "mn", "mo", "mp", "mq", "mr", "ms", "mt", "mu", "mv", 
                "mw", "mx", "my", "mz", "na", "nc", "ne", "nf", "ng", "ni", "nl", "no", 
                "np", "nr", "nu", "nz", "om", "pa", "pe", "pf", "pg", "ph", "pk", "pl", 
                "pm", "pn", "pr", "ps", "pt", "pw", "py", "qa", "re", "ro", "rs", "ru", 
                "rw", "sa", "sb", "sc", "sd", "se", "sg", "sh", "si", "sj", "sk", "sl", 
                "sm", "sn", "so", "sr", "st", "su", "sv", "sy", "sz", "tc", "td", "tf", 
                "tg", "th", "tj", "tk", "tl", "tp", "tm", "tn", "to", "tp", "tl", "tr", 
                "tt", "tv", "tw", "tz", "ua", "ug", "uk", "gb", "us", "uy", "uz", "va", 
                "vc", "ve", "vg", "vi", "vn", "vu", "wf", "ws", "ye", "yt", "za", "zm", 
                "zw"
            };

            _validTopLevelDomains.Add("biz");
            _validTopLevelDomains.Add("com");
            _validTopLevelDomains.Add("info");
            _validTopLevelDomains.Add("name");
            _validTopLevelDomains.Add("net");
            _validTopLevelDomains.Add("org");
            _validTopLevelDomains.Add("pro");

            _validTopLevelDomains.Add("aero");
            _validTopLevelDomains.Add("asia");
            _validTopLevelDomains.Add("cat");
            _validTopLevelDomains.Add("coop");
            _validTopLevelDomains.Add("edu");
            _validTopLevelDomains.Add("gov");
            _validTopLevelDomains.Add("int");
            _validTopLevelDomains.Add("jobs");
            _validTopLevelDomains.Add("mil");
            _validTopLevelDomains.Add("mobi");
            _validTopLevelDomains.Add("museum");
            _validTopLevelDomains.Add("tel");
            _validTopLevelDomains.Add("travel");
        }

        /// <summary>
        /// RFC822 internet text message (email) address validation.
        /// </summary>
        /// <param name="emailAddress">The address to validate</param>
        public bool Validate(string emailAddress)
        {
            string errmsg;
            return Validate(emailAddress, out errmsg);
        }

        /// <summary>
        /// RFC 822 internet text message (email) address validation.
        /// </summary>
        /// <param name="emailAddress">The address to validate</param>
        /// <param name="errorMessage">A message describing the particular error that caused the validation to fail.</param>
        [SuppressMessage("Microsoft.Design", "CA1021:AvoidOutParameters", MessageId = "1#", Justification = "Out parameter is deemed to be the most appropriate API for this method")]
        public bool Validate(string emailAddress, out string errorMessage)
        {
            if (emailAddress == null)
                throw new ArgumentNullException("emailAddress");

            int length = emailAddress.Length;

            if (length == 0)
            {
                errorMessage = "address empty";
                return false;
            }

            errorMessage = null;

            int atpos = emailAddress.LastIndexOf('@');

            if (atpos == -1)
            {
                errorMessage = "could not find '@' char";
                return false;
            }

            if (atpos == length)
            {
                errorMessage = "empty domain";
                return false;
            }

            string localPart = emailAddress.Substring(0, atpos);
            string domain = emailAddress.Substring(atpos + 1);

            if (!IsValidLocalPart(localPart, out errorMessage))
                return false;

            if (!IsValidDomain(domain, out errorMessage))
                return false;

            return true;
        }

        private static bool IsValidLocalPart(string localPart, out string errorMessage)
        {
            if (localPart == null)
                throw new ArgumentNullException("localPart");

            int length = localPart.Length;

            if (length == 0)
            {
                errorMessage = "local-part was empty";
                return false;
            }

            if (localPart[0] == '.')
            {
                errorMessage = "dot is not allowed as first character in local-part";
                return false;
            }

            if (localPart[length - 1] == '.')
            {
                errorMessage = "trailing dot in local-part";
                return false;
            }

            // Happy path; no quoted-strings
            if (localPart.IndexOf('"') == -1)
            {
                string word;
                int position = 0;
                int start = 0;

                //// While-loop below should be equal to
                ////string[] words = localPart.Split('.');
                ////foreach (var word in words)
                ////{
                ////    if (!IsValidAtom(word, out errorMessage))
                ////        return false;
                ////}
                //// replaced for slight speedup

                while (position < length)
                {
                    position = localPart.IndexOf('.', position);

                    if (position == -1)
                        position = length;

                    word = localPart.Substring(start, position - start);

                    if (!IsValidAtom(word, out errorMessage))
                        return false;

                    position++;
                    start = position;
                }

                errorMessage = null;
                return true;
            }
            else
            {
                /* parses and validates
                 * local-part    =  word *("." word)
                 * word          =  atom / quoted-string
                 * quoted-string = <"> *(qtext/quoted-pair) <">
                 * qtext         =  <any CHAR excepting <">, "\" & CR, and including linear-white-space>
                 * quoted-pair   =  "\" CHAR
                 */

                int position = 0;
                int start = 0;
                char currentChar;

                while (position < length && position != -1)
                {
                    currentChar = localPart[position];

                    if (currentChar != '"')
                    {
                        // Start of atom

                        // All atoms except the first one must be preceeded by a dot
                        if (position != 0 && currentChar != '.')
                        {
                            errorMessage = "no dot between words in local-part";
                            return false;
                        }

                        start = position + 1;
                        position = localPart.IndexOf('.', position + 1);

                        if (position == -1)
                            position = length - 1;

                        var atom = localPart.Substring(start, (position - start) + 1);

                        if (!IsValidAtom(atom, out errorMessage))
                            return false;
                    }
                    else
                    {
                        // Start of quoted string; find the first quote char that's not escaped
                        start = position;

                        while (position < length)
                        {
                            position = localPart.IndexOf('"', position + 1);

                            if (position == -1)
                            {
                                errorMessage = "invalid quoted string";
                                return false;
                            }

                            // Quoted
                            if (localPart[position - 1] == '\\')
                                continue;

                            if (!IsValidQuotedString(localPart.Substring(start, (position - start) + 1), out errorMessage))
                                return false;

                            break;
                        }
                    }

                    position++;
                }

                errorMessage = null;
                return true;
            }
        }

        // word =  atom / quoted-string
        private static bool IsValidWord(string word, out string errorMessage)
        {
            if (word == null)
                throw new ArgumentNullException("word");

            int length = word.Length;

            if (length == 0)
            {
                errorMessage = "empty word";
                return false;
            }

            if (word[0] == '"' && word[length - 1] == '"')
            {
                return IsValidQuotedString(word, out errorMessage);
            }

            if (!IsValidAtom(word, out errorMessage))
                return false;

            errorMessage = null;
            return true;
        }

        // atom =  1*<any CHAR except specials, SPACE and CTLs>
        private static bool IsValidAtom(string atom, out string errorMessage)
        {
            if (atom == null)
                throw new ArgumentNullException("atom");

            char[] chars = atom.ToCharArray();
            var length = chars.Length;
            char c;

            for (int i = 0; i < length; i++)
            {
                c = chars[i];

                if ((c < 33 || c > 126) || specialChars.Contains(c))
                {
                    errorMessage = "invalid character in atom";
                    return false;
                }
            }

            errorMessage = null;
            return true;
        }

        // quoted-string = <"> *(qtext/quoted-pair) <">
        // qtext         =  <any CHAR excepting <">, "\" & CR, and including linear-white-space>
        // quoted-pair   =  "\" CHAR
        private static bool IsValidQuotedString(string qs, out string errorMessage)
        {
            if (qs == null)
                throw new ArgumentNullException("qs");

            int length = qs.Length;

            if (length < 2 || qs[0] != '"' || qs[length - 1] != '"')
            {
                errorMessage = "badly formed quoted string";
                return false;
            }

            // meaning that empty quoted-strings is ok according to RFC quoted string declaration
            if (length == 2)
            {
                errorMessage = null;
                return true;
            }

            if (!IsValidQuotedStringContents(qs.Substring(1, length - 2), out errorMessage))
                return false;

            errorMessage = null;
            return true;
        }

        // quoted-string = <"> *(qtext/quoted-pair) <">
        private static bool IsValidQuotedStringContents(string content, out string errorMessage)
        {
            if (content == null)
                throw new ArgumentNullException("content");

            char[] chars = content.ToCharArray();
            bool escaped = false;

            int length = content.Length;

            if (chars[length - 1] == '\\')
            {
                errorMessage = "escape character trailing quoted string";
                return false;
            }

            for (int i = 0; i < length; i++)
            {
                if (chars[i] == '\\')
                {
                    escaped = true;
                }
                else
                {
                    if (!quotedStringTextChars.Contains(chars[i]) && !escaped)
                    {
                        errorMessage = "unescaped special character in quoted string";
                        return false;
                    }

                    escaped = false;
                }
            }

            errorMessage = null;
            return true;
        }

        private bool IsValidDomain(string domain, out string errorMessage)
        {
            if (domain == null)
                throw new ArgumentNullException("domain");

            int length = domain.Length;

            if (length == 0)
            {
                errorMessage = "domain was empty";
                return false;
            }

            char firstChar = domain[0];
            char lastChar = domain[length - 1];

            if (firstChar == '[' && lastChar == ']')
            {
                if (!IsValidDomainLiteral(domain, out errorMessage))
                    return false;

                errorMessage = null;
                return true;
            }

            if (firstChar == '.')
            {
                errorMessage = "domain starts with a dot";
                return false;
            }

            if (lastChar == '.')
            {
                errorMessage = "trailing dot in domain";
                return false;
            }

            string[] subDomains = domain.Split('.');

            bool rfc822strict = this.Rfc822Strict;

            if (subDomains.Length == 1 && (RequireTopLevelDomain && !rfc822strict))
            {
                errorMessage = "no top level domain specified";
                return false;
            }

            foreach (var subDomain in subDomains)
            {
                if (!IsValidSubDomain(subDomain, out errorMessage, rfc822strict))
                {
                    errorMessage = "invalid subdomain: " + errorMessage;
                    return false;
                }
            }

            if (!rfc822strict && RequireTopLevelDomain)
            {
                string tld = subDomains[subDomains.Length - 1];

                if (!RequireKnownTopLevelDomain)
                {
                    char[] chars = tld.ToCharArray();
                    int tldLength = chars.Length;

                    if (tldLength < 2)
                    {
                        errorMessage = "top level domain to short";
                        return false;
                    }

                    char c;

                    for (int i = 0; i < tldLength; i++)
                    {
                        c = chars[i];

                        // ^[a-zA-Z]+$
                        if (!((c >= 95 && c <= 122) || (c >= 65 && c <= 90)))
                        {
                            errorMessage = "invalid top level domain";
                            return false;
                        }
                    }
                }
                else
                {
                    if (!IsKnownTopLevelDomain(subDomains[subDomains.Length - 1], out errorMessage))
                        return false;
                }
            }

            errorMessage = null;
            return true;
        }

        private bool IsValidDomainLiteral(string domainLiteral, out string errorMessage)
        {
            if (domainLiteral == null)
                throw new ArgumentNullException("domainLiteral");

            if (!Rfc822Strict && !AllowDomainLiterals)
            {
                errorMessage = "domain-literals not allowed";
                return false;
            }

            int length = domainLiteral.Length;

            if (length < 2)
            {
                errorMessage = "domain literal to short";
                return false;
            }

            if (domainLiteral[0] != '[')
            {
                errorMessage = "domain literal did not start with a bracket";
                return false;
            }

            if (domainLiteral[length - 1] != ']')
            {
                errorMessage = "domain literal did not end with a bracket";
                return false;
            }

            if (length == 2)
            {
                if (Rfc822Strict)
                {
                    /* 
                     * RFC 822 defines a domain-literal as
                     * domain-literal =  "[" *(dtext / quoted-pair) "]"
                     * 
                     * which means that [] is a valid domain literal
                     */
                    errorMessage = null;
                    return true;
                }

                errorMessage = "empty domain-literal";
                return false;
            }

            var domainLiteralContent = domainLiteral.Substring(1, length - 2);

            if (Rfc822Strict)
            {
                if (!IsValidQuotedStringContents(domainLiteralContent, out errorMessage))
                    return false;
            }
            else
            {
                // Assume dotted-quad
                IPAddress addr;

                if (!IPAddress.TryParse(domainLiteralContent, out addr))
                {
                    errorMessage = "invalid dotted-quad notation inside domain literal";
                    return false;
                }
            }

            errorMessage = null;
            return true;
        }

        private static bool IsValidSubDomain(string subDomain, out string errorMessage, bool rfc822Strict)
        {
            if (subDomain == null)
                throw new ArgumentNullException("subDomain");

            if (!rfc822Strict)
            {
                /*
                 * RFC822 declares a sub-domain as 
                 * 
                 * sub-domain  =  domain-ref / domain-literal
                 * domain-ref  =  atom
                 * atom        =  1*<any CHAR except specials, SPACE and CTLs>
                 * 
                 * This would allow for characters like "," and "!" which are not
                 * accepted in DNS so therefore we'll tweak this a bit to disallow
                 * characters not accepted in DNS
                 */

                char[] chars = subDomain.ToCharArray();
                int length = chars.Length;
                int c;

                for (int i = 0; i < length; i++)
                {
                    c = chars[i];

                    // ^[a-z0-9\-A-Z]+$
                    if (!(
                          (c >= 97 && c <= 122) || // a-z
                          (c >= 48 && c <= 57) || // 0-9
                          c == 45 || // -
                          (c >= 65 && c <= 90)) // A-Z
                    )
                    {
                        errorMessage = "invalid character in sub-domain";
                        return false;
                    }
                }

                errorMessage = null;
                return true;
            }
            else
            {
                return IsValidAtom(subDomain, out errorMessage);
            }
        }

        private static bool IsKnownTopLevelDomain(string tld, out string errorMessage)
        {
            if (tld == null)
                throw new ArgumentNullException("tld");

            if (!_validTopLevelDomains.Contains(tld))
            {
                errorMessage = "unknown top-level domain";
                return false;
            }

            errorMessage = null;
            return true;
        }
    }
}