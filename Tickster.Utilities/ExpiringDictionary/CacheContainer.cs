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
using System.Linq;
using System.Text;
using System.Threading;

namespace Tickster.Caching
{
    /// <summary>
    /// A container used exclusively in the ExpiringDictionary class as a way of centralizing all
    /// neccessary functionality and properties associated with an ExpiringDictionary entry.
    /// The container is semi-immutable in the sense that the key and item cannot be changed
    /// after it's created. The expiration time and mode (Absolute or Sliding) of the container 
    /// is however not immutable and may be changed.
    /// </summary>
    /// <typeparam name="TKey">The type of the key in the container</typeparam>
    /// <typeparam name="TValue">The type of the value in the container</typeparam>
    internal class CacheContainer<TKey, TValue>
    {
        /// <summary>
        /// Gets a value indicating whether or not the container is "Alive", ie whether or not
        /// it has expired.
        /// </summary>
        public bool IsAlive
        {
            get
            {
                return ExpiresIn > TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Gets a value indicating when the container expires (in UTC)
        /// </summary>
        public DateTime ExpiresUtc
        {
            get
            {
                if (_absoluteExpirationUtc.HasValue)
                    return _absoluteExpirationUtc.Value;
                else if (_slidingExpiration.HasValue)
                    return _lastTouch.Add(_slidingExpiration.Value);
                else
                    throw new ArgumentException("Neither absolute expiration or sliding expiration was set");
            }
        }

        /// <summary>
        /// Gets a value indicating the time until the container expires. May return negative values if
        /// the container already has expired.
        /// </summary>
        public TimeSpan ExpiresIn { get { return ExpiresUtc - DateTime.UtcNow; } }

        private Nullable<DateTime> _absoluteExpirationUtc;
        private Nullable<TimeSpan> _slidingExpiration;

        /// <summary>
        /// A reference to the last time this container was accessed
        /// </summary>
        private DateTime _lastTouch;

        public TKey Key { get; private set; }

        private TValue _item;

        public TValue Item
        {
            get
            {
                Touch();
                return _item;
            }
        }

        /// <summary>
        /// Gets or sets the callback that will be invoked when the container
        /// is removed from the dictionary.
        /// </summary>
        public Action<TKey, TValue, RemoveReason> RemoveCallback { get; set; }

        public CacheContainer(TKey key, TValue item, TimeSpan expiration, bool sliding)
        {
            Key = key;
            _item = item;
            SetExpiration(expiration, sliding);
        }

        /// <summary>
        /// Explicitly sets the expiration time of this container.
        /// </summary>
        /// <param name="expiration">How long the container should be kept alive</param>
        /// <param name="sliding">Indicates whether or not the container should use sliding expiration</param>
        public void SetExpiration(TimeSpan expiration, bool sliding)
        {
            if (sliding)
            {
                _absoluteExpirationUtc = null;
                _slidingExpiration = expiration;
            }
            else
            {
                _slidingExpiration = null;
                _absoluteExpirationUtc = DateTime.UtcNow.Add(expiration);
            }

            Touch();
        }

        /// <summary>
        /// Touches the CacheContainer and returns a timespan indicating the new lifetime of the item
        /// </summary>
        public TimeSpan Touch()
        {
            _lastTouch = DateTime.UtcNow;
            return ExpiresIn;
        }

        /// <summary>
        /// Called by the ExpiringDictionary when the item has been removed from the dictionary
        /// </summary>
        /// <param name="reason">The reason of the removal</param>
        internal void OnRemoved(RemoveReason reason)
        {
            if (RemoveCallback != null)
                RemoveCallback(Key, Item, reason);
        }
    }
}