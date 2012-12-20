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
using System.Threading;

namespace Tickster.Threading
{
    public class SerializedResource<TKey, TValue> : IDisposable where TValue : class
    {
        /// <summary>
        /// Gets the key of this resource
        /// </summary>
        public TKey Key { get; private set; }

        private Func<TKey, TValue> _factory;
        private TValue _value;

        /// <summary>
        /// Gets the value associated with this resource
        /// </summary>
        public TValue Value
        {
            get
            {
                try
                {
                    AssertValueLoaded();
                }
                catch
                {
                    IsFaulted = true;
                    throw;
                }

                return _value;
            }
        }

        /// <summary>
        /// Gets the pool that controls this resource.
        /// </summary>
        public SerializedResourcePool<TKey, TValue> Pool { get; private set; }

        public DateTime LastTouchUtc { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this is the first time this resource is retrieved (newly instantiated)
        /// </summary>
        /// <value><c>true</c> if this instance is new; otherwise, <c>false</c>.</value>
        public bool IsNew { get; private set; }

        internal bool IsFaulted { get; private set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance has expired.
        /// </summary>
        /// <value>
        ///     <c>true</c> if this instance has expired; otherwise, <c>false</c>.
        /// </value>
        internal bool HasExpired { get; set; }

        internal SerializedResource(TKey key, Func<TKey, TValue> factory, SerializedResourcePool<TKey, TValue> pool)
        {
            if (factory == null)
                throw new ArgumentNullException("factory");

            this._factory = factory;

            this.Key = key;
            this.Pool = pool;

            Touch();

            IsNew = true;
        }

        /// <summary>
        /// Asserts that the the value property is loaded from factory.
        /// </summary>
        internal void AssertValueLoaded()
        {
            if (_factory != null)
            {
                try
                {
                    _value = _factory(Key);
                }
                catch (Exception exc)
                {
                    throw new InvalidOperationException("Could not load value, factory threw exception. See inner exception for more information", exc);
                }

                _factory = null;
            }
        }

        /// <summary>
        /// Touches this instance thus updating the time until expiration (if this resource can expire).
        /// </summary>
        internal void Touch()
        {
            LastTouchUtc = DateTime.UtcNow;

            if (IsNew)
                IsNew = false;
        }

        /// <summary>
        /// Disposes this resource, returning it to the pool. Never use this object
        /// after it has been returned to the pool.
        /// </summary>
        public void Dispose()
        {
            Return();
        }

        /// <summary>
        /// Acquires an exclusive lock on the resouce.
        /// </summary>
        public void Enter()
        {
            Monitor.Enter(this);
        }

        /// <summary>
        /// Attempts to acquire an exclusive lock on the resouce.
        /// </summary>
        public bool TryEnter()
        {
            return Monitor.TryEnter(this);
        }

        /// <summary>
        /// Attempts to acquire an exclusive lock on the resouce.
        /// </summary>
        public bool TryEnter(TimeSpan timeout)
        {
            return Monitor.TryEnter(this, timeout);
        }

        /// <summary>
        /// Returns this resource to its associated resource pool. Never use this object
        /// after it has been returned to the pool.
        /// </summary>
        public void Return()
        {
            // It is important that we don't do any logic here since this method is called by the remove-method of the pool.
            IsNew = false;
            Monitor.Exit(this);
        }
    }
}