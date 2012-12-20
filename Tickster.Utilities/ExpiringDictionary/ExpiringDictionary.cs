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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace Tickster.Caching
{
    /// <summary>
    /// Represents a generic collection of key/value pairs with scheduled expiration of items
    /// </summary>
    /// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
    /// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
    public sealed class ExpiringDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IDisposable
    {
        /// <summary>
        /// Value indicating whether the dictionary is disposed or not
        /// </summary>
        private bool _disposed;

        // Backing field for DefaultExpirationTime
        private TimeSpan _defaultExpirationTime;

        /// <summary>
        /// Gets or sets the expiration time used for items added without an explicit expiration time
        /// </summary>
        public TimeSpan DefaultExpirationTime
        {
            get
            {
                return _defaultExpirationTime;
            }
            set
            {
                if (value <= TimeSpan.FromSeconds(1))
                    value = TimeSpan.FromSeconds(1);

                _defaultExpirationTime = value;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether to use sliding expiration for items added without an explicit expiration mode.
        /// </summary>
        public bool SlidingExpiration { get; set; }

        /// <summary>
        /// Gets the number of items in the dictionary.
        /// </summary>
        public int Count
        {
            get { return _items.Count; }
        }

        /// <summary>
        /// Gets a value indicating whether the dictionary is read-only.
        /// </summary>
        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a snapshot collection containing the keys in the dictionary
        /// </summary>
        public ICollection<TKey> Keys
        {
            get
            {
                _lock.EnterReadLock();

                try
                {
                    return _items.Keys.ToList();
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Gets a snapshot collection containing the items contained in the collection at the moment
        /// </summary>
        public ICollection<TValue> Values
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _items.Values.Select(cc => cc.Item).ToList();
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Gets or sets the element with the specified key.
        /// </summary>
        /// <exception cref="System.ArgumentNullException">Thrown when the key argument is null</exception>
        /// <exception cref="System.Collection.Generic.KeyNotFoundException">Thrown when no item with the specified key could be found</exception>
        [SuppressMessage("Microsoft.Design", "CA1065:DoNotRaiseExceptionsInUnexpectedLocations", Justification = "Throwing KeyNotFoundException is arguably the expected behaviour since that's the way the BCL generic dictionary works.")]
        public TValue this[TKey key]
        {
            get
            {
                if (key == null)
                    throw new ArgumentNullException("key");

                TValue val;

                if (TryGetValue(key, out val))
                    return val;

                throw new KeyNotFoundException(); // Causes CA1065:DoNotRaiseExceptionsInUnexpectedLocations
            }
            set
            {
                Add(new CacheContainer<TKey, TValue>(key, value, _defaultExpirationTime, SlidingExpiration));
            }
        }

        private bool _frozen;

        /// <summary>
        /// Gets or sets a value indicating whether to allow the dictionary to purge items.
        /// </summary>
        public bool IsFrozen
        {
            get
            {
                return _frozen;
            }
            set
            {
                lock (this)
                {
                    if (value)
                        Freeze();
                    else
                        Unfreeze();
                }
            }
        }

        /// <summary>
        /// Gets or sets a callback for when items are removed from the dictionary
        /// </summary>
        /// <remarks>This fires even if the items are being removed through a call to Clear()</remarks>
        public Action<TKey, TValue, RemoveReason> RemoveCallback { get; set; }

        private Dictionary<TKey, CacheContainer<TKey, TValue>> _items;
        private Timer _purgeTimer;
        private DateTime _nextPurgeUtc;
        private ReaderWriterLockSlim _lock;

        /// <summary>
        /// Initializes a new instance of the ExpiringDictionary class with a specified default expiration time of 5 minutes. The expiration mode is absolute (ie no sliding expiration of items)
        /// </summary>
        public ExpiringDictionary()
        {
            _defaultExpirationTime = TimeSpan.FromMinutes(5);
            SlidingExpiration = false;

            _items = new Dictionary<TKey, CacheContainer<TKey, TValue>>(10);
            _lock = new ReaderWriterLockSlim();
            _purgeTimer = new Timer(PurgeExpiredItems);
            _nextPurgeUtc = DateTime.MaxValue;
        }

        /// <summary>
        /// Initializes a new instance of the ExpiringDictionary class with a specified default expiration time and expiration mode
        /// </summary>
        /// <param name="expirationTime">The expiration time used for items added without an explicit expiration time</param>
        /// <param name="slidingExpiration">Whether or not to use sliding expiration for items added without an explicit expiration mode</param>
        public ExpiringDictionary(TimeSpan expirationTime, bool slidingExpiration)
            : this()
        {
            DefaultExpirationTime = expirationTime;
            SlidingExpiration = slidingExpiration;
        }

        /// <summary>
        /// Initializes a new instance of the ExpiringDictionary class with a specified default expiration time. The expiration mode is absolute (ie no sliding expiration of items)
        /// </summary>
        /// <param name="expirationTime">The expiration time used for items added without an explicit expiration time</param>
        public ExpiringDictionary(TimeSpan expirationTime)
            : this()
        {
            DefaultExpirationTime = expirationTime;
        }

        /// <summary>
        /// Initializes a new instance of the ExpiringDictionary class with a specified default expiration time. The expiration mode is absolute (ie no sliding expiration of items)
        /// </summary>
        /// <param name="expirationMinutes">The expiration time in minutes used for items added without an explicit expiration time</param>
        public ExpiringDictionary(int expirationMinutes)
            : this()
        {
            if (expirationMinutes <= 0)
                throw new ArgumentOutOfRangeException("expirationMinutes", "Expiration must be greater than zero");

            DefaultExpirationTime = TimeSpan.FromMinutes(expirationMinutes);
        }

        /// <summary>
        /// Callback method called by the purgeTimer. Purges expired items in a safe manner
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "Not catching all exceptions would tear down the entire appdomain since this method is run in a separate thread")]
        private void PurgeExpiredItems(object state)
        {
            _lock.EnterWriteLock();
            try
            {
                if (_frozen)
                    return;

                _nextPurgeUtc = DateTime.MaxValue;

                if (_items.Count == 0)
                    return;

                // .ToList prevents delayed execution
                _items
                    .Where(kv => kv.Value.IsAlive == false)
                    .ToList()
                    .ForEach(i => PurgeItem(i.Key, i.Value, RemoveReason.Expired));

                StartPurgeTimer();
            }
            catch (Exception exc)
            {
                /* This will tear down the AppDomain and we don't want that
                 * So we'll try to alert the developer by other means */
                LogException(exc);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Removes one item in a safe manner and fires remove callbacks if available
        /// </summary>
        private void PurgeItem(TKey key, CacheContainer<TKey, TValue> cc, RemoveReason reason)
        {
            PurgeItem(key, cc, reason, true);
        }

        /// <summary>
        /// Removes one item in a safe manner and fires remove callbacks if invokeRemoveCallbacks is true
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "Not catching all exceptions would tear down the entire appdomain since this method is run in a separate thread")]
        private void PurgeItem(TKey key, CacheContainer<TKey, TValue> cc, RemoveReason reason, bool invokeRemoveCallbacks)
        {
            if (invokeRemoveCallbacks)
            {
                try
                {
                    cc.OnRemoved(reason);

                    if (RemoveCallback != null)
                        RemoveCallback(key, cc.Item, reason);
                }
                catch (Exception exc)
                {
                    /* This could potentially tear down the AppDomain and we don't want that
                     * So we'll try to alert the developer by other means */
                    LogException(exc);
                }
            }

            _items.Remove(key);
        }

        private void SetNextPurge(TimeSpan expiry)
        {
            if (IsFrozen)
                return;

            if (expiry < TimeSpan.Zero)
                expiry = TimeSpan.Zero;

            // Trim to nearest second. We don't need more precision than that
            if (expiry.Milliseconds > 0)
                expiry = TimeSpan.FromSeconds(Math.Ceiling(expiry.TotalSeconds));

            // .net timers cannot be configured for longer than ~49 days (4294967294 milliseconds)
            // so if our next purge is greater than that we run once in 49 days and try to figure
            // out when to purge next.
            var max = TimeSpan.FromDays(49);

            if (expiry > max)
                expiry = max;

            _nextPurgeUtc = DateTime.UtcNow.Add(expiry);
            _purgeTimer.Change((long)expiry.TotalMilliseconds, Timeout.Infinite);
        }

        private void StopPurgeTimer()
        {
            _purgeTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _nextPurgeUtc = DateTime.MaxValue;
        }

        private void StartPurgeTimer()
        {
            if (_items.Count > 0)
                SetNextPurge(_items.Min(kv => kv.Value.ExpiresIn));
        }

        /// <summary>
        /// Prevents the dictionary from purging any items
        /// </summary>
        public void Freeze()
        {
            if (_frozen)
                return;

            _lock.EnterWriteLock();
            try
            {
                StopPurgeTimer();
                _frozen = true;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Allow the dictionary to purge items
        /// </summary>
        public void Unfreeze()
        {
            if (!_frozen)
                return;

            _lock.EnterWriteLock();
            try
            {
                _frozen = false;
                StartPurgeTimer();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        [Conditional("DEBUG")]
        private static void LogException(Exception exc)
        {
            if (Debugger.IsAttached)
                Debugger.Break();

            string message = string.Format(CultureInfo.InvariantCulture, "Exception in MemoryCache.PurgeExpiredItems: {0}", exc);
            Debug.WriteLine(message);
        }

        /// <summary>
        /// Removes the item with the supplied key if such an item exists
        /// </summary>
        /// <returns>True if the item was found and removed, false otherwise</returns>
        public bool Remove(TKey key)
        {
            if (_items.ContainsKey(key))
            {
                _lock.EnterWriteLock();
                try
                {
                    CacheContainer<TKey, TValue> cc;
                    if (_items.TryGetValue(key, out cc))
                    {
                        PurgeItem(key, cc, RemoveReason.ExplicitRemove);
                        return true;
                    }
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }

            return false;
        }

        /// <summary>
        /// Insert a cache container into the dictionary. If an item with the same key exists it will be removed
        /// </summary>
        private void Add(CacheContainer<TKey, TValue> cc)
        {
            Add(cc, false);
        }

        private void Add(CacheContainer<TKey, TValue> cc, bool overwriteExisting)
        {
            _lock.EnterWriteLock();
            try
            {
                InternalAdd(cc, overwriteExisting);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        private void InternalAdd(CacheContainer<TKey, TValue> cc, bool overwriteExisting)
        {
            if (cc.ExpiresUtc < _nextPurgeUtc)
                SetNextPurge(cc.ExpiresIn);

            CacheContainer<TKey, TValue> currentContainer;

            if (overwriteExisting && _items.TryGetValue(cc.Key, out currentContainer))
                PurgeItem(cc.Key, currentContainer, RemoveReason.Replaced);

            _items.Add(cc.Key, cc);
        }

        /// <summary>
        /// Add the specified key and value to the dictionary with a specified sliding expiration
        /// </summary>
        /// <remarks>If an item with the same key exists it will be removed</remarks>
        /// <param name="key">The key to add</param>
        /// <param name="value">The value to add</param>
        /// <param name="slidingExpiration">The sliding expiration to use</param>
        /// <param name="removeCallback">A callback that will be invoked when the item is removed from the dictionary</param>
        public void Add(TKey key, TValue value, TimeSpan slidingExpiration, Action<TKey, TValue, RemoveReason> removeCallback)
        {
            Add(GetCacheContainer(key, value, slidingExpiration, true, removeCallback));
        }

        private static CacheContainer<TKey, TValue> GetCacheContainer(TKey key, TValue value, TimeSpan expirationTime, bool sliding, Action<TKey, TValue, RemoveReason> removeCallback)
        {
            var cc = new CacheContainer<TKey, TValue>(key, value, expirationTime, sliding);
            cc.RemoveCallback = removeCallback;

            return cc;
        }

        /// <summary>
        /// Add the specified key and value to the dictionary with a specified sliding expiration
        /// </summary>
        /// <remarks>If an item with the same key exists it will be removed</remarks>
        /// <param name="key">The key to add</param>
        /// <param name="value">The value to add</param>
        /// <param name="slidingExpiration">The sliding expiration to use</param>
        public void Add(TKey key, TValue value, TimeSpan slidingExpiration)
        {
            Add(key, value, slidingExpiration, null);
        }

        /// <summary>
        /// Add the specified key and value to the dictionary with a specified absolute expiration
        /// </summary>
        /// <remarks>If an item with the same key exists it will be removed</remarks>
        /// <param name="key">The key to add</param>
        /// <param name="value">The value to add</param>
        /// <param name="absoluteExpiration">The point in time when this item should be removed</param>
        /// <param name="removeCallback">A callback that will be invoked when the item is removed from the dictionary</param>
        public void Add(TKey key, TValue value, DateTime absoluteExpiration, Action<TKey, TValue, RemoveReason> removeCallback)
        {
            TimeSpan expiry = absoluteExpiration - DateTime.Now;
            Add(GetCacheContainer(key, value, expiry, false, removeCallback));
        }

        /// <summary>
        /// Add the specified key and value to the dictionary with a specified absolute expiration
        /// </summary>
        /// <remarks>If an item with the same key exists it will be removed</remarks>
        /// <param name="key">The key to add</param>
        /// <param name="value">The value to add</param>
        /// <param name="absoluteExpiration">The point in time when this item should be removed</param>
        public void Add(TKey key, TValue value, DateTime absoluteExpiration)
        {
            Add(key, value, absoluteExpiration, null);
        }

        /// <summary>
        /// Add the specified key and value to the dictionary with the default timeout
        /// </summary>
        /// <remarks>If an item with the same key exists it will be removed</remarks>
        /// <param name="key">The key to add</param>
        /// <param name="value">The value to add</param>
        /// <param name="removeCallback">A callback that will be invoked when the item is removed from the dictionary</param>
        public void Add(TKey key, TValue value, Action<TKey, TValue, RemoveReason> removeCallback)
        {
            if (SlidingExpiration)
                Add(key, value, _defaultExpirationTime, removeCallback);
            else
                Add(key, value, DateTime.Now.Add(_defaultExpirationTime), removeCallback);
        }

        /// <summary>
        /// Add the specified key and value to the dictionary with the default expiration time and expiration mode
        /// </summary>
        /// <remarks>If an item with the same key exists it will be removed</remarks>
        /// <param name="key">The key to add</param>
        /// <param name="value">The value to add</param>
        public void Add(TKey key, TValue value)
        {
            Add(key, value, null);
        }

        /// <summary>
        /// Add the specified item to the dictionary with the default expiration time and expiration mode
        /// </summary>
        /// <remarks>If an item with the same key exists it will be removed</remarks>
        public void Add(KeyValuePair<TKey, TValue> item)
        {
            Add(item.Key, item.Value);
        }

        /// <summary>
        /// Updates the expiration time of all items where the supplied predicate matches. Returns the number of items that where updated
        /// </summary>
        public int SetExpiration(Func<TKey, TValue, bool> selector, TimeSpan expirationTime, bool sliding)
        {
            int c = 0;

            _lock.EnterWriteLock();
            try
            {
                foreach (var item in _items.Where(kv => selector(kv.Key, kv.Value.Item)))
                {
                    item.Value.SetExpiration(expirationTime, sliding);
                    c++;
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }

            return c;
        }

        /// <summary>
        /// Updates the expiration time of the item with the supplied key. Returns true if the item exists and false otherwise
        /// </summary>
        public bool SetExpiration(TKey key, TimeSpan expirationTime, bool sliding)
        {
            _lock.EnterWriteLock();
            try
            {
                CacheContainer<TKey, TValue> cc;
                if (_items.TryGetValue(key, out cc))
                {
                    cc.SetExpiration(expirationTime, sliding);
                    return true;
                }
                else
                {
                    return false;
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Touches the entry associated with the supplied key. This has no effect on 
        /// entries with an absolute expiry. Returns true if the item existed and was touched,
        /// false otherwise.
        /// </summary>
        public bool Touch(TKey key)
        {
            CacheContainer<TKey, TValue> cc;

            _lock.EnterReadLock();
            try
            {
                if (_items.TryGetValue(key, out cc))
                {
                    cc.Touch();
                    return true;
                }
            }
            finally
            {
                _lock.ExitReadLock();
            }

            return false;
        }

        /// <summary>
        /// Returns whether or not the dictionary contains the key at the moment
        /// </summary>
        public bool ContainsKey(TKey key)
        {
            _lock.EnterReadLock();
            try
            {
                return _items.ContainsKey(key);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Attempts to retrieve the value associated with the specified key
        /// </summary>
        /// <returns>True if found, false otherwise</returns>
        public bool TryGetValue(TKey key, out TValue value)
        {
            _lock.EnterReadLock();
            try
            {
                CacheContainer<TKey, TValue> cc;

                if (_items.TryGetValue(key, out cc))
                {
                    value = cc.Item;
                    return true;
                }

                value = default(TValue);
                return false;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// In an atomic fassion checks for the existence of the supplied key and adds it if neccessary using the supplied callback
        /// </summary>
        public TValue Get(TKey key, Func<TKey, TValue> notFoundCallback)
        {
            return Get(key, () => notFoundCallback(key));
        }

        /// <summary>
        /// In an atomic fassion checks for the existence of the supplied key and adds it if neccessary using the supplied callback
        /// </summary>
        public TValue Get(TKey key, Func<TValue> notFoundCallback)
        {
            TValue value;

            if (TryGetValue(key, out value))
                return value;

            _lock.EnterWriteLock();
            try
            {
                CacheContainer<TKey, TValue> cc;

                if (_items.TryGetValue(key, out cc))
                    return cc.Item;

                value = notFoundCallback();

                InternalAdd(GetCacheContainer(key, value, DefaultExpirationTime, SlidingExpiration, null), false);

                return value;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Removes all items from the dictionary
        /// </summary>
        public void Clear()
        {
            Clear(true);
        }

        /// <summary>
        /// Removes all items from the dictionary with or without invoking remove callbacks as indicated by invokeRemoveCallbacks parameter
        /// </summary>
        public void Clear(bool invokeRemoveCallbacks)
        {
            _lock.EnterWriteLock();
            try
            {
                _items.ToList().ForEach(item => PurgeItem(item.Key, item.Value, RemoveReason.Cleared, invokeRemoveCallbacks));

                StopPurgeTimer();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Returns whether or not the item exists within the dictionary
        /// </summary>
        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
        {
            _lock.EnterReadLock();
            try
            {
                CacheContainer<TKey, TValue> cc;

                if (_items.TryGetValue(item.Key, out cc) && item.Value.Equals(cc.Item))
                    return true;

                return false;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Copies all items into the supplied array at the given position
        /// </summary>
        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            if (array == null)
                throw new ArgumentNullException("array");

            if (arrayIndex < 0)
                throw new ArgumentOutOfRangeException("arrayIndex", "Index cannot be negative");

            _lock.EnterWriteLock();
            try
            {
                if ((array.Length - arrayIndex) < this.Count)
                    throw new ArgumentException("Will not fit into array with given offset");

                foreach (var kv in this)
                    array[arrayIndex++] = kv;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Removes the item from the dictionary
        /// </summary>
        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
        {
            _lock.EnterWriteLock();
            try
            {
                CacheContainer<TKey, TValue> cc;

                if (_items.TryGetValue(item.Key, out cc) && item.Value.Equals(cc.Item))
                {
                    PurgeItem(item.Key, cc, RemoveReason.ExplicitRemove);
                    return true;
                }
                else
                {
                    return false;
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            _lock.EnterReadLock();
            try
            {
                foreach (var item in _items)
                    yield return new KeyValuePair<TKey, TValue>(item.Key, item.Value.Item);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<KeyValuePair<TKey, TValue>>)this).GetEnumerator();
        }

        public void Dispose()
        {
            if (_disposed == false)
            {
                _lock.Dispose();
                _purgeTimer.Dispose();

                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }
}